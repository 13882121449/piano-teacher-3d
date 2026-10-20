using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 课程会话控制器（方案 9.1「保留核心门面，避免控制器无限膨胀」）。
    /// <para>
    /// 职责：<b>会话协调 + 公开命令</b>。底层算法细节已拆分到各模块
    /// （<see cref="SongLibraryService"/> 曲库 / <see cref="ErrorReporter"/> 错误 /
    /// <see cref="LessonTimeline"/> 时间轴 / <see cref="MidiParser"/> + <see cref="MidiEventScheduler"/> 解析调度 /
    /// <see cref="PianoAudioSynth"/> 音频 / <see cref="AvatarManager"/> 角色 /
    /// <see cref="CameraModeController"/> 视角 / <see cref="PianoSceneEnvironment"/> 场景环境 /
    /// <c>UI</c> 展示层）。
    /// </para>
    /// <para>
    /// 铁律（方案 6.1 / 13 取舍 2）：<see cref="LessonTimeline"/> 是唯一权威播放时间来源，
    /// 音符调度、音频、琴键、手部、节拍器、进度条与预提示全部从它读取；任何模块不得自建计时器。
    /// </para>
    /// <para>
    /// 会话状态（方案 6.2）：<see cref="LessonState"/> 显式定义，切曲走「校验 → 临时解析 → 一次性提交 →
    /// 清理旧状态 → 更新 UI」，任一步失败回滚到明确状态，不留「标题是新曲、音符仍是旧曲」的混合状态。
    /// </para>
    /// </summary>
    public class LessonSessionController : MonoBehaviour
    {
        // ==================== 表现层模块 ====================
        private LessonTimeline _timeline;
        private MidiEventScheduler _scheduler;
        private PianoKeyMap _keyMap;
        private PianoKeyAnimator _keyAnimator;
        private PianoAudioSynth _audio;
        private PianoKeyHighlighter _highlighter;
        private Metronome _metronome;
        private AvatarManager _avatar;
        private HandIKController _handIK;
        private FingerPoseController _fingerPose;
        private CameraModeController _camera;
        private PianoSceneEnvironment _environment;
        private ErrorReporter _reporter;

        // ==================== 服务（方案 9.1 / 9.2） ====================
        private readonly SongLibraryService _library = new SongLibraryService();
        private readonly UserPreferences _prefs = new UserPreferences();

        /// <summary>照片外观管线（方案 9.2）；构造时不产生任何网络或用户数据写入。</summary>
        private CharacterCustomizationService _customization;
        private PrivacyConsentController _consent;

        /// <summary>指法提供器（方案 5.3 / 10）：默认启发式，可被替换为人工编辑或模型推断。</summary>
        private readonly IFingeringProvider _fingering = new HeuristicFingeringProvider();

        /// <summary>
        /// 双手共用的「手指 ↔ 音高」分配（方案 5.3 第二阶段）：
        /// 手指姿态控制器与手部 IK 读同一份结果，因此弯曲的手指必然就是落在对应琴键上的手指。
        /// </summary>
        private FingerAssignmentService _fingerAssignment;

        // ==================== 会话状态 ====================
        private LessonState _state = LessonState.NoSong;
        private MidiParseResult _currentSong;
        private string _songId = "";
        private string _songSourcePath = "";
        private PracticeHand _practiceHand = PracticeHand.Both;
        private float _lastTime; // 用于检测 Loop/Seek 导致的时间回退

        private SongAnalysis _analysis = new SongAnalysis();
        private readonly List<MidiNoteEvent> _upcomingBuffer = new();
        private readonly List<MidiNoteEvent> _handLeadBuffer = new();
        private float _highlightLookAhead = 0.6f;

        /// <summary>
        /// 手部预置窗口（方案 5.3 第二阶段 / 4.2.3 轻量预测）：
        /// 音符到来前这段时间内，该手先移向对应琴键，避免「换位途中错过音符」。
        /// </summary>
        public float HandLeadSeconds { get; set; } = 0.25f;

        private bool _loading;             // 加载事务进行中（方案 6.2）→ 面态为 Loading，且防重入
        private bool _cancelRequested;     // 加载取消请求（方案 6.4「可取消状态」）
        private bool _audioDegraded;       // 音频不可用（静音继续），只上报一次
        private bool _suppressPrefsWrite;  // 应用偏好时不回写（避免无意义落盘）

        // ---- 可取消分阶段加载（优化方案 §7-8）----
        private SongLoadJob _loadJob;      // 进行中的后台作业（null = 没有）
        private bool _loadJobIsImport;     // 作业结果是否要走「复制进用户曲库」分支
        private SongLoadStage _lastLoadStage = SongLoadStage.Idle;

        // ---- 显示模式（展示 / 教学）----
        private DisplayMode _displayMode = DisplayMode.Showcase;
        private bool _hasHighlightToRestore;
        private HighlightMode _highlightToRestore = HighlightMode.Off;

        // 当前按下的音高集合（教学模式给相机提供取景范围；也供"手指↔琴键"一致性验证）
        private readonly HashSet<int> _pressedPitches = new HashSet<int>();
        private bool _pressedSpanValid;
        private int _pressedMinPitch;
        private int _pressedMaxPitch;
        private bool _prefsDirty;
        private float _prefsNextSaveAt;

        // ==================== 只读状态 ====================

        /// <summary>会话状态（方案 6.2）。</summary>
        public LessonState State => _state;

        /// <summary>是否正在加载/解析曲目。</summary>
        public bool IsLoading => _state == LessonState.Loading;

        /// <summary>
        /// 当前加载阶段（优化方案 §7-8）。没有后台作业时为 <see cref="SongLoadStage.Idle"/>。
        /// UI 据此显示「读取文件 / 校验并解析 / 分析曲目」，<b>不显示虚构百分比</b>。
        /// </summary>
        public SongLoadStage LoadStage => _loadJob != null ? _loadJob.Stage : SongLoadStage.Idle;

        /// <summary>加载阶段文案（空字符串表示当前没有进行中的加载）。</summary>
        public string LoadStageLabel => _loadJob != null ? SongLoadJob.StageLabel(_loadJob.Stage) : string.Empty;

        /// <summary>是否已有可用曲目。</summary>
        public bool HasSong => _currentSong != null && _currentSong.Success;

        /// <summary>当前曲名（已加载曲目时非空）。</summary>
        public string CurrentSongName { get; private set; } = "";

        /// <summary>当前曲目的安全内部 ID（不含完整路径，可用于上下文与最近列表）。</summary>
        public string CurrentSongId => _songId;

        public PlaybackState PlaybackState => _timeline != null ? _timeline.State : PlaybackState.Stopped;

        /// <summary>当前播放时间（秒），唯一来源是 <see cref="LessonTimeline"/>。</summary>
        public float CurrentTime => _timeline != null ? _timeline.CurrentTime : 0f;

        /// <summary>当前曲目总时长（秒）。</summary>
        public float TotalDuration => _timeline != null ? _timeline.TotalDuration : 0f;

        public float PlaybackSpeed => _timeline != null ? _timeline.PlaybackSpeed : 1f;
        public CameraMode CameraMode => _camera != null ? _camera.Mode : CameraMode.Full;
        public PracticeHand PracticeHand => _practiceHand;
        public bool IsLooping => _timeline != null && _timeline.IsLooping;
        public int CurrentAvatarIndex => _avatar != null ? _avatar.CurrentIndex : 0;

        /// <summary>当前琴键高亮模式。</summary>
        public HighlightMode KeyHighlight => _highlighter != null ? _highlighter.Mode : HighlightMode.Off;

        /// <summary>节拍器是否开启。</summary>
        public bool MetronomeEnabled => _metronome != null && _metronome.Enabled;

        /// <summary>每小节拍数（默认 4/4）。</summary>
        public int BeatsPerBar => _metronome != null ? _metronome.BeatsPerBar : 4;

        /// <summary>当前曲目分析结果（未加载曲目时为空的非 null 结果）。</summary>
        public SongAnalysis Analysis => _analysis;

        /// <summary>
        /// 手部动作质量报告（方案阶段五验收量化）。未装配手部 IK 时返回 null。
        /// </summary>
        public HandMotionReport HandMotion => _handIK != null ? _handIK.BuildMotionReport() : null;

        /// <summary>累计检测到并已钳制的异常姿态（穿模风险）次数；0 表示全程无穿模。</summary>
        public int PoseIssueCount => _handIK != null ? _handIK.PoseIssueCount : 0;

        /// <summary>最近一次上报的错误（方案 8.1）。无错误时为 default。</summary>
        public AppError LastError => _reporter != null ? _reporter.Last : default;

        /// <summary>最近曲目（方案 6.4：只保存必要元数据）。</summary>
        public IReadOnlyList<SongMetadata> RecentSongs => _library.RecentSongs;

        /// <summary>当前画质档位（方案 7.1）。</summary>
        public QualityTier CurrentQualityTier { get; private set; } = QualityTier.Standard;

        /// <summary>琴键预提示提前量（秒），clamp 到 [0.05, 3]。</summary>
        public float HighlightLookAhead
        {
            get => _highlightLookAhead;
            set
            {
                _highlightLookAhead = Mathf.Clamp(value, 0.05f, 3f);
                if (!_suppressPrefsWrite) { _prefs.HighlightLookAhead = _highlightLookAhead; MarkPrefsDirty(); }
            }
        }

        /// <summary>当前视角的排他性状态文案（"手部特写" / "全景" / "自由观察"）。</summary>
        public string CameraModeLabel => _camera != null ? _camera.ModeLabel : "全景";

        /// <summary>音频是否可用（不可用时允许静音继续，方案 8.2）。</summary>
        public bool AudioAvailable => _audio != null && _audio.Initialized;

        /// <summary>本次会话是否处于「音频降级（静音）」，供 UI 显示音频状态。</summary>
        public bool AudioDegraded => _audioDegraded;

        // ==================== 事件（供 UI / 会话外层订阅） ====================

        public event Action<string> OnError;                      // 兼容通道：面向用户的中文提示
        public event Action<AppError> OnAppError;                 // 结构化错误（方案 8.1）
        public event Action<LessonState> OnLessonStateChanged;
        public event Action OnSongLoaded;
        public event Action<PlaybackState> OnPlaybackStateChanged;
        public event Action<float, float> OnProgressChanged;      // current, total
        public event Action<int> OnAvatarChanged;
        public event Action<CameraMode> OnCameraModeChanged;
        public event Action<PracticeHand> OnPracticeHandChanged;
        public event Action<HighlightMode> OnHighlightModeChanged;
        public event Action<bool> OnMetronomeToggled;
        public event Action<int> OnMetronomeBeat;                 // 参数：小节内拍序号（0 = 强拍）
        public event Action<SongAnalysis> OnAnalysisReady;

        /// <summary>加载阶段变化（Queued → Reading → Validating → Analyzing → Ready/Failed/Cancelled）。</summary>
        public event Action<SongLoadStage> OnLoadStageChanged;

        /// <summary>显示模式（展示 / 教学）。</summary>
        public DisplayMode DisplayMode => _displayMode;

        /// <summary>是否处于教学模式（隐藏手部、仅高亮被按下的琴键）。</summary>
        public bool IsTeachingMode => _displayMode == DisplayMode.Teaching;

        /// <summary>教学模式强制「仅高亮被按下的琴键」时，返回实际生效的高亮模式。</summary>
        public HighlightMode EffectiveHighlightMode => _highlighter != null ? _highlighter.Mode : HighlightMode.Off;

        /// <summary>当前被按下的琴键音高数量（教学模式取景与验证用）。</summary>
        public int PressedPitchCount => _pressedPitches.Count;

        /// <summary>取当前按下琴键的音高范围（无按下时为 false）。</summary>
        public bool TryGetPressedPitchSpan(out int minPitch, out int maxPitch)
        {
            minPitch = _pressedMinPitch;
            maxPitch = _pressedMaxPitch;
            return _pressedSpanValid;
        }

        /// <summary>显示模式变化广播。</summary>
        public event Action<DisplayMode> OnDisplayModeChanged;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            _prefs.Load();
            BuildScene();
            PreflightSubsystems();
        }

        private void Start()
        {
            // 主菜单点过「导入 MIDI」→ 进入课程场景后自动打开文件选择器（方案 6.4）
            if (PianoTeacher3D.UI.MainMenuUI.ConsumePendingImport())
            {
                ImportMidi();
                ApplyPreferences();
                return;
            }

            LoadInitialSong();
            ApplyPreferences();   // 曲目就绪后再套用偏好，避免被「切曲重建」逻辑覆盖
        }

        private void Update()
        {
            // 后台曲目作业：主线程只轮询状态并在完成时提交（Unity 对象一律留在主线程创建）
            if (_loadJob != null)
            {
                if (_loadJob.Stage != _lastLoadStage) NotifyLoadStage(_loadJob.Stage);
                if (_loadJob.IsDone) CompleteLoadJob();
            }

            // 照片外观任务：读图与生成都在主线程推进，取消与超时按注入时钟判定
            _customization?.Tick(Time.unscaledTime);

            if (_timeline == null || _scheduler == null) return;

            float current = _timeline.CurrentTime;
            if (current < _lastTime - 0.001f)
            {
                // Loop 跳转 / Seek 回退：清空后按新时间点重建按键与手部状态（方案 6.3）
                ResetPlaybackState();
                _scheduler.SeekTo(current);
                _camera?.InvalidateFraming();
            }
            else
            {
                _scheduler.Tick(current);
            }
            _lastTime = current;

            UpdateHighlightUpcoming(current);
            UpdateHandLeadTargets(current);
            _metronome?.Tick(current);
            _handIK?.Tick(current);

            if (_displayMode == DisplayMode.Teaching) ApplyTeachingKeySpan();

            OnProgressChanged?.Invoke(current, _timeline.TotalDuration);

            if (_prefsDirty && Time.unscaledTime >= _prefsNextSaveAt) SavePreferences();
        }

        private void OnApplicationQuit()
        {
            if (_prefsDirty) SavePreferences();
        }

        private void OnDisable()
        {
            if (_prefsDirty) SavePreferences();
        }

        /// <summary>
        /// 每帧把预提示窗口 [当前时间, 当前时间 + 提前量) 内的音符推送给高亮器，
        /// 并顺带把该窗口的音高范围交给相机（让特写取景包含「即将发生的琴键」，方案 4.2.1）。
        /// 分手练习时只提示当前练习手，保持与琴键动画 / 手部动作一致。
        /// </summary>
        private void UpdateHighlightUpcoming(float current)
        {
            if (_highlighter == null) return;

            // 停止状态：不显示任何琴键提示（Stop 语义为完全复位，避免"已停止但仍亮着"的误导）
            if (_timeline == null || _timeline.State == PlaybackState.Stopped)
            {
                if (_highlighter.HighlightedKeyCount > 0 || _highlighter.UpcomingNoteCount > 0)
                    _highlighter.ClearAll();
                return;
            }

            var mode = _highlighter.Mode;
            if (mode != HighlightMode.Upcoming && mode != HighlightMode.Both) return;

            _scheduler.CollectNotesInRange(current, current + _highlightLookAhead, _upcomingBuffer);

            if (_practiceHand != PracticeHand.Both)
            {
                bool wantLeft = _practiceHand == PracticeHand.LeftOnly;
                for (int i = _upcomingBuffer.Count - 1; i >= 0; i--)
                {
                    if (PianoNoteFilter.IsLeftHand(_upcomingBuffer[i].Pitch) != wantLeft)
                        _upcomingBuffer.RemoveAt(i);
                }
            }

            _highlighter.RefreshUpcoming(_upcomingBuffer);
            ForwardUpcomingKeySpan();
        }

        /// <summary>把预提示窗口的音高范围交给相机（内部有保持时间，不喂入即自动失效）。</summary>
        private void ForwardUpcomingKeySpan()
        {
            if (_camera == null || _upcomingBuffer.Count == 0) return;

            int min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < _upcomingBuffer.Count; i++)
            {
                int p = _upcomingBuffer[i].Pitch;
                if (p < min) min = p;
                if (p > max) max = p;
            }
            if (min <= max) _camera.SetFramingKeySpan(min, max);
        }

        /// <summary>
        /// 手部预置（方案 5.3 第二阶段「先形成手型再下压」/ 方案 4.2.3「对音符密集区使用轻量预测」）：
        /// 把每只手在 <see cref="HandLeadSeconds"/> 内即将到来、且此刻尚未按下的音高推给手部 IK，
        /// 让手腕在音符开始前先移向对应琴键。取法与 <c>HandIKController.FindHandTarget</c> 保持一致
        /// （左手取最低音、右手取最高音），并按当前练习手过滤。
        /// </summary>
        private void UpdateHandLeadTargets(float current)
        {
            if (_handIK == null || _scheduler == null) return;

            if (_timeline == null || _timeline.State == PlaybackState.Stopped || _currentSong == null)
            {
                _handIK.SetUpcomingPitches(-1, -1);   // 停止态不预置，避免手继续移向旧音符
                return;
            }

            _scheduler.CollectNotesInRange(current, current + Mathf.Max(0f, HandLeadSeconds), _handLeadBuffer);

            int left = -1, right = -1;
            for (int i = 0; i < _handLeadBuffer.Count; i++)
            {
                var n = _handLeadBuffer[i];
                if (n.StartTime <= current + 0.0005f) continue;   // 已经开始的不算「即将到来」

                bool isLeft = PianoNoteFilter.IsLeftHand(n.Pitch);
                if (_practiceHand != PracticeHand.Both)
                {
                    bool wantLeft = _practiceHand == PracticeHand.LeftOnly;
                    if (isLeft != wantLeft) continue;
                }

                if (isLeft)
                {
                    if (left < 0 || n.Pitch < left) left = n.Pitch;
                }
                else if (n.Pitch > right)
                {
                    right = n.Pitch;
                }
            }

            _handIK.SetUpcomingPitches(left, right);
        }

        // ==================== 场景装配（方案 9.3：保留程序化装配） ====================

        private void BuildScene()
        {
            // 1. 统一时间轴（唯一时间源）
            _timeline = gameObject.AddComponent<LessonTimeline>();
            _timeline.OnStateChanged += HandleTimelineStateChanged;

            // 2. 错误上报（方案 8.1）：底层只产出数据，这里统一走它
            _reporter = gameObject.AddComponent<ErrorReporter>();
            _reporter.StateProvider = () => _state;

            // 3. MIDI 事件调度器
            _scheduler = gameObject.AddComponent<MidiEventScheduler>();

            // 4. 钢琴（真实比例 88 键 + 立式琴体，居中于 X）
            var piano = new GameObject("Piano");
            piano.transform.SetParent(transform, false);
            _keyMap = piano.AddComponent<PianoKeyMap>();
            _keyMap.BuildKeys();
            piano.transform.localPosition = new Vector3(-_keyMap.TotalWidth * 0.5f, 0f, 0f);

            // 5. 教学场景环境（地面 / 背景 / 三点光照 / 接触阴影近似；质量档位在此落地）
            //    优先用户显式覆盖；否则走设备建议 Auto，首次启动不盲目应用 High（优化方案 P1-07）
            CurrentQualityTier = _prefs.ResolveQualityTier();
            var quality = QualityProfile.For(CurrentQualityTier);
            _environment = gameObject.AddComponent<PianoSceneEnvironment>();
            _environment.Build(_keyMap, quality);

            // 5.1 场景文件里可能残留的灯光：统一交给 PianoSceneEnvironment 管理，避免叠加成两套阴影
            DisableLegacySceneLights();

            // 6. 琴键动画 + 音频 + 高亮提示 + 节拍器
            _keyAnimator = gameObject.AddComponent<PianoKeyAnimator>();
            _keyAnimator.Initialize(_keyMap);

            _audio = gameObject.AddComponent<PianoAudioSynth>();
            _audio.Initialize();

            _highlighter = gameObject.AddComponent<PianoKeyHighlighter>();
            _highlighter.Initialize(_keyMap);

            _metronome = gameObject.AddComponent<Metronome>();
            _metronome.Initialize();
            _metronome.OnBeat += beat => OnMetronomeBeat?.Invoke(beat);

            // 7. 人物（3 预设角色，坐于键盘前方 +Z 侧，面向 -Z 朝钢琴）
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.transform.SetParent(transform, false);
            avatarRoot.transform.localPosition = new Vector3(0f, 0f, 0.42f);
            avatarRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _avatar = avatarRoot.AddComponent<AvatarManager>();
            _avatar.BuildAvatars();
            _avatar.OnCharacterViewChanged += OnCharacterViewChanged;
            BuildCustomizationPipeline();

            // 8. 手部 IK + 手指姿态：两者共用同一份「手指 ↔ 音高」分配（方案 5.3 第二阶段），
            //    保证「弯下去的手指 == 指尖伸向该键的手指 == 真正按下该键的手指」三者严格一致。
            _fingerAssignment = new FingerAssignmentService();
            _fingerAssignment.Provider = _fingering;

            _handIK = gameObject.AddComponent<HandIKController>();
            _handIK.Initialize(_avatar, _keyMap);
            _handIK.Assignment = _fingerAssignment;

            _fingerPose = gameObject.AddComponent<FingerPoseController>();
            _fingerPose.Assignment = _fingerAssignment;
            _fingerPose.TargetSource = _handIK;          // 手指朝"它该按的那个键"对准
            _fingerPose.Initialize(_avatar.LeftHand, _avatar.RightHand);
            _handIK.PoseIssueSource = _fingerPose;       // 关节限位次数进入动作指标（附录 A.6.3）

            // 9. 摄像机控制 + 环境氛围
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PianoTeacher3D.UI.UITheme.SceneBg;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.47f);

            _camera = gameObject.AddComponent<CameraModeController>();
            _camera.Initialize(cam, _avatar, _keyMap);
            _camera.SetPracticeHand(_practiceHand);

            // 10. UI（控制栏 + 角色选择；状态栏由控制栏内部装配）
            var controlsUI = gameObject.AddComponent<PianoTeacher3D.UI.LessonControlsUI>();
            controlsUI.Initialize(this);
            var avatarUI = gameObject.AddComponent<PianoTeacher3D.UI.AvatarSelectionUI>();
            avatarUI.Initialize(this);

            // 11. 事件连线（琴键 / 音频 / 高亮 / 手部订阅音符事件）
            _scheduler.OnNoteOn += _keyAnimator.OnNoteOn;
            _scheduler.OnNoteOn += _audio.OnNoteOn;
            _scheduler.OnNoteOn += _handIK.OnNoteOn;
            _scheduler.OnNoteOn += _fingerPose.OnNoteOn;
            _scheduler.OnNoteOn += _highlighter.OnNoteOn;
            _scheduler.OnNoteOff += _keyAnimator.OnNoteOff;
            _scheduler.OnNoteOff += _audio.OnNoteOff;      // 方案 7.3：长音必须在 Note Off 时释放
            _scheduler.OnNoteOff += _handIK.OnNoteOff;
            _scheduler.OnNoteOff += _fingerPose.OnNoteOff;
            _scheduler.OnNoteOff += _highlighter.OnNoteOff;
            _scheduler.OnNoteOn += TrackPressedNote;      // 跟踪当前按下音高（教学模式取景 / 一致性验证）
            _scheduler.OnNoteOff += UntrackPressedNote;
        }

        /// <summary>关闭场景文件里遗留的灯光（环境由 <see cref="PianoSceneEnvironment"/> 统一管理）。</summary>
        private void DisableLegacySceneLights()
        {
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                if (l.transform.IsChildOf(transform)) continue; // 环境自建灯光保留
                l.enabled = false;
            }
        }

        /// <summary>启动自检（方案 8.2）：音频 / 人物骨架不可用时给出可继续的降级提示。</summary>
        private void PreflightSubsystems()
        {
            if (_audio != null && !_audio.Initialized)
            {
                _audioDegraded = true;
                string detail = string.IsNullOrEmpty(_audio.LastInitError) ? "音频设备不可用" : _audio.LastInitError;
                Report(ErrorReporter.AudioInitFailed(detail));
            }

            if (_avatar != null && !string.IsNullOrEmpty(_avatar.LastRigValidationMessage))
                Report(ErrorReporter.AvatarInvalid(_avatar.LastRigValidationMessage));
        }

        // ==================== 曲目加载（事务，方案 6.2 / 6.4） ====================

        private void LoadInitialSong()
        {
            // 主菜单传入的选中曲目优先
            string selected = PianoTeacher3D.UI.MainMenuUI.SelectedSongPath;
            if (!string.IsNullOrEmpty(selected) && SafeFileExists(selected))
            {
                LoadSong(selected);
                return;
            }

            string dir = _library.BuiltInSongDirectory;
            if (!SafeDirectoryExists(dir))
            {
                Report(AppError.Recoverable(ErrorCode.SongDirectoryMissing,
                    "找不到示例曲目录（StreamingAssets/Songs）。请先点击「导入 MIDI」导入自己的曲目。",
                    RecoveryAction.ChooseFile, null, "dir=Songs"));
                SetState(LessonState.NoSong);
                return;
            }

            var files = _library.ListBuiltInSongs();
            if (files.Count == 0)
            {
                Report(AppError.Recoverable(ErrorCode.SongListEmpty,
                    "示例曲目录里没有可用的 .mid 曲目。请点击「导入 MIDI」导入自己的曲目。",
                    RecoveryAction.ChooseFile));
                SetState(LessonState.NoSong);
                return;
            }

            LoadSong(files[0]);
        }

        /// <summary>
        /// 加载指定 MIDI 曲目。
        /// 事务顺序（方案 6.2）：校验文件与路径 → 在临时对象中解析并检查 → 一次性提交 →
        /// 清理播放状态 → 更新 UI；任一步失败**回滚到明确状态**，绝不留半加载状态。
        /// </summary>
        public void LoadSong(string path)
        {
            if (_loading)
            {
                Report(ErrorReporter.Busy("加载曲目"));
                return;
            }

            // ---- 第 1、2 步：校验 + 临时解析（不触碰任何现有状态） ----
            var check = _library.Validate(path ?? "");
            if (!check.Ok || check.Parse == null)
            {
                FailLoad(check.ToError());
                return;
            }

            var meta = BuildMetadata(path, check.Parse);
            CommitSong(check.Parse, path, meta);
        }

        /// <summary>
        /// 在后台读取 / 校验 / 解析 / 分析指定曲目，完成后在主线程一次性提交（优化方案 §7-8）。
        /// <para>
        /// <b>线程边界</b>：后台只跑纯 C#（<see cref="MidiFileValidator"/> / <see cref="MidiParser"/> /
        /// <see cref="SongAnalyzer"/>），场景对象、UI、曲库索引写入全部由 <see cref="CompleteLoadJob"/> 在主线程完成。
        /// </para>
        /// <para>
        /// <b>失败/取消</b>：都不触碰当前曲目与播放状态；只有成功才替换，因此不会出现「标题换成新曲、音符还是旧曲」。
        /// </para>
        /// </summary>
        public void LoadSongAsync(string path)
        {
            BeginLoadJob(path, asImport: false);
        }

        /// <summary>启动后台作业（<paramref name="asImport"/> = 完成后还要复制进用户曲库）。</summary>
        private void BeginLoadJob(string path, bool asImport)
        {
            if (_loading || _loadJob != null)
            {
                Report(ErrorReporter.Busy(asImport ? "导入曲目" : "加载曲目"));
                return;
            }

            var job = new SongLoadJob(path ?? "");
            _loadJob = job;
            _loadJobIsImport = asImport;
            _lastLoadStage = SongLoadStage.Queued;
            SetLoading(true);
            NotifyLoadStage(job.Stage);
            job.Start();
        }

        /// <summary>主线程收尾：读取后台结果 → 提交 / 上报失败 / 回滚取消。</summary>
        private void CompleteLoadJob()
        {
            var job = _loadJob;
            if (job == null) return;
            _loadJob = null;

            bool cancelled = _cancelRequested || job.Stage == SongLoadStage.Cancelled;
            _cancelRequested = false;

            try
            {
                if (cancelled)
                {
                    NotifyLoadStage(SongLoadStage.Cancelled);
                    RestoreIdleState();          // 旧曲与旧播放状态原样保留
                    _reporter?.Clear();
                    return;
                }

                if (job.Stage != SongLoadStage.Ready || job.Check.Parse == null)
                {
                    NotifyLoadStage(SongLoadStage.Failed);
                    FailLoad(BuildJobError(job));
                    return;
                }

                // ---- 导入模式：复制进用户曲库（已校验，不再重复解析）----
                if (_loadJobIsImport)
                {
                    var import = _library.Import(job.SourcePath, job.Check);
                    if (!import.Ok || import.Meta == null || import.Check.Parse == null)
                    {
                        NotifyLoadStage(SongLoadStage.Failed);
                        FailLoad(import.Check.ToError());
                        return;
                    }

                    _prefs.LastSongId = import.Meta.Id;
                    NotifyLoadStage(SongLoadStage.Ready);
                    CommitSong(import.Check.Parse, import.Meta.SourcePath, import.Meta, job.Analysis);
                    return;
                }

                // ---- 直接打开：只做元数据与提交 ----
                var meta = BuildMetadata(job.SourcePath, job.Check.Parse);
                NotifyLoadStage(SongLoadStage.Ready);
                CommitSong(job.Check.Parse, job.SourcePath, meta, job.Analysis);
            }
            finally
            {
                job.Dispose();                    // 释放取消令牌与作业引用（不留下临时资源）
                SetLoading(false);
            }
        }

        /// <summary>把后台作业的失败转成结构化错误（异常路径不能伪装成「文件格式错误」）。</summary>
        private static AppError BuildJobError(SongLoadJob job)
        {
            if (!string.IsNullOrEmpty(job.Check.UserMessage)) return job.Check.ToError();

            return AppError.Recoverable(ErrorCode.Unknown,
                "读取曲目时出现意外错误，当前曲目未受影响。请重试一次；若仍然失败，请换一个 MIDI 文件。",
                RecoveryAction.Retry, null, $"loadJob={job.FailureDetail}");
        }

        private void NotifyLoadStage(SongLoadStage stage)
        {
            _lastLoadStage = stage;
            OnLoadStageChanged?.Invoke(stage);
        }

        /// <summary>通过最近曲目记录打开曲目（文件被移动/删除时给出可操作提示，不崩溃）。</summary>
        public bool TryOpenRecentSong(string idOrPath)
        {
            if (_loading)
            {
                Report(ErrorReporter.Busy("打开曲目"));
                return false;
            }

            if (_library.TryResolveRecent(idOrPath, out var meta, out var resolved, out var problem))
            {
                LoadSong(resolved);
                return true;
            }

            Report(AppError.Recoverable(ErrorCode.FileNotFound,
                problem ?? "找不到这首曲目，请重新选择文件。",
                RecoveryAction.ChooseFile, null, $"song={PathSanitizer.Sanitize(idOrPath)}"));
            return false;
        }

        /// <summary>打开应用内 MIDI 文件选择器并导入选中文件（方案 6.4）。</summary>
        public void ImportMidi()
        {
            if (_loading)
            {
                Report(ErrorReporter.Busy("导入曲目"));
                return;
            }

            string start = SafeDirectoryExists(_library.UserSongDirectory)
                ? _library.UserSongDirectory
                : _library.BuiltInSongDirectory;

            PianoTeacher3D.UI.FileBrowserUI.Show(start, ImportMidiFromPath, () => _reporter?.Clear());
        }

        /// <summary>导入指定路径的 MIDI（后台校验/解析/分析 → 主线程复制进曲库并加载）。</summary>
        public void ImportMidiFromPath(string path)
        {
            if (_loading || _loadJob != null)
            {
                Report(ErrorReporter.Busy("导入曲目"));
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                Report(AppError.Recoverable(ErrorCode.FileNotFound,
                    "没有选择任何文件。请重新点击「导入 MIDI」选择一个 .mid / .midi 文件。",
                    RecoveryAction.ChooseFile));
                return;
            }

            BeginLoadJob(path, asImport: true);
        }

        /// <summary>
        /// 取消当前加载（方案 6.4「显示加载进度、可取消状态」/ 优化方案 §7-8）。
        /// <para>
        /// 后台作业取消后，结果被丢弃，<b>旧曲与旧播放状态保持不变</b>；作业已结束时立即收尾，
        /// 因此 UI 不会停在 Loading。同步加载（<see cref="LoadSong"/>）路径沿用原有语义。
        /// </para>
        /// </summary>
        public void CancelLoading()
        {
            if (_loadJob != null)
            {
                _cancelRequested = true;
                _loadJob.Cancel();
                if (_loadJob.IsDone) CompleteLoadJob();   // 已完成的作业立即按取消收尾
                return;
            }

            if (!_loading && _state != LessonState.Loading) return;

            _cancelRequested = true;                // 提交前的临界区会读到它
            if (_state == LessonState.Loading)
            {
                _cancelRequested = false;
                RestoreIdleState();
                _reporter?.Clear();
            }
        }

        /// <summary>第 3–5 步：一次性提交新曲目并清理旧曲状态。</summary>
        private void CommitSong(MidiParseResult result, string path, SongMetadata meta,
            SongAnalysis precomputedAnalysis = null)
        {
            SetLoading(true);
            try
            {
                // ---- 关闭旧曲并清理全部残留状态 ----
                _timeline.Stop();
                ResetPlaybackState();

                if (_cancelRequested)
                {
                    _cancelRequested = false;
                    RestoreIdleState();
                    return;
                }

                // ---- 一次性提交 ----
                _currentSong = result;
                _songSourcePath = path ?? "";
                _songId = meta != null && !string.IsNullOrEmpty(meta.Id)
                    ? meta.Id
                    : SongLibraryService.StableId(_songSourcePath);
                CurrentSongName = meta != null && !string.IsNullOrEmpty(meta.Title)
                    ? meta.Title
                    : Path.GetFileNameWithoutExtension(_songSourcePath);

                _timeline.SetDuration(result.TotalDurationSeconds);
                _timeline.ClearLoop();          // 切曲重置 A-B 标记（方案 6.3 推荐默认策略）
                _lastTime = 0f;

                // 按当前练习模式装载音符（分手练习由过滤层完成，方案 6.3）
                _scheduler.Load(PianoNoteFilter.FilterByHand(result.Notes, _practiceHand));

                _metronome?.Load(result.TempoChanges, result.TotalDurationSeconds);
                // 后台已分析过就复用，避免在主线程再算一遍（优化方案 §7-8「重活不留在主线程」）
                _analysis = precomputedAnalysis ?? SongAnalyzer.Analyze(
                    result.Notes, result.TempoChanges, result.TotalDurationSeconds);
                _handIK?.ResetMetrics();        // 换曲后重新开始动作质量测量

                ResetPlayback();
                _camera?.ClearFramingKeySpan();
                _camera?.InvalidateFraming();

                // ---- 更新 UI / 最近曲目 / 偏好 ----
                _library.RecordPlayed(meta);
                _prefs.LastSongId = _songId;
                PianoTeacher3D.UI.MainMenuUI.NotifySongOpened(_songSourcePath);

                SetState(LessonState.Ready);
                OnSongLoaded?.Invoke();
                OnPracticeHandChanged?.Invoke(_practiceHand);
                OnAnalysisReady?.Invoke(_analysis);

                ReportParseWarnings(result);
                if (!_suppressPrefsWrite) { _prefsDirty = true; SavePreferences(); }
            }
            finally
            {
                SetLoading(false);
            }
        }

        /// <summary>加载失败：上报错误并回滚到明确状态（有旧曲 → 保持旧曲；无旧曲 → Error）。</summary>
        private void FailLoad(AppError error)
        {
            Report(error);
            if (HasSong) RestoreIdleState();   // 回滚：保留上一份可用曲目与播放状态，不产生混合状态
            else SetState(LessonState.Error);
        }

        /// <summary>按时间轴 / 曲目实际情况落回一个明确、非过渡态的状态。</summary>
        private void RestoreIdleState()
        {
            if (_timeline != null && _timeline.State == PlaybackState.Playing)
            {
                SetState(LessonState.Playing);
                return;
            }
            if (_timeline != null && _timeline.State == PlaybackState.Paused)
            {
                SetState(LessonState.Paused);
                return;
            }
            SetState(HasSong ? LessonState.Ready : LessonState.NoSong);
        }

        /// <summary>曲目解析产生的兼容性提示（方案 6.4 / 11.1：Format 2、异常 tempo、缺 Note Off）。</summary>
        private void ReportParseWarnings(MidiParseResult result)
        {
            if (result == null) return;

            if (result.Warnings != null && result.Warnings.Count > 0)
            {
                for (int i = 0; i < result.Warnings.Count; i++)
                {
                    _reporter?.Report(AppError.Warning(ErrorCode.UnsupportedFormat,
                        $"这首曲目有兼容性提示：{result.Warnings[i]}", $"format={result.MidiFormat}"));
                }
            }

            if (result.DanglingNoteCount > 0)
            {
                _reporter?.Report(AppError.Warning(ErrorCode.MidiDanglingNotes,
                    $"曲目中有 {result.DanglingNoteCount} 个音符缺少结束标记，已按曲目末尾补全，长音可能略长。" +
                    "换一首规范导出的 MIDI 可以获得更准确的时长。",
                    $"dangling={result.DanglingNoteCount}"));
            }

            // 分手练习下该手无音符 → 功能降级提示（方案 8.2）
            WarnIfPracticeHandEmpty();
        }

        private void WarnIfPracticeHandEmpty()
        {
            if (_practiceHand == PracticeHand.Both || _currentSong == null) return;
            bool left = _practiceHand == PracticeHand.LeftOnly;
            var filtered = PianoNoteFilter.FilterByHand(_currentSong.Notes, _practiceHand);
            if (filtered.Count == 0) Report(ErrorReporter.HandHasNoNotes(left));
        }

        private static SongMetadata BuildMetadata(string path, MidiParseResult parse)
        {
            long size = 0;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists) size = fi.Length;
            }
            catch (Exception)
            {
                size = 0; // 元数据只用于展示，取不到就算了
            }

            return new SongMetadata
            {
                Id = SongLibraryService.StableId(path),
                Title = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                DurationSeconds = parse.TotalDurationSeconds,
                NoteCount = parse.Notes != null ? parse.Notes.Count : 0,
                TrackCount = parse.TrackCount,
                TempoChangeCount = parse.TempoChanges != null ? parse.TempoChanges.Count : 0,
                MidiFormat = parse.MidiFormat,
                FileSizeBytes = size,
                DanglingNoteCount = parse.DanglingNoteCount,
                ImportedAtIso = DateTime.Now.ToString("o")
            };
        }

        // ==================== 播放控制（转发到统一时间轴） ====================

        public void PlayPause()
        {
            if (_timeline == null) return;
            if (_timeline.State == PlaybackState.Playing) { Pause(); return; }
            Play();
        }

        public void Play()
        {
            if (!RequireSong("播放")) return;

            bool resuming = _timeline != null && _timeline.State == PlaybackState.Paused;
            _timeline?.Play();

            if (resuming)
            {
                // 从暂停恢复：按当前时间重建按键 / 手部状态，避免长音恢复后静默或状态残留（方案 7.3）
                ResetPlaybackState();
                _scheduler?.SeekTo(_timeline.CurrentTime);
                _camera?.InvalidateFraming();
            }
        }

        public void Pause()
        {
            _timeline?.Pause();
            _audio?.ReleaseAll();   // 方案 7.3：暂停时释放所有正在发声的音符
        }

        public void Stop()
        {
            _timeline?.Stop();
            ResetPlaybackState();
            _camera?.ClearFramingKeySpan();
            _camera?.InvalidateFraming();
        }

        public void Restart()
        {
            if (!RequireSong("重播")) return;
            _timeline?.Restart();
            _scheduler?.SeekTo(0f);
            ResetPlaybackState();
            _camera?.InvalidateFraming();
        }

        public void Seek(float seconds)
        {
            if (_timeline == null || _scheduler == null) return;
            _timeline.Seek(seconds);
            ResetPlaybackState();
            _scheduler.SeekTo(seconds);
            _camera?.InvalidateFraming();
        }

        public void SeekNormalized(float t01)
        {
            if (_timeline == null) return;
            Seek(Mathf.Clamp01(t01) * _timeline.TotalDuration);
        }

        public void SetSpeed(float speed)
        {
            if (_timeline == null) return;
            _timeline.PlaybackSpeed = Mathf.Max(0.1f, speed);
            if (!_suppressPrefsWrite) { _prefs.Speed = _timeline.PlaybackSpeed; MarkPrefsDirty(); }
        }

        public void CycleSpeed()
        {
            float[] speeds = { 0.5f, 1f, 1.5f };
            if (_timeline == null) return;
            int i = Array.IndexOf(speeds, _timeline.PlaybackSpeed);
            SetSpeed(speeds[(i + 1) % speeds.Length]);
        }

        // ==================== 视角 / 角色切换（只改表现层，方案 13 取舍 3） ====================

        public void CycleCameraMode()
        {
            if (_camera == null) return;
            _camera.CycleMode();
            AfterCameraModeChanged();
        }

        public void SetCameraMode(CameraMode mode)
        {
            if (_camera == null) return;
            _camera.SetMode(mode);
            AfterCameraModeChanged();
        }

        /// <summary>从自由观察返回进入前的教学预设（方案 4.4）。</summary>
        public void ReturnToCameraPreset() => _camera?.ReturnToPreset();

        public void ResetCamera() => _camera?.ResetView();

        private void AfterCameraModeChanged()
        {
            _camera?.SetPracticeHand(_practiceHand);
            _camera?.InvalidateFraming();
            OnCameraModeChanged?.Invoke(_camera.Mode);
            if (!_suppressPrefsWrite) { _prefs.LastCameraMode = (int)_camera.Mode; MarkPrefsDirty(); }
        }

        public void CycleAvatar()
        {
            if (_avatar == null) return;
            _avatar.SwitchNext();
            _fingerPose?.Initialize(_avatar.LeftHand, _avatar.RightHand);
            AfterAvatarChanged();
        }

        public void SwitchAvatar(int index)
        {
            if (_avatar == null) return;
            _avatar.SwitchAvatar(index);
            _fingerPose?.Initialize(_avatar.LeftHand, _avatar.RightHand);
            _fingerPose?.ReleaseAll();
            AfterAvatarChanged();
        }

        private void AfterAvatarChanged()
        {
            _camera?.InvalidateFraming();

            // 换角色后重新开始动作质量测量：不同骨架的腕部基准位置本就不同，
            // 切换瞬间的整段位移属于「换了一套身体」而非「手在移动」，
            // 计入 MaxStepDistance 会得到一个无意义的巨大值并掩盖真正的瞬移问题。
            _handIK?.ResetMetrics();

            OnAvatarChanged?.Invoke(_avatar.CurrentIndex);
            if (!_suppressPrefsWrite) { _prefs.AvatarIndex = _avatar.CurrentIndex; MarkPrefsDirty(); }
        }

        // ==================== 角色档案切换与照片外观（方案 7.3 / 9.2） ====================

        /// <summary>照片外观管线（UI 用它提交照片、预览与应用）。未装配时返回 null。</summary>
        public CharacterCustomizationService Customization => _customization;

        /// <summary>隐私同意控制器（照片面板在提交前必须拿到它）。未装配时返回 null。</summary>
        public PrivacyConsentController PrivacyConsent => _consent;

        /// <summary>当前角色契约实例（可能为 null：内置角色初始化前）。</summary>
        public ICharacterView CurrentCharacterView => _avatar != null ? _avatar.CurrentView : null;

        /// <summary>
        /// 按角色档案切换角色（方案 7.3 事务：加载 → 校验 → 激活 → 提交，失败保留原角色）。
        /// 内置角色走 <see cref="AvatarManager"/> 的既有路径，外部预制品走切换事务。
        /// </summary>
        public CharacterSwitchResult ActivateCharacter(CharacterProfile profile)
        {
            if (_avatar == null)
                return new CharacterSwitchResult(CharacterSwitchState.RolledBack,
                    "角色系统尚未初始化。", string.Empty, string.Empty);

            CharacterSwitchResult result = _avatar.TryActivate(profile);
            if (result.Succeeded)
            {
                // 外部角色的骨骼引用换了：必须重新绑定手指姿态控制器，否则手指不跟谱
                _fingerPose?.Initialize(_avatar.LeftHand, _avatar.RightHand);
                _fingerPose?.ReleaseAll();
                AfterAvatarChanged();
            }

            return result;
        }

        /// <summary>
        /// 角色视图变更（内置互切 / 外部角色提交 / 切回内置 / 销毁）都走这里：
        /// 外观管线必须重新绑定，否则应用过的照片外观会丢引用或泄漏纹理（方案 11.2）。
        /// </summary>
        private void OnCharacterViewChanged(ICharacterView view)
        {
            if (_customization != null) _customization.BindCharacter(view, out _);

            // 手指关节轴随角色切换：程序化角色用默认「局部 X 轴」，导入骨架用推导出的铰链轴。
            // 不切换的话，Biped 手指会被拧成麻花而不是弯曲（见 IFingerJointAxes 注释）。
            if (_fingerPose != null)
            {
                _fingerPose.JointAxes = view is ICharacterJointAxesProvider provider
                    ? provider.JointAxes
                    : DefaultFingerJointAxes.Instance;
            }
        }

        /// <summary>装配照片外观管线。只创建内存对象，不联网、不写用户数据。</summary>
        private void BuildCustomizationPipeline()
        {
            try
            {
                string root = Path.Combine(Application.persistentDataPath, "customization");
                var storage = new AppearanceStorage(root);
                _consent = new PrivacyConsentController(root);
                var provider = new MockImageGenerationProvider(() => Time.unscaledTime);
                _customization = new CharacterCustomizationService(storage, _consent, provider);
                _customization.BindCharacter(_avatar != null ? _avatar.CurrentView : null, out _);
            }
            catch (Exception ex)
            {
                // 外观管线失败不能拖垮演奏（方案 9.6）：只记录，不抛出
                _customization = null;
                Debug.LogWarning($"照片外观管线未启用：{ex.GetType().Name}");
            }
        }

        /// <summary>当前角色是否持有效 Humanoid 骨架（供 UI / 诊断显示）。</summary>
        public bool HasValidHumanoidRig => _avatar != null && _avatar.HasHumanoidRig;

        // ==================== 画质档位（方案 7.1 / 7.2） ====================

        /// <summary>切换画质档位（幂等，可重复调用；不影响时间轴与教学语义）。用户手动选择后会记为显式覆盖。</summary>
        public void SetQualityTier(QualityTier tier)
        {
            CurrentQualityTier = NormalizeTier((int)tier);
            var quality = QualityProfile.For(CurrentQualityTier);
            quality.Apply();
            _environment?.ApplyQuality(quality);
            _prefs.SetQualityTierOverride(CurrentQualityTier);
            MarkPrefsDirty();
        }

        /// <summary>
        /// 恢复画质 Auto：清掉用户覆盖，下次启动重新按设备建议（优化方案 P1-07「恢复自动的入口」）。
        /// </summary>
        public void UseAutomaticQuality()
        {
            _prefs.UseAutomaticQuality();
            CurrentQualityTier = _prefs.ResolveQualityTier();
            var quality = QualityProfile.For(CurrentQualityTier);
            quality.Apply();
            _environment?.ApplyQuality(quality);
            MarkPrefsDirty();
        }

        /// <summary>当前画质是否处于 Auto（未显式覆盖），供 UI 显示当前档位与恢复自动的入口。</summary>
        public bool IsQualityAutomatic => !_prefs.QualityTierUserOverride;

        /// <summary>画质档位的中文描述（诊断面板用）。</summary>
        public string QualityDescription => QualityProfile.For(CurrentQualityTier).Describe();

        private static QualityTier NormalizeTier(int raw) => raw == (int)QualityTier.High
            ? QualityTier.High
            : QualityTier.Standard;

        /// <summary>场景环境可见性（调试 / 视觉对比用）。</summary>
        public void SetEnvironmentVisible(bool visible) => _environment?.SetEnvironmentVisible(visible);

        // ==================== 显示模式（展示 / 教学） ====================

        /// <summary>
        /// 切换显示模式：<see cref="DisplayMode.Showcase"/> 保持完整手部动画；
        /// <see cref="DisplayMode.Teaching"/> 隐藏手部，只高亮被按下的琴键（左手冷蓝 / 右手暖橙）。
        /// <para>
        /// 切换会立即按当前时间轴重建按键 / 手部 / 高亮状态，因此不会出现"上一模式的残留"
        /// （例如教学模式里残留的手部姿态，或展示模式里残留的强制高亮）。
        /// </para>
        /// </summary>
        public void SetDisplayMode(DisplayMode mode)
        {
            if (_displayMode == mode) return;
            _displayMode = mode;
            ApplyDisplayMode();
            OnDisplayModeChanged?.Invoke(mode);
            if (!_suppressPrefsWrite) { _prefs.DisplayMode = (int)mode; MarkPrefsDirty(); }
        }

        /// <summary>在展示 / 教学之间切换。</summary>
        public void ToggleDisplayMode() => SetDisplayMode(
            _displayMode == DisplayMode.Teaching ? DisplayMode.Showcase : DisplayMode.Teaching);

        private void ApplyDisplayMode()
        {
            bool teaching = _displayMode == DisplayMode.Teaching;

            // ① 手部 / 手臂网格可见性（不动骨骼与 IK，因此切回时零残留）
            _avatar?.SetHandsAndArmsVisible(!teaching);

            // ② 教学模式强制「仅高亮被按下的琴键」；进入时记住用户偏好，退出时还原
            if (_highlighter != null)
            {
                if (teaching)
                {
                    if (_highlighter.Mode != HighlightMode.Active)
                    {
                        if (!_hasHighlightToRestore)
                        {
                            _highlightToRestore = _highlighter.Mode;
                            _hasHighlightToRestore = true;
                        }
                        _highlighter.Mode = HighlightMode.Active;
                        OnHighlightModeChanged?.Invoke(HighlightMode.Active);
                    }
                }
                else if (_hasHighlightToRestore)
                {
                    _hasHighlightToRestore = false;
                    _highlighter.Mode = _highlightToRestore;
                    OnHighlightModeChanged?.Invoke(_highlightToRestore);
                }
            }

            // ③ 立即生效且无残留：按当前时间轴重建按键 / 手部 / 高亮状态
            if (_scheduler != null && _timeline != null)
            {
                ResetPlaybackState();
                _scheduler.SeekTo(_timeline.CurrentTime);
            }
            _camera?.InvalidateFraming();

            // ④ 教学模式下手部不可见，取景范围改由「当前按下的琴键」提供
            if (teaching) ApplyTeachingKeySpan();
            else _camera?.ClearFramingKeySpan();
        }

        /// <summary>教学模式：把当前按下琴键的音高范围交给相机（代替不可见的手部范围）。</summary>
        private void ApplyTeachingKeySpan()
        {
            if (_camera == null) return;
            if (!_pressedSpanValid)
            {
                _camera.ClearFramingKeySpan();
                return;
            }
            _camera.SetFramingKeySpan(_pressedMinPitch, _pressedMaxPitch);
        }

        private void TrackPressedNote(MidiNoteEvent e)
        {
            if (!PianoKeyMap.IsInRange(e.Pitch)) return;
            if (_pressedPitches.Add(e.Pitch)) UpdatePressedPitchSpan();
        }

        private void UntrackPressedNote(MidiNoteEvent e)
        {
            if (_pressedPitches.Remove(e.Pitch)) UpdatePressedPitchSpan();
        }

        private void UpdatePressedPitchSpan()
        {
            if (_pressedPitches.Count == 0)
            {
                _pressedSpanValid = false;
                return;
            }

            int min = int.MaxValue, max = int.MinValue;
            foreach (int p in _pressedPitches)
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }
            _pressedMinPitch = min;
            _pressedMaxPitch = max;
            _pressedSpanValid = true;
        }

        // ==================== 练习模式 / 循环（方案 2.2 / 6.3） ====================

        public void CyclePracticeHand()
        {
            SetPracticeHand((PracticeHand)(((int)_practiceHand + 1) % 3));
        }

        /// <summary>切换练习模式（双手 / 仅左手 / 仅右手），保持播放进度连续。</summary>
        public void SetPracticeHand(PracticeHand hand)
        {
            if (_practiceHand == hand) return;
            _practiceHand = hand;
            ReloadFilteredNotes();
            _camera?.SetPracticeHand(hand);
            OnPracticeHandChanged?.Invoke(hand);
            if (!_suppressPrefsWrite) { _prefs.PracticeHand = (int)hand; MarkPrefsDirty(); }
        }

        /// <summary>整曲循环开关（A-B 片段循环用 <see cref="SetLoop"/> 精确设置）。</summary>
        public void ToggleLoop()
        {
            if (_timeline == null) return;
            if (!RequireSong("循环")) return;

            if (_timeline.IsLooping) _timeline.ClearLoop();
            else _timeline.SetLoop(0f, _timeline.TotalDuration);
        }

        /// <summary>设置 A-B 循环；区间非法时自动关闭循环并给出可恢复提示（方案 8.3）。</summary>
        public void SetLoop(float start, float end)
        {
            if (_timeline == null) return;
            if (!RequireSong("设置循环")) return;

            _timeline.SetLoop(start, end);
            if (!_timeline.IsLooping) Report(ErrorReporter.LoopRangeInvalid());
        }

        /// <summary>关闭 A-B 循环并复位区间。</summary>
        public void ClearLoop() => _timeline?.ClearLoop();

        private void ReloadFilteredNotes()
        {
            if (_currentSong == null || _timeline == null || _scheduler == null) return;

            float current = _timeline.CurrentTime;
            var filtered = PianoNoteFilter.FilterByHand(_currentSong.Notes, _practiceHand);
            _scheduler.Load(filtered);
            ResetPlaybackState();
            _scheduler.SeekTo(current);
            _camera?.InvalidateFraming();

            if (filtered.Count == 0 && _practiceHand != PracticeHand.Both)
                Report(ErrorReporter.HandHasNoNotes(_practiceHand == PracticeHand.LeftOnly));
        }

        /// <summary>统一守卫：未加载曲目时给出用户可见提示并返回 false（避免"点了没反应"）。</summary>
        private bool RequireSong(string action)
        {
            if (HasSong) return true;
            Report(ErrorReporter.NoSong(action));
            return false;
        }

        // ==================== 错误呈现（方案 8.1 / 8.3） ====================

        /// <summary>
        /// 统一错误出口：写入日志（脱敏）+ 更新 <see cref="LastError"/> + 广播
        /// <see cref="OnAppError"/>（结构化）与 <see cref="OnError"/>（兼容通道，文案为面向用户的中文提示）。
        /// </summary>
        private void Report(AppError error)
        {
            if (!error.IsValid) return;

            if (_reporter != null) _reporter.Report(error);
            else Debug.LogError($"[PianoTeacher3D] {error.ToLogString()}");

            OnAppError?.Invoke(error);
            if (!string.IsNullOrEmpty(error.UserMessage)) OnError?.Invoke(error.UserMessage);
        }

        /// <summary>执行最近一次错误的建议恢复动作（UI 的恢复按钮调用，方案 8.1）。</summary>
        public void HandleRecovery()
        {
            RecoveryAction action = _reporter != null ? _reporter.Last.Recovery : RecoveryAction.None;
            _reporter?.Clear();

            switch (action)
            {
                case RecoveryAction.ChooseFile:
                    ImportMidi();
                    break;
                case RecoveryAction.Retry:
                case RecoveryAction.ReloadSong:
                    if (!string.IsNullOrEmpty(_songSourcePath)) LoadSong(_songSourcePath);
                    else ImportMidi();
                    break;
                case RecoveryAction.ResetLoop:
                    _timeline?.ClearLoop();
                    break;
                case RecoveryAction.UseDefaultAvatar:
                    SwitchAvatar(0);
                    break;
                case RecoveryAction.ContinueMuted:
                    if (_audio != null) _audio.Muted = true;
                    break;
                default:
                    break;
            }
        }

        /// <summary>清空最近错误（不影响已写入日志）。</summary>
        public void ClearLastError() => _reporter?.Clear();

        // ==================== 用户偏好（方案 9.2） ====================

        /// <summary>
        /// 套用已保存的用户偏好（最近曲目 / 最后视角 / 界面设置），与产品默认配置分开。
        /// <para>
        /// 自动化测试（<c>-batchmode</c>）下<b>跳过</b>：测试必须从确定性初始状态出发，
        /// 否则开发机上的个人偏好会污染断言（例如上次停留在"左手练习"）。
        /// </para>
        /// </summary>
        private void ApplyPreferences()
        {
            if (Application.isBatchMode) return;

            _suppressPrefsWrite = true;
            try
            {
                SetSpeed(_prefs.Speed);
                SetBeatsPerBar(_prefs.BeatsPerBar);
                HighlightLookAhead = _prefs.HighlightLookAhead;
                SetMetronomeEnabled(_prefs.MetronomeEnabled);

                SetCameraMode((CameraMode)Mathf.Clamp(_prefs.LastCameraMode, 0, 2));
                SwitchAvatar(Mathf.Clamp(_prefs.AvatarIndex, 0, AvatarManager.AvatarCount - 1));
                SetPracticeHand(NormalizeHand(_prefs.PracticeHand));
                SetHighlightMode(NormalizeHighlight(_prefs.HighlightMode));
                SetDisplayMode(_prefs.DisplayMode == (int)DisplayMode.Teaching ? DisplayMode.Teaching : DisplayMode.Showcase);
            }
            finally
            {
                _suppressPrefsWrite = false;
            }
        }

        private void MarkPrefsDirty()
        {
            _prefsDirty = true;
            if (_prefsNextSaveAt <= 0f) _prefsNextSaveAt = Time.unscaledTime + 2f;
        }

        private void SavePreferences()
        {
            _prefsDirty = false;
            _prefsNextSaveAt = 0f;
            try
            {
                _prefs.Save();
            }
            catch (Exception ex)
            {
                // 偏好写失败不应影响教学流程（只提示，不炸）
                Debug.LogWarning($"[PianoTeacher3D] 用户偏好保存失败：{ex.GetType().Name}");
            }
        }

        private static PracticeHand NormalizeHand(int raw) =>
            raw >= (int)PracticeHand.LeftOnly && raw <= (int)PracticeHand.RightOnly
                ? (PracticeHand)raw
                : PracticeHand.Both;

        private static HighlightMode NormalizeHighlight(int raw) =>
            raw >= (int)HighlightMode.Off && raw <= (int)HighlightMode.Both
                ? (HighlightMode)raw
                : HighlightMode.Off;

        // ==================== 内部状态机 ====================

        private void HandleTimelineStateChanged(PlaybackState state)
        {
            OnPlaybackStateChanged?.Invoke(state);
            if (_loading) return; // 加载事务期间状态由 Loading 掌控

            switch (state)
            {
                case PlaybackState.Playing:
                    SetState(LessonState.Playing);
                    break;
                case PlaybackState.Paused:
                    SetState(LessonState.Paused);
                    break;
                default:
                    SetState(HasSong ? LessonState.Ready : LessonState.NoSong);
                    break;
            }
        }

        private void SetState(LessonState next)
        {
            if (_state == next) return;
            _state = next;
            OnLessonStateChanged?.Invoke(next);
        }

        private void SetLoading(bool loading)
        {
            _loading = loading;
            if (loading)
            {
                SetState(LessonState.Loading);
                return;
            }

            // 事务结束：按实际播放/曲目情况落回明确状态（不覆盖已设置的终态）
            if (_state != LessonState.Loading) return;
            SetState(_timeline != null && _timeline.State == PlaybackState.Playing
                ? LessonState.Playing
                : HasSong ? LessonState.Ready : LessonState.NoSong);
        }

        private void ResetPlayback()
        {
            _timeline?.Stop();
            ResetPlaybackState();
        }

        /// <summary>释放全部瞬时状态：琴键按下、手部、指法、高亮、节拍游标、音频声部。</summary>
        private void ResetPlaybackState()
        {
            _keyAnimator?.ReleaseAll();
            _handIK?.ReleaseAll();
            _fingerPose?.ReleaseAll();
            _highlighter?.ClearAll();
            _audio?.ReleaseAll();       // 方案 7.3：切曲 / Stop / Seek 时释放所有发声
            _metronome?.Reset(_timeline != null ? _timeline.CurrentTime : 0f);
            _pressedPitches.Clear();
            _pressedSpanValid = false;
        }

        // ==================== 琴键高亮 / 节拍器（方案 2.2） ====================

        /// <summary>循环切换高亮模式：关闭 → 预提示+发声 → 仅发声 → 仅预提示 → 关闭。</summary>
        public void CycleHighlightMode()
        {
            if (_highlighter == null) return;
            HighlightMode next = _highlighter.Mode switch
            {
                HighlightMode.Off => HighlightMode.Both,
                HighlightMode.Both => HighlightMode.Active,
                HighlightMode.Active => HighlightMode.Upcoming,
                _ => HighlightMode.Off
            };
            SetHighlightMode(next);
        }

        /// <summary>设置高亮模式（关闭时彻底清除颜色，不留残留）。</summary>
        public void SetHighlightMode(HighlightMode mode)
        {
            if (_highlighter == null) return;

            // 教学模式固定为「仅高亮被按下的琴键」（就是该模式的定义）；
            // 用户此刻的选择记下来，作为切回展示模式后要恢复的偏好。
            if (_displayMode == DisplayMode.Teaching && mode != HighlightMode.Active)
            {
                _highlightToRestore = mode;
                _hasHighlightToRestore = true;
            }
            else
            {
                _highlighter.Mode = mode;
            }

            if (mode == HighlightMode.Off)
            {
                _highlighter.ClearAll();
                _upcomingBuffer.Clear();
                _camera?.ClearFramingKeySpan();
            }
            OnHighlightModeChanged?.Invoke(_highlighter.Mode);
            if (!_suppressPrefsWrite) { _prefs.HighlightMode = (int)mode; MarkPrefsDirty(); }
        }

        /// <summary>节拍器开关（未加载曲目时给出提示）。</summary>
        public void ToggleMetronome()
        {
            if (_metronome == null) return;
            if (!_metronome.Enabled && !RequireSong("节拍器")) return;
            SetMetronomeEnabled(!_metronome.Enabled);
        }

        /// <summary>显式设置节拍器开关（应用偏好时不弹提示）。</summary>
        public void SetMetronomeEnabled(bool enabled)
        {
            if (_metronome == null) return;
            if (_metronome.Enabled == enabled) return;
            _metronome.Enabled = enabled;
            OnMetronomeToggled?.Invoke(enabled);
            if (!_suppressPrefsWrite) { _prefs.MetronomeEnabled = enabled; MarkPrefsDirty(); }
        }

        /// <summary>设置每小节拍数（4 = 4/4 拍）。</summary>
        public void SetBeatsPerBar(int beatsPerBar)
        {
            if (_metronome == null) return;
            _metronome.BeatsPerBar = beatsPerBar;
            if (!_suppressPrefsWrite) { _prefs.BeatsPerBar = _metronome.BeatsPerBar; MarkPrefsDirty(); }
        }

        // ==================== 文件工具 ====================

        private static bool SafeFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); }
            catch (Exception) { return false; }
        }

        private static bool SafeDirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return Directory.Exists(path); }
            catch (Exception) { return false; }
        }
    }
}
