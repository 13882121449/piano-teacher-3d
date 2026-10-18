using UnityEngine;
using UnityEngine.EventSystems;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.CameraControl
{
    public enum CameraMode
    {
        Closeup, // 手部特写
        Full,    // 全景
        Free     // 自由观察
    }

    /// <summary>
    /// 视角状态机（手部特写 / 全景 / 自由观察）。
    /// <para>
    /// 对应文档：方案 4.1–4.5（三种视角的统一设计）、6.3（角色切换 + 视角跟随组合规则）、
    /// 第 14 章第 1 项（CameraModeController：显式状态机、过渡中断、动态取景边界、返回/重置规则）；
    /// 审查 3.1 A/B/C/D（三状态定义、手部特写重做、全景前侧 3/4、模式切换清晰）、3.2（视角可用性）、
    /// 第 4 章 CameraModeController 行、第 5 章阶段 A。
    /// </para>
    /// <para>
    /// <b>仅改表现层</b>：本组件不持有也不修改 <c>LessonTimeline</c> / MIDI 事件 / 角色状态，
    /// 因此任何视角切换（含连续快速切换）都不会改变播放状态、当前曲目与当前时间（方案 4.5 验收第 1 条）。
    /// </para>
    /// <para>
    /// <b>性能</b>（方案 7.2）：<c>Update</c> 内无 LINQ、无字符串拼接、无 <c>FindObjectsByType</c>、
    /// 无每帧组件查找；包围盒与手部 Transform 引用均缓存或直接读取字段。
    /// </para>
    /// </summary>
    public class CameraModeController : MonoBehaviour
    {
        // ==================== Inspector：全景（方案 4.3 / 审查 3.1C） ====================

        [Header("全景预设（前侧 3/4 角度，修正侧视低音端遮挡；方案 4.3 / 审查 3.1C）")]
        public float OverviewYaw = 34f;          // 水平角：0 = 正前方，34° = 前侧 3/4
        public float OverviewPitch = 16f;        // 俯角
        public float OverviewFov = 50f;          // 基准视野
        public float OverviewMinFov = 40f;
        public float OverviewMaxFov = 62f;
        public float OverviewMinDistance = 1.2f;
        public float OverviewMaxDistance = 4.5f;
        public float OverviewFrameMargin = 1.12f;
        public Vector3 OverviewFocusOffset = new Vector3(0f, 0.02f, 0f);
        public float OverviewReferenceHeight = 1.45f;   // 基准身高（米）
        public float OverviewHeightCompensation = 0.35f; // 身高/头身比轻量校正系数
        public float OverviewFollowDamping = 6f;
        public float OverviewMaxFovStepPerSecond = 20f;

        [Header("全景·「键盘无遮挡」教学子视角（默认关闭，必须明确标识；方案 4.3 禁止默认把琴体透明化）")]
        public bool KeyboardUnobstructedSubView = false;
        public float KeyboardSubViewYaw = 8f;
        public float KeyboardSubViewPitch = 42f;
        public float KeyboardSubViewFocusLift = 0.06f;

        // ==================== Inspector：手部特写（方案 4.2 / 审查 3.1B） ====================

        [Header("手部特写（活动练习手范围 ∪ 当前/即将发生的琴键范围；方案 4.2 / 审查 3.1B）")]
        public float CloseupBaseFov = 30f;            // FOV 下限
        public float CloseupMaxFov = 48f;             // FOV 上限（限制视野变化，抑制"呼吸感"）
        public float CloseupMinDistance = 0.20f;
        // 距离上限：需同时容纳「双手跨度最大 + 窄窗口（4:3 甚至更窄）」的场景。
        // 实测在 640×480 下，大跨度双手（低音端 ↔ 默认手位）会同时顶满距离与 FOV，
        // 使活动手贴到画面边缘（审查 3.2 观察标准明确要求「活动手掌、主要指尖与对应琴键同时可见」）。
        // 上限仍是有界预设（不会随音符无限制拉远），动态变化依旧受 RateLimit 限速。
        public float CloseupMaxDistance = 0.88f;
        public float CloseupFrameMargin = 1.28f;      // 边界余量：手与将按琴键不贴边
        public float CloseupHandPadding = 0.045f;     // 手部范围外扩（掌心体积 + 指尖）
        public float CloseupFingerTipReach = 0.05f;   // 指根 → 指尖的延伸估计（米）
        public float CloseupVerticalFollowLimit = 0.035f; // 焦点垂直跟随幅度上限
        public float CloseupBottomUiInset = 0.02f;    // 底部 UI 预留（焦点下移）
        // 观察俯角（固定值，不随手部动作变化）。
        // 【为什么必须是 60°】手部特写要「从后上方越过头顶看琴键」，而角色头部位于
        // 琴键后方 z≈0.42m、高约 1.09m（写实男性更高）。相机位置 = 焦点 + d·(sinθ, cosθ)，
        // 只要水平后移量 d·cosθ 超过「焦点到头部的水平距离」(≈0.46m)，头部就会落在
        // 相机 → 双手的连线上，画面正中会被一大团头发挡住（实测 34° 时必然遮挡）。
        // 取 θ=60° 时 cosθ=0.5，即使取到最大距离 0.88m，d·cosθ=0.44 < 0.46，相机始终位于头部之前，
        // 从几何上排除头部遮挡；同时保留足够宽度以容纳大跨度双手（审查 3.2 观察标准）。
        public float CloseupViewPitch = 60f;
        public float CloseupViewYaw = 0f;
        public float CloseupPrimaryShare = 0.7f;      // 单手练习时主手的最小宽度占比
        public float CloseupKeyPredictSeconds = 0.12f; // 「即将发生的琴键」轻量预测时间（方案 4.2.3）
        public float CloseupMaxPredictSpeed = 2.5f;   // 预测速度上限（米/秒），抑制跳变
        public float CloseupFollowDamping = 9f;
        public float CloseupMaxFovStepPerSecond = 18f;
        public float CloseupHandVelocityDamping = 6f;
        public float SeekJumpThreshold = 0.45f;       // 手部目标单帧跳变量（米）：判定 Seek/循环回跳

        // ==================== Inspector：自由观察（方案 4.4 / 审查 3.1D） ====================

        [Header("自由观察（右键旋转 + 滚轮缩放；UI 悬停时不响应；方案 4.4 / 审查 3.1D）")]
        public float OrbitSensitivity = 4f;
        public float ZoomSensitivity = 3f;
        public float MinDistance = 0.5f;
        public float MaxDistance = 6f;
        public float MinPitch = 5f;
        public float MaxPitch = 85f;
        public float MinHeight = 0.15f;      // 防穿地
        public float MinYaw = -180f;         // 旋转范围限制：默认不限（仅规整）
        public float MaxYaw = 180f;
        public float FreeCollisionPadding = 0.12f; // 防穿琴体 / 人物
        public float FreeFov = 50f;
        public float FreeFollowDamping = 16f;
        public float FreeMaxFovStepPerSecond = 60f;

        // ==================== Inspector：过渡（方案 4.1 末条 / 审查 3.1D） ====================

        [Header("过渡（统一 0.3–0.5s 缓动；中断以当前实际姿态为新起点，绝不叠加）")]
        public float TransitionDuration = 0.35f;
        public float ResetTransitionDuration = 0.3f;

        // ==================== 公开状态 ====================

        /// <summary>当前视角模式（保持既有语义）。</summary>
        public CameraMode Mode { get; private set; } = CameraMode.Full;

        /// <summary>当前模式的中文名：手部特写 / 全景 / 自由观察（审查 3.1D：状态必须能在 UI 上直接读出）。</summary>
        public string ModeLabel => _presets[(int)Mode].Label;

        /// <summary>是否正在做模式切换过渡。</summary>
        public bool IsTransitioning => _transition.Active;

        /// <summary>自由观察下是否正在被鼠标拖动旋转（用于 UI 显示与外部查询）。</summary>
        public bool IsUserOrbiting => Mode == CameraMode.Free && _userOrbiting;

        /// <summary>进入自由观察前的教学预设；<see cref="ReturnToPreset"/> 返回该预设（方案 4.4 / 审查 3.1D）。</summary>
        public CameraMode PreviousPreset { get; private set; } = CameraMode.Closeup;

        /// <summary>当前相机到当前焦点的距离（米）。</summary>
        public float CurrentDistance => _cam == null ? 0f : Vector3.Distance(_cam.transform.position, _currentFocus);

        /// <summary>「键盘无遮挡」教学子视角开关（默认关闭；方案 4.3 要求必须明确标识为教学模式）。</summary>
        public bool KeyboardUnobstructed
        {
            get => KeyboardUnobstructedSubView;
            set
            {
                if (KeyboardUnobstructedSubView == value) return;
                KeyboardUnobstructedSubView = value;
                if (Mode == CameraMode.Full) _resetPending = true; // 子视角切换同样走统一过渡
            }
        }

        // ==================== 内部状态 ====================

        private readonly CameraModePreset[] _presets = CreatePresets();

        private Camera _cam;
        private AvatarManager _avatar;
        private PianoKeyMap _keyMap;
        private bool _avatarReady;

        private CameraPoseTransition _transition;
        private bool _modeChanged;
        private bool _resetPending;
        private float _resetDuration;

        private PracticeHand _practiceHand = PracticeHand.Both;
        private Vector3 _currentFocus;
        private bool _hasSmoothedFocus;

        // 手部速度预测（方案 4.2.3「轻量预测」）+ 时间回退检测（审查 3.1B「异常状态」）
        private Vector3 _prevRawCenter;
        private Vector3 _handVelocity;
        private bool _hasRawCenter;

        // 缓存包围盒（仅在初始化 / InvalidateFraming / 角色变化时重算；方案 7.2 禁止每帧组件查找）
        private Bounds _subjectBounds;
        private bool _hasSubjectBounds;
        private Bounds _pianoBounds;
        private bool _hasPianoBounds;
        private Bounds _keyboardBounds;
        private bool _hasKeyboardBounds;
        private bool _boundsValid;
        private int _boundsAvatarIndex = -1;

        // 自由观察
        private Vector3 _orbitTarget;
        private float _orbitYaw;
        private float _orbitPitch;
        private float _orbitDistance;
        private float _orbitFov = 50f;
        private bool _userOrbiting;

        // 可选接线：当前/即将发生的琴键音高范围（协调者按帧喂入；未喂入时仅用轻量预测）
        private int _keySpanMinPitch = PianoKeyMap.MinPitch;
        private int _keySpanMaxPitch = PianoKeyMap.MaxPitch;
        private float _keySpanTime = -999f;
        private bool _hasKeySpan;

        /// <summary>外部琴键范围的有效期（秒）：避免协调者停止喂入后长期沿用过期范围。</summary>
        public float KeySpanHoldSeconds = 0.5f;

        private static CameraModePreset[] CreatePresets()
        {
            var presets = new CameraModePreset[3];
            presets[(int)CameraMode.Closeup] = new CameraModePreset(CameraMode.Closeup, "手部特写")
            {
                NearClip = 0.05f,
                FarClip = 30f
            };
            presets[(int)CameraMode.Full] = new CameraModePreset(CameraMode.Full, "全景")
            {
                NearClip = 0.1f,
                FarClip = 60f
            };
            presets[(int)CameraMode.Free] = new CameraModePreset(CameraMode.Free, "自由观察")
            {
                NearClip = 0.05f,
                FarClip = 50f
            };
            return presets;
        }

        // ==================== 生命周期 ====================

        /// <summary>
        /// 装配：摄像机、角色（用于手部取景与全身包围盒）、钢琴（用于键盘范围与自由观察防穿）。
        /// <paramref name="keyMap"/> 允许为 null（此时候手部特写退化为按手部范围的构图，全景不强制 88 键）。
        /// </summary>
        public void Initialize(Camera cam, AvatarManager avatar, PianoKeyMap keyMap = null)
        {
            _cam = cam;
            _avatar = avatar;
            _keyMap = keyMap;
            _avatarReady = _avatar != null && _avatar.CurrentSkeleton != null;
            if (_avatar != null && !_avatarReady)
            {
                // 角色骨架尚未构建：不应崩在 Update 里，明确告警后使用备用构图
                Debug.LogWarning("[CameraModeController] 角色骨架尚未构建，手部特写将暂时使用琴键范围构图");
            }

            RefreshPresetParams();
            InvalidateFraming();
            EnsureBounds(); // 立即计算一次缓存包围盒（角色已由 SessionController 提前构建）

            // 初始化即就位全景标准构图（保持既有语义：初始化不播放"入场动画"）
            Mode = CameraMode.Full;
            _modeChanged = false;
            _transition = default;
            _userOrbiting = false;

            var preset = _presets[(int)CameraMode.Full];
            if (!SolveDesiredPose(0f, preset, out CameraPose pose))
            {
                // 场景资源尚未就绪：使用静态备用构图，避免相机停在编辑器默认位置
                CameraTarget fallback = CameraTarget.Create(
                    new Vector3(0f, 0.80f, 0.10f), 1.2f,
                    FramingPolicy.DirectionFromAngles(OverviewYaw, OverviewPitch), 2.6f, OverviewFov);
                preset.Target = fallback;
                pose = CameraPose.FromTarget(fallback);
            }
            preset.Pose = pose;
            ApplyClipping(preset);
            ApplyPose(pose);
        }

        private void Update()
        {
            if (_cam == null) return;

            float dt = Time.deltaTime;
            if (dt < 1e-4f) dt = 1e-4f; // 极端帧率/暂停帧保护，避免除零与阻尼停滞

            EnsureBounds();
            RefreshPresetParams();

            var preset = _presets[(int)Mode];

            // 自由观察：用户输入只影响相机自身状态，与时间轴完全解耦（方案 4.4）
            if (preset.AllowUserInput) HandleFreeInput();
            else _userOrbiting = false;

            if (!SolveDesiredPose(dt, preset, out CameraPose desired))
            {
                // 场景资源缺失（无角色且无钢琴）：保持当前姿态，不做任何日志输出（Update 内零分配）
                return;
            }

            if (_modeChanged)
            {
                _modeChanged = false;
                _resetPending = false;
                _transition = default;
                _userOrbiting = false;
                _transition.Begin(ReadCameraPose(), desired, TransitionDuration);
                ApplyClipping(preset);
            }
            else if (_resetPending)
            {
                _resetPending = false;
                _transition.Begin(ReadCameraPose(), desired, _resetDuration);
            }

            CameraPose next;
            if (_transition.Active)
            {
                _transition.SetTarget(desired); // 动态预设持续更新终点：不重启过渡、不叠加插值状态
                _transition.Advance(dt);
                next = _transition.Evaluate();
            }
            else
            {
                CameraPose current = ReadCameraPose();
                float k = FramingPolicy.DampFactor(preset.FollowDamping, dt);
                next = new CameraPose
                {
                    Position = Vector3.Lerp(current.Position, desired.Position, k),
                    Rotation = Quaternion.Slerp(current.Rotation, desired.Rotation, k),
                    FieldOfView = Mathf.Lerp(current.FieldOfView, desired.FieldOfView, k)
                };
            }

            // FOV 变化限幅 + 防穿地
            next.FieldOfView = FramingPolicy.RateLimit(_cam.fieldOfView, next.FieldOfView, preset.MaxFovStepPerSecond * dt);
            next.Position = FramingPolicy.ClampMinHeight(next.Position, MinHeight);

            ApplyPose(next);
        }

        // ==================== 公开 API ====================

        /// <summary>切换视角模式（同模式重复调用为无操作；切换只改表现层，不触碰时间轴）。</summary>
        public void SetMode(CameraMode mode)
        {
            if (mode == Mode) return;

            if (mode == CameraMode.Free)
            {
                PreviousPreset = Mode;              // 记录进入前的教学预设（方案 4.4）
                CaptureOrbitFromCurrentPose();      // 从当前实际姿态接管，进入时画面不跳变
            }

            Mode = mode;
            _modeChanged = true;
        }

        /// <summary>循环切换：特写 → 全景 → 自由观察 → 特写。</summary>
        public void CycleMode() => SetMode((CameraMode)(((int)Mode + 1) % 3));

        /// <summary>
        /// 按 R：回到当前模式的标准构图。
        /// 特写/全景 = 回到该模式标准构图；自由观察 = 回到「进入前预设的标准构图」且仍停留在自由观察
        /// （审查 3.1D：R 的语义必须一致且能在 UI 上解释）。
        /// </summary>
        public void ResetView()
        {
            if (Mode == CameraMode.Free)
            {
                ApplyPresetCompositionToOrbit(PreviousPreset);
                return;
            }

            InvalidateFraming();       // 重新按当前时间轴/当前角色计算取景边界
            _resetPending = true;
            _resetDuration = ResetTransitionDuration;
        }

        /// <summary>从自由观察返回进入前的教学预设（UI「返回教学视角」；非自由观察下为无操作）。</summary>
        public void ReturnToPreset()
        {
            // 返回策略由状态预设声明：只有"可返回"的状态（自由观察）会响应
            if (!_presets[(int)Mode].ReturnsToPreviousPreset) return;
            if (PreviousPreset == Mode) return;
            SetMode(PreviousPreset);
        }

        /// <summary>
        /// 驱动手部特写的取景范围（面板「练习手」联动；方案 6.3「角色切换 + 视角跟随」同层规则）。
        /// 只影响构图，不触碰时间轴与 MIDI 事件。
        /// </summary>
        public void SetPracticeHand(PracticeHand hand)
        {
            if (_practiceHand == hand) return;
            _practiceHand = hand;
            InvalidateFraming(); // 练习手变化后按新的活动手重算取景（避免沿旧目标滑过去）
        }

        /// <summary>
        /// 重算取景边界：角色切换、切曲、拖动进度/停止/循环跳回 A 点后调用。
        /// 会清空手部目标平滑与速度预测状态，使相机先按当前时间轴重算目标，再平滑回到构图
        /// （审查 3.1B「异常状态」：不追着旧目标移动）。
        /// </summary>
        public void InvalidateFraming()
        {
            _boundsValid = false;
            _hasSmoothedFocus = false;
            _hasRawCenter = false;
            _handVelocity = Vector3.zero;
        }

        /// <summary>
        /// 可选接线（协调者按帧调用）：喂入「当前时间点及即将发生的琴键」音高范围，
        /// 供手部特写把琴键范围并入取景目标（方案 4.2.1）。未调用时仅使用手部范围 + 轻量预测。
        /// 传入的音高范围应已按练习手过滤（<c>PianoNoteFilter</c>）。
        /// </summary>
        public void SetFramingKeySpan(int minPitch, int maxPitch)
        {
            if (maxPitch < minPitch)
            {
                int t = minPitch;
                minPitch = maxPitch;
                maxPitch = t;
            }
            _keySpanMinPitch = Mathf.Max(minPitch, PianoKeyMap.MinPitch);
            _keySpanMaxPitch = Mathf.Min(maxPitch, PianoKeyMap.MaxPitch);
            _hasKeySpan = _keySpanMaxPitch >= _keySpanMinPitch;
            _keySpanTime = Time.time;
        }

        /// <summary>清除外部喂入的琴键范围（例如停止播放时）。</summary>
        public void ClearFramingKeySpan()
        {
            _hasKeySpan = false;
            _keySpanTime = -999f;
        }

        // ==================== 取景求解（每种模式一个分支，各自持目标/姿态参数） ====================

        private bool SolveDesiredPose(float dt, CameraModePreset preset, out CameraPose pose)
        {
            switch (preset.Mode)
            {
                case CameraMode.Closeup:
                    return SolveCloseupDesired(dt, preset, out pose);
                case CameraMode.Free:
                    return SolveFreeDesired(preset, out pose);
                default:
                    return SolveFullDesired(preset, out pose);
            }
        }

        /// <summary>手部特写：活动练习手范围 ∪ 当前/即将发生的琴键范围 → 平滑跟随（方案 4.2）。</summary>
        private bool SolveCloseupDesired(float dt, CameraModePreset preset, out CameraPose pose)
        {
            pose = default;
            if (!_avatarReady) return false;

            HandRegion left = SampleHandRegion(leftHand: true);
            HandRegion right = SampleHandRegion(leftHand: false);

            // 轻量预测：按手部速度外推「即将按下」的琴键区域（方案 4.2.3）
            bool hasPredict = TryPredictUpcomingRegion(dt, left, right, out Bounds predicted);

            Bounds keyBounds;
            bool hasKeyBounds = TryGetActiveKeySpanBounds(out keyBounds);
            if (hasPredict)
            {
                if (hasKeyBounds) keyBounds.Encapsulate(predicted);
                else
                {
                    keyBounds = predicted;
                    hasKeyBounds = true;
                }
            }

            var tuning = BuildCloseupTuning();
            if (!FramingPolicy.ComputeCloseupTarget(left, right, keyBounds, hasKeyBounds, _practiceHand, tuning,
                    Aspect, out CameraTarget target))
            {
                return false;
            }

            // 焦点指数平滑：不逐帧瞬跳（方案 4.2.3 / 审查 3.1B「阻尼跟随」）
            _currentFocus = _hasSmoothedFocus
                ? FramingPolicy.Damp(_currentFocus, target.Focus, CloseupFollowDamping, dt)
                : target.Focus;
            _hasSmoothedFocus = true;

            target.Focus = _currentFocus;
            preset.Target = target;
            pose = CameraPose.FromTarget(target);
            preset.Pose = pose;
            return true;
        }

        /// <summary>全景：人物包围盒 ∪ 钢琴包围盒的固定构图，前侧 3/4 角度（方案 4.3）。</summary>
        private bool SolveFullDesired(CameraModePreset preset, out CameraPose pose)
        {
            pose = default;
            if (!_boundsValid) return false;

            Bounds keyboard = _hasKeyboardBounds ? _keyboardBounds : SubjectOrKeyboardBounds();
            var tuning = BuildFullTuning();
            CameraTarget target = FramingPolicy.ComputeFullTarget(_hasSubjectBounds ? _subjectBounds : keyboard, keyboard,
                KeyboardUnobstructedSubView, tuning, Aspect);

            _currentFocus = target.Focus; // 全景为固定构图：不追随音符（审查 3.1A）
            preset.Target = target;
            pose = CameraPose.FromTarget(target);
            preset.Pose = pose;
            return true;
        }

        /// <summary>自由观察：保留用户旋转/缩放，并做防穿（方案 4.4 / 审查 3.1D）。</summary>
        private bool SolveFreeDesired(CameraModePreset preset, out CameraPose pose)
        {
            Vector3 focus = _orbitTarget;
            float pitch = FramingPolicy.ClampPitch(_orbitPitch, MinPitch, MaxPitch);
            Vector3 back = FramingPolicy.DirectionFromAngles(_orbitYaw, pitch);
            Vector3 position = focus + back * _orbitDistance;

            // 防穿地（MinHeight）+ 防穿琴体/人物（方案 4.4「碰撞限制避免相机穿过琴体或人物」）
            position = FramingPolicy.ClampMinHeight(position, MinHeight);
            if (_boundsValid)
            {
                if (_hasPianoBounds)
                    position = FramingPolicy.PushOutside(_pianoBounds, FreeCollisionPadding, position);
                if (_hasSubjectBounds)
                    position = FramingPolicy.PushOutside(_subjectBounds, FreeCollisionPadding * 0.5f, position);
            }

            _currentFocus = focus;
            preset.Target = CameraTarget.Create(focus, _orbitDistance, back, _orbitDistance, _orbitFov);
            pose = CameraPose.LookAt(position, focus, _orbitFov);
            preset.Pose = pose;
            return true;
        }

        // ==================== 输入（仅自由观察） ====================

        private void HandleFreeInput()
        {
            // 鼠标悬停在 UI 面板/按钮/进度条上时不得旋转场景（方案 4.4 / 审查 3.1D）
            bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool orbitHeld = Input.GetMouseButton(1);
            _userOrbiting = FramingPolicy.ShouldAcceptOrbitInput(pointerOverUi, orbitHeld);

            if (_userOrbiting)
            {
                _orbitYaw = FramingPolicy.ClampYaw(
                    _orbitYaw + Input.GetAxis("Mouse X") * OrbitSensitivity, MinYaw, MaxYaw);
                _orbitPitch = FramingPolicy.ClampPitch(
                    _orbitPitch - Input.GetAxis("Mouse Y") * OrbitSensitivity, MinPitch, MaxPitch);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!pointerOverUi && Mathf.Abs(scroll) > 1e-4f)
            {
                // 缩放同样受 UI 悬停保护：面板上的滚轮应滚动面板而不是缩放场景
                _orbitDistance = Mathf.Clamp(
                    _orbitDistance - scroll * ZoomSensitivity * _orbitDistance, MinDistance, MaxDistance);
            }
        }

        /// <summary>从当前实际姿态接管自由观察参数：进入自由观察时画面不跳变。</summary>
        private void CaptureOrbitFromCurrentPose()
        {
            _orbitTarget = _currentFocus;
            Vector3 back = _cam != null ? _cam.transform.position - _orbitTarget : Vector3.zero;
            _orbitDistance = Mathf.Clamp(back.magnitude, MinDistance, MaxDistance);

            if (back.sqrMagnitude > 1e-6f)
            {
                Vector3 n = back.normalized;
                _orbitYaw = FramingPolicy.NormalizeYaw(Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg);
                _orbitPitch = FramingPolicy.ClampPitch(Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg, MinPitch, MaxPitch);
            }
            else
            {
                _orbitYaw = 0f;
                _orbitPitch = Mathf.Clamp(30f, MinPitch, MaxPitch);
            }

            float presetFov = _presets[(int)PreviousPreset].Target.FieldOfView;
            _orbitFov = presetFov > 0.01f ? presetFov : FreeFov;
        }

        /// <summary>把某个教学预设的标准构图应用到自由观察（R 键在自由观察下的语义）。</summary>
        private void ApplyPresetCompositionToOrbit(CameraMode mode)
        {
            CameraTarget target;
            var preset = _presets[(int)mode];

            if (mode == CameraMode.Closeup)
            {
                // 特写是动态构图：按当前手部/琴键范围重新求解一次标准构图
                if (!RecomputeCloseupTargetForOrbit(out target)) target = preset.Target;
            }
            else if (mode == CameraMode.Full && _boundsValid)
            {
                Bounds keyboard = _hasKeyboardBounds ? _keyboardBounds : SubjectOrKeyboardBounds();
                target = FramingPolicy.ComputeFullTarget(_hasSubjectBounds ? _subjectBounds : keyboard, keyboard,
                    KeyboardUnobstructedSubView, BuildFullTuning(), Aspect);
            }
            else
            {
                target = preset.Target;
            }

            if (target.Distance <= 0.01f || target.Direction.sqrMagnitude < 1e-6f) return;

            _orbitTarget = target.Focus;
            _orbitDistance = Mathf.Clamp(target.Distance, MinDistance, MaxDistance);
            _orbitFov = target.FieldOfView > 0.01f ? target.FieldOfView : _orbitFov;

            Vector3 n = target.Direction.normalized;
            _orbitYaw = FramingPolicy.NormalizeYaw(Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg);
            _orbitPitch = FramingPolicy.ClampPitch(Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg, MinPitch, MaxPitch);
        }

        private bool RecomputeCloseupTargetForOrbit(out CameraTarget target)
        {
            target = default;
            if (!_avatarReady) return false;

            HandRegion left = SampleHandRegion(leftHand: true);
            HandRegion right = SampleHandRegion(leftHand: false);
            bool hasKeyBounds = TryGetActiveKeySpanBounds(out Bounds keyBounds);
            if (!left.Valid && !right.Valid && !hasKeyBounds) return false;

            return FramingPolicy.ComputeCloseupTarget(left, right, keyBounds, hasKeyBounds, _practiceHand,
                BuildCloseupTuning(), Aspect, out target);
        }

        // ==================== 手部范围采样与琴键范围 ====================

        /// <summary>
        /// 采样单手范围：手腕 + 掌心 + 五指指根 + 指尖延伸（方案 4.2.1：不能只用两腕中心）。
        /// 只用缓存的手部 Transform（<see cref="IHandRig"/>），不做任何组件查找。
        /// </summary>
        private HandRegion SampleHandRegion(bool leftHand)
        {
            if (!_avatarReady) return HandRegion.Empty;

            IHandRig hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            if (hand == null || hand.Wrist == null) return HandRegion.Empty;

            Vector3 wrist = hand.Wrist.position;
            HandRegion region = HandRegion.FromPoint(wrist);

            if (hand.Palm != null) region.Encapsulate(hand.Palm.position);

            var fingers = hand.Fingers;
            if (fingers != null)
            {
                // 使用 for 而非 foreach：IReadOnlyList 的枚举器会装箱，Update 路径内需零分配
                for (int i = 0; i < fingers.Count; i++)
                {
                    Transform f = fingers[i];
                    if (f == null) continue;
                    Vector3 p = f.position;
                    region.Encapsulate(p);

                    Vector3 dir = p - wrist;
                    if (dir.sqrMagnitude > 1e-6f)
                    {
                        region.Encapsulate(p + dir.normalized * CloseupFingerTipReach);
                    }
                }
            }

            return region;
        }

        /// <summary>
        /// 「即将发生的琴键」轻量预测：按平滑后的手部水平速度外推一小段时间（方案 4.2.3）。
        /// 同时检测手部目标单帧大幅跳变（Seek / 停止 / 循环跳回 A 点）并放弃预测。
        /// </summary>
        private bool TryPredictUpcomingRegion(float dt, in HandRegion left, in HandRegion right, out Bounds predicted)
        {
            predicted = default;

            bool hasAny = false;
            Vector3 center = Vector3.zero;
            int count = 0;
            if (left.Valid) { center += left.Center; count++; hasAny = true; }
            if (right.Valid) { center += right.Center; count++; hasAny = true; }
            if (!hasAny || count == 0) return false;

            center /= count;
            Vector3 planarCenter = new Vector3(center.x, 0f, center.z);

            bool jumped = _hasRawCenter && Vector3.Distance(planarCenter, _prevRawCenter) > SeekJumpThreshold;
            if (jumped)
            {
                // 时间回退/跳转：不沿用旧速度，避免相机追着旧目标滑过去（审查 3.1B）
                _handVelocity = Vector3.zero;
            }
            else if (_hasRawCenter && dt > 0f)
            {
                Vector3 instantaneous = (planarCenter - _prevRawCenter) / dt;
                _handVelocity = FramingPolicy.Damp(_handVelocity, instantaneous, CloseupHandVelocityDamping, dt);
            }

            _prevRawCenter = planarCenter;
            _hasRawCenter = true;

            if (jumped || _handVelocity.sqrMagnitude < 1e-6f) return false;

            Vector3 step = Vector3.ClampMagnitude(_handVelocity, CloseupMaxPredictSpeed) * CloseupKeyPredictSeconds;
            if (step.sqrMagnitude < 1e-6f) return false;

            HandRegion upcoming = left.Valid ? left : right;
            if (left.Valid && right.Valid) upcoming.Encapsulate(right);
            upcoming.Expand(CloseupHandPadding);
            upcoming.Encapsulate(upcoming.Center + step);

            predicted = upcoming.ToBounds();
            return true;
        }

        /// <summary>当前是否有可用的外部琴键音高范围（带有效期，防止过期范围长期生效）。</summary>
        private bool IsKeySpanActive => _hasKeySpan && (Time.time - _keySpanTime) <= KeySpanHoldSeconds;

        /// <summary>
        /// 「当前时间点及即将发生的琴键」范围 → 世界包围盒（仅使用外部喂入的音高范围；方案 4.2.1）。
        /// 注意：不存在音高范围时返回 false，<b>不会</b>退化为整个 88 键，
        /// 否则特写会把整条键盘当作用户视区，退化成全景。
        /// </summary>
        private bool TryGetActiveKeySpanBounds(out Bounds bounds)
        {
            bounds = default;
            if (!IsKeySpanActive) return false;
            return TryBuildKeyBoundsForSpan(_keySpanMinPitch, _keySpanMaxPitch, out bounds);
        }

        /// <summary>88 键（A0 pitch=21 至 C8 pitch=108）的世界包围盒（方案 4.3：全景必须容纳完整键盘）。</summary>
        private bool TryBuildKeyBounds(out Bounds bounds) =>
            TryBuildKeyBoundsForSpan(PianoKeyMap.MinPitch, PianoKeyMap.MaxPitch, out bounds);

        /// <summary>把音高区间转换为键的世界包围盒（只取两个端点键，避免遍历 88 键）。</summary>
        private bool TryBuildKeyBoundsForSpan(int minPitch, int maxPitch, out Bounds bounds)
        {
            bounds = default;
            if (_keyMap == null) return false;
            if (!_keyMap.TryGetKey(minPitch, out Transform lowKey) || lowKey == null) return false;
            if (!_keyMap.TryGetKey(maxPitch, out Transform highKey) || highKey == null) return false;

            Vector3 a = lowKey.position;
            Vector3 b = highKey.position;
            float halfWidth = _keyMap.WhiteWidth * 0.5f;
            float halfLength = _keyMap.WhiteLength * 0.5f;
            float topY = Mathf.Max(a.y, b.y) + _keyMap.BlackHeight;

            Vector3 min = new Vector3(Mathf.Min(a.x, b.x) - halfWidth, Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z) - halfLength);
            Vector3 max = new Vector3(Mathf.Max(a.x, b.x) + halfWidth, topY, Mathf.Max(a.z, b.z) + halfLength);
            bounds = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        // ==================== 包围盒缓存 ====================

        /// <summary>
        /// 惰性重算包围盒：仅在无效时或检测到角色 Index 变化时重算（方案 7.2：禁止每帧 GetComponentsInChildren）。
        /// </summary>
        private void EnsureBounds()
        {
            if (_boundsValid)
            {
                if (_avatar == null) return;
                if (_avatar.CurrentIndex == _boundsAvatarIndex) return;
            }
            RebuildBounds();
        }

        /// <summary>
        /// 重算「当前激活角色」与「钢琴」的 Renderer 包围盒。
        /// 只统计 <c>activeInHierarchy</c> 的 Renderer：<see cref="AvatarManager"/> 会把非当前角色 SetActive(false)，
        /// 因此这里必须排除未激活对象（否则新人物的包围盒会被另外两个角色污染）。
        /// </summary>
        private void RebuildBounds()
        {
            _boundsValid = false;
            _boundsAvatarIndex = _avatar != null ? _avatar.CurrentIndex : -1;

            bool hasSubject = false;
            Bounds subject = default;
            if (_avatar != null)
            {
                var renderers = _avatar.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (hasSubject) subject.Encapsulate(r.bounds);
                    else
                    {
                        subject = r.bounds;
                        hasSubject = true;
                    }
                }
            }

            bool hasPiano = false;
            Bounds piano = default;
            if (_keyMap != null)
            {
                var renderers = _keyMap.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (hasPiano) piano.Encapsulate(r.bounds);
                    else
                    {
                        piano = r.bounds;
                        hasPiano = true;
                    }
                }

                if (TryBuildKeyBounds(out Bounds keyBounds))
                {
                    _keyboardBounds = keyBounds;
                    _hasKeyboardBounds = true;
                    if (hasPiano) piano.Encapsulate(keyBounds);
                    else
                    {
                        piano = keyBounds;
                        hasPiano = true;
                    }
                }
                else
                {
                    _keyboardBounds = default;
                    _hasKeyboardBounds = false;
                }
            }
            else
            {
                _keyboardBounds = default;
                _hasKeyboardBounds = false;
            }

            if (!hasSubject) subject = default;
            if (!hasPiano) piano = default;

            _hasSubjectBounds = hasSubject;
            _subjectBounds = subject;
            _hasPianoBounds = hasPiano;
            _pianoBounds = piano;
            _boundsValid = hasSubject || hasPiano;
        }

        /// <summary>没有钢琴时用于全景构图的兜底包围盒（人物包围盒；两者都缺失时返回默认盒）。</summary>
        private Bounds SubjectOrKeyboardBounds()
        {
            if (_hasKeyboardBounds) return _keyboardBounds;
            if (_hasSubjectBounds) return _subjectBounds;
            return new Bounds(new Vector3(0f, 0.8f, 0.2f), new Vector3(1.4f, 1.6f, 1.2f));
        }

        // ==================== 参数与姿态辅助 ====================

        private float Aspect => _cam != null && _cam.aspect > FramingPolicy.MinAspect ? _cam.aspect : 16f / 9f;

        private CloseupFramingTuning BuildCloseupTuning()
        {
            return new CloseupFramingTuning
            {
                BaseFov = CloseupBaseFov,
                MaxFov = CloseupMaxFov,
                MinDistance = CloseupMinDistance,
                MaxDistance = CloseupMaxDistance,
                FrameMargin = CloseupFrameMargin,
                HandPadding = CloseupHandPadding,
                VerticalReferenceY = _keyMap != null ? _keyMap.KeySurfaceY : 0.75f,
                VerticalFollowLimit = CloseupVerticalFollowLimit,
                BottomUiInset = CloseupBottomUiInset,
                ViewPitch = CloseupViewPitch,
                ViewYaw = CloseupViewYaw,
                PrimaryShare = CloseupPrimaryShare
            };
        }

        private FullFramingTuning BuildFullTuning()
        {
            return new FullFramingTuning
            {
                Yaw = OverviewYaw,
                Pitch = OverviewPitch,
                FieldOfView = OverviewFov,
                MinFov = OverviewMinFov,
                MaxFov = OverviewMaxFov,
                FrameMargin = OverviewFrameMargin,
                MinDistance = OverviewMinDistance,
                MaxDistance = OverviewMaxDistance,
                FocusOffset = OverviewFocusOffset,
                ReferenceHeight = OverviewReferenceHeight,
                HeightCompensation = OverviewHeightCompensation,
                KeyboardUnobstructedYaw = KeyboardSubViewYaw,
                KeyboardUnobstructedPitch = KeyboardSubViewPitch,
                KeyboardUnobstructedFocusLift = KeyboardSubViewFocusLift
            };
        }

        /// <summary>把 Inspector 字段刷新到三个状态预设（允许运行时在 Inspector 调参）。</summary>
        private void RefreshPresetParams()
        {
            _resetDuration = ResetTransitionDuration;

            var closeup = _presets[(int)CameraMode.Closeup];
            closeup.FollowDamping = CloseupFollowDamping;
            closeup.MaxFovStepPerSecond = CloseupMaxFovStepPerSecond;
            closeup.AllowUserInput = false;          // 教学预设：禁用鼠标输入，保证构图可预测（方案 4.4）
            closeup.ReturnsToPreviousPreset = false;

            var full = _presets[(int)CameraMode.Full];
            full.FollowDamping = OverviewFollowDamping;
            full.MaxFovStepPerSecond = OverviewMaxFovStepPerSecond;
            full.AllowUserInput = false;
            full.ReturnsToPreviousPreset = false;

            var free = _presets[(int)CameraMode.Free];
            free.FollowDamping = FreeFollowDamping;
            free.MaxFovStepPerSecond = FreeMaxFovStepPerSecond;
            free.AllowUserInput = true;              // 唯一由用户控制的状态
            free.ReturnsToPreviousPreset = true;     // 返回策略：可回到进入前的教学预设
        }

        private void ApplyClipping(CameraModePreset preset)
        {
            if (_cam == null) return;
            _cam.nearClipPlane = preset.NearClip;
            _cam.farClipPlane = preset.FarClip;
        }

        private CameraPose ReadCameraPose()
        {
            Transform t = _cam.transform;
            return new CameraPose
            {
                Position = t.position,
                Rotation = t.rotation,
                FieldOfView = _cam.fieldOfView
            };
        }

        private void ApplyPose(in CameraPose pose)
        {
            Transform t = _cam.transform;
            t.position = pose.Position;
            t.rotation = pose.Rotation;
            if (pose.FieldOfView > 0.01f) _cam.fieldOfView = pose.FieldOfView;
        }
    }
}
