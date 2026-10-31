using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 音频模块（方案 3.3 数据流第 5 步 / <b>方案 7.3 音频优先级</b> / 审查 1.3）。
    /// <para>
    /// <b>发声方式</b>：由 <c>AudioSource.PlayOneShot</c> 改为<b>受控发声池</b>（<see cref="PianoVoiceAllocator"/> + 固定数量的
    /// <see cref="AudioSource"/>）。每个声部槽位持有自己的 AudioSource，长音符按 Note Off 在 0.12s 内余弦淡出释放，
    /// 因此暂停、拖动进度、切曲、A-B 循环边界都能通过 <see cref="ReleaseAll"/> 把<b>所有</b>已发声音符正确释放，
    /// 不会再出现 PlayOneShot 无法停止导致的"切曲后仍在响"。
    /// </para>
    /// <para>
    /// <b>音频路径零分配（方案 7.3）</b>：<see cref="OnNoteOn"/>、<see cref="OnNoteOff"/> 与 <c>Update</c> 不做任何
    /// <c>new List/数组/闭包</c>；所有 <see cref="AudioClip"/> 在 <see cref="Initialize"/> 中按音高一次性预生成并复用。
    /// </para>
    /// <para>
    /// <b>力度响应</b>：音量按 <see cref="PianoVoiceAllocator.VelocityGain"/> 线性映射（含最小底噪）；
    /// 音色通过每声部的 <see cref="AudioLowPassFilter"/> 截止频率随力度升降（弱奏更暗、强奏更亮），
    /// 不需要为每个力度档重复生成采样，内存与原有的"每音高 1 份 clip"完全一致。
    /// </para>
    /// <para>
    /// <b>初始化失败防护</b>：音频设备不可用或 <see cref="AudioClip.Create"/> 失败时<b>不抛异常</b>，
    /// 置 <see cref="Initialized"/> = false 并写入中文 <see cref="LastInitError"/>；上层据此上报"音频不可用"警告并允许静音继续。
    /// 此时 <see cref="OnNoteOn"/> 静默返回。
    /// </para>
    /// <para>
    /// <b>许可说明（方案 5.4 / 7.3）</b>：当前使用<b>程序化波形合成</b>（基频 + 2/3/4 次谐波 + 指数衰减），
    /// 零采样依赖、零第三方资源，可作为长期降级模式保留。若后续替换为采样音色，必须选用明确 CC0 / 公有领域或已授权的采样，
    /// 并在 <c>Docs/asset-licenses.md</c> 登记来源与许可；同时评估<b>延迟、长音衰减、力度响应、同时发声数、许可条件</b>
    /// 五项指标后再决定是否替换。
    /// </para>
    /// </summary>
    public class PianoAudioSynth : MonoBehaviour
    {
        /// <summary>声部池容量上限：超过时按"正在淡出的声部 → 最弱 → 最旧"抢占，不得无上限新建 AudioSource。</summary>
        public const int MaxVoices = 24;

        /// <summary>Note Off 默认淡出时长（秒）。</summary>
        public const float DefaultFadeSeconds = 0.12f;

        /// <summary>合成波形时长（秒）：与改造前一致，保证内存占用量级不变。</summary>
        private const float ClipDurationSeconds = 2.5f;

        /// <summary>
        /// 制音释放声时长（秒）。松键时制音器落弦的闷响，很短——只是反馈，不是音效。
        /// </summary>
        private const float DamperDurationSeconds = 0.09f;

        /// <summary>制音声相对主音量的比例：必须很低，否则快速连奏会变成「咚咚」伴奏。</summary>
        private const float DamperVolume = 0.10f;

        /// <summary>按音高展开的最大声像偏移（0 = 居中，1 = 完全偏一侧）。0.30 刚好听得出左右，不夸张。</summary>
        private const float MaxPan = 0.30f;

        /// <summary>合成采样率（Hz）：最高音 C8 的 4 次谐波仍低于 Nyquist，不产生混叠。</summary>
        private const int SampleRate = 44100;

        private AudioClip[] _clips;                    // 按 (pitch - MinPitch) 索引
        private AudioSource[] _sources;                // 槽位 → AudioSource（位于各自的子物体，保证滤波器路由确定）
        private AudioLowPassFilter[] _toneFilters;     // 槽位 → 音色低通（力度映射）
        private AudioSource _damperSource;             // 制音声专用（不占 24 声部池）
        private AudioClip _damperClip;
        private PianoVoiceAllocator _allocator;
        private bool _initStarted;
        private bool _muted;

        /// <summary>音频是否可用。false 时 <see cref="OnNoteOn"/> 静默返回，上层应提示"音频不可用"并允许静音播放。</summary>
        public bool Initialized { get; private set; }

        /// <summary>最近一次初始化失败的原因（中文）。成功时为 null。</summary>
        public string LastInitError { get; private set; }

        /// <summary>主音量，取值 [0,1]。</summary>
        public float MasterVolume { get; private set; } = 1f;

        /// <summary>
        /// 静音开关。置 true 会立即释放所有在发声部；静音期间 <see cref="OnNoteOn"/> 不再占用声部。
        /// </summary>
        public bool Muted
        {
            get => _muted;
            set
            {
                if (_muted == value) return;
                _muted = value;
                if (value) ReleaseAll();
            }
        }

        /// <summary>正在发声（未进入释放淡出）的声部数。淡出中的声部不再计入，但其槽位仍占用至淡出结束。</summary>
        public int ActiveVoiceCount => _allocator != null ? _allocator.ActiveCount : 0;

        /// <summary>声部池容量（= <see cref="MaxVoices"/>，供诊断/回归对照）。</summary>
        public int VoiceCapacity => _allocator != null ? _allocator.Capacity : MaxVoices;

        /// <summary>累计 voice-stealing 次数（池满抢占），供性能与音频回归诊断（优化方案 P1-04）。</summary>
        public int VoiceStealCount => _allocator != null ? _allocator.StealCount : 0;

        private void Update()
        {
            if (_allocator == null || _sources == null) return;

            // 用 unscaled 时间推进淡出，避免 timeScale 变化/暂停影响释放进度（方案 7.3）
            _allocator.AdvanceReleases(Time.unscaledDeltaTime);

            for (int i = 0; i < _sources.Length; i++)
            {
                var src = _sources[i];
                if (src == null) continue;

                if (_allocator.IsOccupied(i))
                {
                    src.volume = _allocator.GetVolume(i);   // 力度 × 主音量 × 淡出曲线
                }
                else if (src.isPlaying)
                {
                    src.Stop();                              // 淡出完成、槽位已归还 → 停声
                }
            }
        }

        /// <summary>
        /// 初始化声部池与音高采样。幂等；失败不抛异常（写 <see cref="LastInitError"/>）。
        /// </summary>
        public void Initialize()
        {
            if (_initStarted) return;   // 幂等：避免重复创建 AudioSource / AudioClip
            _initStarted = true;

            LastInitError = null;
            Initialized = false;

            if (!Application.isPlaying)
            {
                LastInitError = "音频未初始化：Editor 编辑态（非播放模式）不创建发声池。";
                return;
            }

            if (AudioSettings.outputSampleRate <= 0)
            {
                LastInitError = "音频未初始化：未检测到可用的音频输出设备（outputSampleRate ≤ 0）。";
                return;
            }

            try
            {
                _allocator = new PianoVoiceAllocator(MaxVoices, DefaultFadeSeconds);
                _allocator.SetMasterVolume(MasterVolume);

                // P0-3：不再在初始化时一次性合成 88 段波形（≈38.8 MB 常驻）。
                // 只分配索引数组，真正弹到的音高才在 GetClip 里按需合成并缓存。
                int clipCount = PianoKeyMap.MaxPitch - PianoKeyMap.MinPitch + 1;
                _clips = new AudioClip[clipCount];

                _sources = new AudioSource[MaxVoices];
                _toneFilters = new AudioLowPassFilter[MaxVoices];
                for (int v = 0; v < MaxVoices; v++)
                {
                    var voiceGo = new GameObject($"Voice_{v}");
                    voiceGo.transform.SetParent(transform, false);

                    var src = voiceGo.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    src.spatialBlend = 0f;   // 2D：教学场景不需要空间衰减
                    src.volume = 0f;

                    var lp = voiceGo.AddComponent<AudioLowPassFilter>();
                    lp.lowpassResonanceQ = 1f;

                    _sources[v] = src;
                    _toneFilters[v] = lp;
                }

                // 制音释放声：独立 AudioSource + 一次性小 clip（约 8 KB），松键时 PlayOneShot
                var damperGo = new GameObject("Damper");
                damperGo.transform.SetParent(transform, false);
                _damperSource = damperGo.AddComponent<AudioSource>();
                _damperSource.playOnAwake = false;
                _damperSource.loop = false;
                _damperSource.spatialBlend = 0f;
                _damperSource.volume = DamperVolume * MasterVolume;
                _damperClip = CreateDamperClip();

                Initialized = true;
            }
            catch (System.Exception ex)
            {
                Initialized = false;
                LastInitError = $"音频初始化失败：{ex.Message}";
                Debug.LogWarning($"[PianoAudioSynth] {LastInitError}（已降级为静音播放，琴键与时间轴不受影响）");
            }
        }

        /// <summary>Note On：从声部池取一个槽位发声，音量/音色由 velocity（力度）决定。</summary>
        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (!Initialized || _muted || _allocator == null || _sources == null) return;
            if (!PianoKeyMap.IsInRange(e.Pitch)) return;

            int slot = _allocator.NoteOn(e.Pitch, e.Velocity);
            if (slot < 0 || slot >= _sources.Length) return;

            var src = _sources[slot];
            var clip = GetClip(e.Pitch);
            if (src == null || clip == null) return;

            src.Stop();                       // 复用槽位前先停旧声，避免抢占后叠加
            src.clip = clip;
            src.volume = _allocator.GetVolume(slot);
            // 立体声展开（写实化批次 6）：按音高分配声像（低音偏左、高音偏右）。
            // 用播放端 panStereo 而不是双声道波形——听感一致但每个音高省一半常驻内存。
            float panT = Mathf.InverseLerp(PianoKeyMap.MinPitch, PianoKeyMap.MaxPitch, e.Pitch);
            src.panStereo = Mathf.Lerp(-MaxPan, MaxPan, panT);
            ApplyTone(slot, e.Velocity);
            src.Play();
        }

        /// <summary>
        /// 按需合成并缓存某音高的波形（P0-3）：初始化只建索引数组，内存随实际弹到的音高增长，
        /// 典型曲目（20~30 个音高）只需 9~13 MB，而不是无条件的 38.8 MB。
        /// 合成一段 2.5 s 波形在实机上为毫秒级以内，首次触发不会造成可感知的卡顿。
        /// </summary>
        private AudioClip GetClip(int pitch)
        {
            int index = pitch - PianoKeyMap.MinPitch;
            if (_clips == null || index < 0 || index >= _clips.Length) return null;

            var cached = _clips[index];
            if (cached != null) return cached;

            var clip = CreatePianoClip(pitch, ClipDurationSeconds);
            if (clip == null)
            {
                // 单个音高失败只降级为该音静音，不再中断整池初始化（原实现会整个抛异常）
                LastInitError = $"AudioClip.Create 失败（pitch={pitch}），该音高将静音";
                Debug.LogWarning($"[PianoAudioSynth] {LastInitError}", this);
                return null;
            }

            _clips[index] = clip;
            return clip;
        }

        /// <summary>
        /// Note Off：把该音高的声部转入淡出释放（未发声的音高为安全空操作）。
        /// 只有「确实有正在发声的声部被释放」时才播放制音声，避免空松键也出声。
        /// </summary>
        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (!Initialized || _allocator == null) return;

            int slot = _allocator.NoteOff(e.Pitch);
            if (slot < 0) return;                     // 该音根本没在响（或已被抢占）

            if (_muted || _damperSource == null || _damperClip == null) return;
            _damperSource.volume = DamperVolume * MasterVolume;
            _damperSource.PlayOneShot(_damperClip);   // 允许重叠：连奏松键本来就密集
        }

        /// <summary>
        /// 释放所有在发声部：立即把它们标记为淡出（槽位逻辑上立刻可复用），并在 0.12s 内停声并归还池。
        /// 用于停止 / 暂停 / 拖动进度 / 切曲 / A-B 循环边界（方案 7.3）。
        /// </summary>
        public void ReleaseAll()
        {
            _allocator?.ReleaseAll();
        }

        /// <summary>设置主音量，自动 clamp 到 [0,1]；对当前在发声部立即生效（Update 每帧回写 volume）。</summary>
        public void SetMasterVolume(float v)
        {
            MasterVolume = Mathf.Clamp01(v);
            _allocator?.SetMasterVolume(MasterVolume);
            if (_damperSource != null) _damperSource.volume = DamperVolume * MasterVolume;
        }

        // ==================== 内部 ====================

        /// <summary>力度 → 音色明暗：弱奏更暗、强奏更亮（零额外采样内存）。</summary>
        private void ApplyTone(int slot, int velocity)
        {
            if (_toneFilters == null || slot >= _toneFilters.Length) return;
            var lp = _toneFilters[slot];
            if (lp == null) return;

            float v = Mathf.Clamp01(velocity / 127f);
            lp.cutoffFrequency = Mathf.Lerp(1400f, 20000f, v);
        }

        /// <summary>
        /// 程序化波形合成（写实化批次 6）：非谐分音 + 分音独立衰减 + 击弦瞬态。
        /// <para>
        /// 物理依据：真实琴弦不是理想弦，分音频率为 <c>n·f0·√(1 + B·n²)</c>（B = 非谐系数，
        /// 低音弦更长更粗 → B 更大）；高次分音能量衰减更快（τ_n ≈ τ_1 / n^0.6）；
        /// 起音有槌头接触琴弦的非周期瞬态噪声。三者叠加才不是「电子风琴」。
        /// </para>
        /// <para>
        /// <b>为什么是单声道</b>：立体声合成会让每个音高的常驻内存翻倍（0.44 → 0.88 MB，
        /// 极端情况 88 键 77 MB）。左右展开改由播放端的 <c>AudioSource.panStereo</c> 按音高
        /// 分配（低音偏左、高音偏右），听感上同样是「左右分布」，但不额外占内存。
        /// </para>
        /// <para>
        /// 零采样依赖、零第三方资源 —— 可作长期降级模式（方案 7.3）。
        /// </para>
        /// </summary>
        private static AudioClip CreatePianoClip(int pitch, float duration)
        {
            float freq = 440f * Mathf.Pow(2f, (pitch - 69) / 12f);
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];

            // 非谐系数：低音端（琴弦长而粗）非谐强，高音端接近理想弦
            float keyT = Mathf.InverseLerp(PianoKeyMap.MinPitch, PianoKeyMap.MaxPitch, pitch);
            float inharmonicity = Mathf.Lerp(0.00035f, 0.00006f, keyT);

            // 分音数：低音区 9 个、每升高一个八度减 1，最少 3 个
            int partials = Mathf.Clamp(9 - (pitch - PianoKeyMap.MinPitch) / 12, 3, 9);

            const float attackSeconds = 0.002f;   // 极短起音（击弦本身就是瞬态，不要淡入感）
            for (int n = 1; n <= partials; n++)
            {
                float partialFreq = n * freq * Mathf.Sqrt(1f + inharmonicity * n * n);
                if (partialFreq >= SampleRate * 0.45f) break;   // 防混叠

                // 分音幅度 1/n^1.15：比 1/n 稍陡，接近钢琴频谱的自然倾斜
                float amp = 1f / Mathf.Pow(n, 1.15f);
                // 分音独立衰减：高次分音衰减更快（低音「嗡」很快消失，只剩基频）
                float tau = 1.6f / Mathf.Pow(n, 0.6f);
                float omega = 2f * Mathf.PI * partialFreq / SampleRate;

                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-t / tau) * Mathf.Min(1f, t / attackSeconds);
                    data[i] += Mathf.Sin(omega * i) * amp * env;
                }
            }

            // 击弦瞬态噪声（槌头接触琴弦的非周期成分）：一阶低通避免刺耳，固定种子保证可复现
            var rng = new System.Random(pitch * 7919 + 13);
            float noiseState = 0f;
            int strikeSamples = Mathf.Min(samples, (int)(0.008f * SampleRate));
            for (int i = 0; i < strikeSamples; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t / 0.0028f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                noiseState += (white - noiseState) * 0.35f;
                data[i] += noiseState * env * 0.10f;
            }

            // 峰值归一化到 0.85：分音数随音高变化，不归一化会让高低音音量差得远
            float peak = 0f;
            for (int i = 0; i < samples; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak > 1e-6f)
            {
                float gain = 0.85f / peak;
                for (int i = 0; i < samples; i++) data[i] *= gain;
            }

            var clip = AudioClip.Create($"piano_{pitch}", samples, 1, SampleRate, false);
            if (clip == null) return null;
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 制音释放声（写实化批次 6）：松键时制音器落到琴弦上的短促闷响。
        /// 重低通噪声 + 22 ms 指数衰减，音量很低——只是让「松键」有声音反馈，不是音效。
        /// </summary>
        private static AudioClip CreateDamperClip()
        {
            int samples = Mathf.Max(1, (int)(DamperDurationSeconds * SampleRate));
            var data = new float[samples];
            var rng = new System.Random(9187);
            float state = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t / 0.022f);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                state += (white - state) * 0.12f;      // 重低通 → 闷响而非「嘶」
                data[i] = state * env;
            }

            var clip = AudioClip.Create("piano_damper", samples, 1, SampleRate, false);
            if (clip == null) return null;
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>
    /// 受控发声池的<b>纯逻辑分配器</b>（非 MonoBehaviour，可脱离音频设备单独单测）。
    /// <para>
    /// 职责：声部槽位的占用/复用、Note Off 淡出进度、池满时的抢占策略、力度增益与主音量。
    /// 不持有任何 UnityEngine 原生对象，不产生托管分配，可安全用于每帧调用。
    /// </para>
    /// <para>
    /// 状态语义：
    /// <list type="bullet">
    /// <item><description><b>空闲</b>：<c>_pitch &lt; 0</c>，槽位可被任意抢占复用。</description></item>
    /// <item><description><b>发声</b>：<c>_pitch ≥ 0</c> 且未进入淡出，计入 <see cref="ActiveCount"/>。</description></item>
    /// <item><description><b>淡出</b>：<c>_pitch ≥ 0</c> 且 <c>_fade ≥ 0</c>，不计入 <see cref="ActiveCount"/> 但计入
    /// <see cref="OccupiedCount"/>；淡出进度到 1 后槽位真正归还。</description></item>
    /// </list>
    /// 因此 <see cref="ReleaseAll"/> 之后 <see cref="ActiveCount"/> 立即为 0（满足"切曲/暂停后不再有在发声部"的语义），
    /// 而声音仍在 0.12s 内自然淡出，不会有爆音。
    /// </para>
    /// </summary>
    public sealed class PianoVoiceAllocator
    {
        /// <summary>默认声部池容量。</summary>
        public const int DefaultCapacity = 24;

        /// <summary>默认淡出时长（秒）。</summary>
        public const float DefaultFadeSeconds = 0.12f;

        /// <summary>力度增益下限：避免 velocity 极小时完全无声（保留可闻底噪）。</summary>
        private const float MinVelocityGain = 0.10f;

        private const float GainEpsilon = 1e-4f;

        private readonly int[] _pitch;    // -1 = 空闲
        private readonly float[] _gain;   // 力度增益（不含淡出与主音量）
        private readonly float[] _fade;   // < 0 = 未释放；[0,1] = 淡出进度
        private readonly long[] _order;   // 分配序号（越大越新）
        private long _sequence;
        private float _masterVolume = 1f;

        /// <summary>声部池容量（AudioSource 槽位数）。</summary>
        public int Capacity { get; }

        /// <summary>淡出时长（秒）。</summary>
        public float FadeSeconds { get; }

        /// <summary>正在发声（未进入淡出）的声部数。</summary>
        public int ActiveCount { get; private set; }

        /// <summary>被占用的槽位数（正在发声 + 正在淡出）。</summary>
        public int OccupiedCount { get; private set; }

        /// <summary>
        /// 累计 voice-stealing 次数：池满且没有空闲槽位时抢占已有声部的次数
        /// （优化方案 P1-04「增加最大同时发声数与 voice-stealing 计数」，用于高密度和弦诊断）。
        /// </summary>
        public int StealCount { get; private set; }

        /// <summary>主音量，取值 [0,1]。</summary>
        public float MasterVolume => _masterVolume;

        public PianoVoiceAllocator(int capacity = DefaultCapacity, float fadeSeconds = DefaultFadeSeconds)
        {
            Capacity = Mathf.Max(1, capacity);
            FadeSeconds = fadeSeconds > 0.001f ? fadeSeconds : DefaultFadeSeconds;

            _pitch = new int[Capacity];
            _gain = new float[Capacity];
            _fade = new float[Capacity];
            _order = new long[Capacity];
            for (int i = 0; i < Capacity; i++)
            {
                _pitch[i] = -1;
                _fade[i] = -1f;
            }
        }

        /// <summary>设置主音量，自动 clamp 到 [0,1]。</summary>
        public void SetMasterVolume(float v) => _masterVolume = Mathf.Clamp01(v);

        /// <summary>槽位是否被占用（正在发声或正在淡出）。</summary>
        public bool IsOccupied(int slot) => slot >= 0 && slot < Capacity && _pitch[slot] >= 0;

        /// <summary>槽位是否正在淡出释放。</summary>
        public bool IsReleasing(int slot) => IsOccupied(slot) && _fade[slot] >= 0f;

        /// <summary>槽位当前音高；空闲返回 -1。</summary>
        public int GetPitch(int slot) => IsOccupied(slot) ? _pitch[slot] : -1;

        /// <summary>槽位当前应输出的音量（力度增益 × 主音量 × 余弦淡出曲线）。</summary>
        public float GetVolume(int slot)
        {
            if (!IsOccupied(slot)) return 0f;

            float fade = _fade[slot];
            float releaseGain = fade < 0f ? 1f : CosineFade(Mathf.Clamp01(fade));
            return _masterVolume * _gain[slot] * releaseGain;
        }

        /// <summary>
        /// 触发一个音符：同音高复用原槽位；否则取空闲槽位；池满时按"正在淡出 → 最弱 → 最旧"抢占。
        /// 返回分配的槽位索引，容量非法时为 -1。
        /// </summary>
        public int NoteOn(int pitch, int velocity)
        {
            if (pitch < 0) return -1;

            int slot = FindByPitch(pitch);
            if (slot < 0) slot = FindFree();
            if (slot < 0)
            {
                slot = FindVictim();
                if (slot >= 0) StealCount++; // 池满抢占：扣一次 voice-stealing
            }
            if (slot < 0) return -1;

            _pitch[slot] = pitch;
            _gain[slot] = VelocityGain(velocity);
            _fade[slot] = -1f;
            _order[slot] = ++_sequence;

            RecountActive();
            return slot;
        }

        /// <summary>把该音高的声部转入淡出释放；未发声的音高返回 -1（安全空操作）。</summary>
        public int NoteOff(int pitch)
        {
            int slot = FindByPitch(pitch);
            if (slot < 0) return -1;

            if (_fade[slot] < 0f) _fade[slot] = 0f;
            RecountActive();
            return slot;
        }

        /// <summary>把所有被占用的槽位标记为淡出释放；<see cref="ActiveCount"/> 立即归零。</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] >= 0) _fade[i] = 0f;
            }
            RecountActive();
        }

        /// <summary>
        /// 推进所有淡出中的槽位；完成的槽位归还池。返回本次归还的数量。供每帧调用，零分配。
        /// </summary>
        public int AdvanceReleases(float deltaTime)
        {
            if (deltaTime <= 0f) return 0;

            float step = deltaTime / FadeSeconds;
            int completed = 0;

            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] < 0 || _fade[i] < 0f) continue;

                _fade[i] += step;
                if (_fade[i] >= 1f)
                {
                    _pitch[i] = -1;
                    _fade[i] = -1f;
                    _gain[i] = 0f;
                    _order[i] = 0;
                    completed++;
                }
            }

            if (completed > 0) RecountActive();
            return completed;
        }

        /// <summary>力度 → 增益：线性映射并保留最小可闻底噪。</summary>
        public static float VelocityGain(int velocity)
        {
            float v = Mathf.Clamp01(velocity / 127f);
            return MinVelocityGain + (1f - MinVelocityGain) * v;
        }

        /// <summary>余弦淡出曲线：fade=0 → 1，fade=1 → 0，两端切线平缓（无爆音）。</summary>
        private static float CosineFade(float fade) => 0.5f * (1f + Mathf.Cos(Mathf.PI * fade));

        private int FindByPitch(int pitch)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] == pitch) return i;
            }
            return -1;
        }

        private int FindFree()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] < 0) return i;
            }
            return -1;
        }

        /// <summary>抢占选择：优先正在淡出的声部（最旧），其次最弱，再次最旧。</summary>
        private int FindVictim()
        {
            int victim = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] < 0) continue;
                if (victim < 0) { victim = i; continue; }

                bool victimReleasing = _fade[victim] >= 0f;
                bool candidateReleasing = _fade[i] >= 0f;

                if (candidateReleasing != victimReleasing)
                {
                    if (candidateReleasing) victim = i;          // 腾出正在淡出的声部优先
                    continue;
                }

                float victimGain = _gain[victim];
                float candidateGain = _gain[i];
                if (candidateGain < victimGain - GainEpsilon)
                {
                    victim = i;                                   // 更弱的声部优先
                }
                else if (Mathf.Abs(candidateGain - victimGain) <= GainEpsilon && _order[i] < _order[victim])
                {
                    victim = i;                                   // 同样弱则抢占更旧的
                }
            }
            return victim;
        }

        /// <summary>重算发声/占用计数（容量 ≤ 24，成本可忽略，且完全无分配）。</summary>
        private void RecountActive()
        {
            int active = 0;
            int occupied = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pitch[i] < 0) continue;
                occupied++;
                if (_fade[i] < 0f) active++;
            }
            ActiveCount = active;
            OccupiedCount = occupied;
        }
    }
}
