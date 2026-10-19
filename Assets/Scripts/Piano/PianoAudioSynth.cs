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

        /// <summary>合成采样率（Hz）：最高音 C8 的 4 次谐波仍低于 Nyquist，不产生混叠。</summary>
        private const int SampleRate = 44100;

        private AudioClip[] _clips;                    // 按 (pitch - MinPitch) 索引
        private AudioSource[] _sources;                // 槽位 → AudioSource（位于各自的子物体，保证滤波器路由确定）
        private AudioLowPassFilter[] _toneFilters;     // 槽位 → 音色低通（力度映射）
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

                int clipCount = PianoKeyMap.MaxPitch - PianoKeyMap.MinPitch + 1;
                _clips = new AudioClip[clipCount];
                for (int i = 0; i < clipCount; i++)
                {
                    _clips[i] = CreatePianoClip(PianoKeyMap.MinPitch + i, ClipDurationSeconds);
                    if (_clips[i] == null)
                        throw new System.InvalidOperationException($"AudioClip.Create 失败（pitch={PianoKeyMap.MinPitch + i}）");
                }

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
            var clip = _clips[e.Pitch - PianoKeyMap.MinPitch];
            if (src == null || clip == null) return;

            src.Stop();                       // 复用槽位前先停旧声，避免抢占后叠加
            src.clip = clip;
            src.volume = _allocator.GetVolume(slot);
            ApplyTone(slot, e.Velocity);
            src.Play();
        }

        /// <summary>Note Off：把该音高的声部转入淡出释放（未发声的音高为安全空操作）。</summary>
        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (!Initialized || _allocator == null) return;
            _allocator.NoteOff(e.Pitch);
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
        /// 程序化波形合成（基频 + 2/3/4 次谐波 + 指数衰减）。
        /// 零采样依赖、零第三方资源 —— 可作长期降级模式（方案 7.3）。
        /// </summary>
        private static AudioClip CreatePianoClip(int pitch, float duration)
        {
            float freq = 440f * Mathf.Pow(2f, (pitch - 69) / 12f);
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-2.5f * t);
                float v = Mathf.Sin(2f * Mathf.PI * freq * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * 2f * freq * t)
                        + 0.25f * Mathf.Sin(2f * Mathf.PI * 3f * freq * t)
                        + 0.12f * Mathf.Sin(2f * Mathf.PI * 4f * freq * t);
                data[i] = v * env * 0.25f;
            }

            var clip = AudioClip.Create($"piano_{pitch}", samples, 1, SampleRate, false);
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
