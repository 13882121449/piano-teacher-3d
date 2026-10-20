using PianoTeacher3D.Core;
using UnityEngine;

namespace PianoTeacher3D.CameraControl
{
    /// <summary>
    /// 取景目标：描述「看什么」，与相机姿态分离（审查 3.1B：明确区分 CameraTarget / CameraPose / FramingPolicy）。
    /// <para>
    /// 纯数据，不引用任何 MonoBehaviour，可在 EditMode 单测中直接构造与断言。
    /// </para>
    /// </summary>
    public struct CameraTarget
    {
        /// <summary>观察焦点（世界坐标），相机始终朝向该点。</summary>
        public Vector3 Focus;

        /// <summary>需要覆盖的观察半径（米）：包围盒相对焦点的最大角距，供诊断与断言使用。</summary>
        public float Radius;

        /// <summary>单位方向：由焦点指向相机（<c>Focus + Direction * Distance</c> = 相机位置）。</summary>
        public Vector3 Direction;

        /// <summary>相机到焦点的距离（米，已做上下限幅）。</summary>
        public float Distance;

        /// <summary>垂直视野（度，已做上下限幅）。</summary>
        public float FieldOfView;

        public static CameraTarget Create(Vector3 focus, float radius, Vector3 direction, float distance, float fieldOfView)
        {
            return new CameraTarget
            {
                Focus = focus,
                Radius = radius,
                Direction = direction,
                Distance = distance,
                FieldOfView = fieldOfView
            };
        }
    }

    /// <summary>
    /// 相机姿态：描述「相机在哪、朝哪看、视野多大」。
    /// 与 <see cref="CameraTarget"/> 分离后，过渡中断只需比较/插值姿态，不必重算取景语义。
    /// </summary>
    public struct CameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float FieldOfView;

        public static CameraPose Create(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            return new CameraPose { Position = position, Rotation = rotation, FieldOfView = fieldOfView };
        }

        /// <summary>由取景目标求解姿态：沿目标方向后退 <see cref="CameraTarget.Distance"/>，上向量固定为世界 Y（杜绝滚转）。</summary>
        public static CameraPose FromTarget(in CameraTarget target)
        {
            Vector3 back = target.Direction.sqrMagnitude > 1e-8f ? target.Direction.normalized : Vector3.forward;
            return LookAt(target.Focus + back * target.Distance, target.Focus, target.FieldOfView);
        }

        /// <summary>朝向指定焦点的姿态；方向退化时保持单位旋转，避免 NaN。</summary>
        public static CameraPose LookAt(Vector3 position, Vector3 focus, float fieldOfView)
        {
            Vector3 dir = focus - position;
            Quaternion rot = dir.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(dir, Vector3.up)
                : Quaternion.identity;
            return new CameraPose { Position = position, Rotation = rot, FieldOfView = fieldOfView };
        }
    }

    /// <summary>
    /// 单手取景范围：手腕 / 掌心 / 指根与指尖延伸的世界坐标包围盒（方案 4.2.1 / 审查 3.1B）。
    /// <para>
    /// 手部特写的目标点由「活动练习手范围 ∪ 当前及即将发生的琴键范围」共同计算，
    /// <b>不是</b>两腕中心的平均值：本结构体保留完整范围，由 <see cref="FramingPolicy"/> 做并集与并集受限合并。
    /// </para>
    /// </summary>
    public struct HandRegion
    {
        /// <summary>是否已采集到有效数据。</summary>
        public bool Valid;

        public Vector3 Min;
        public Vector3 Max;

        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;

        public static HandRegion Empty => new HandRegion { Valid = false, Min = Vector3.zero, Max = Vector3.zero };

        public static HandRegion FromPoint(Vector3 point)
        {
            return new HandRegion { Valid = true, Min = point, Max = point };
        }

        public static HandRegion FromBounds(in Bounds bounds)
        {
            return new HandRegion { Valid = true, Min = bounds.min, Max = bounds.max };
        }

        /// <summary>并入一个点。</summary>
        public void Encapsulate(Vector3 point)
        {
            if (!Valid)
            {
                Valid = true;
                Min = point;
                Max = point;
                return;
            }
            Min = Vector3.Min(Min, point);
            Max = Vector3.Max(Max, point);
        }

        /// <summary>并入另一个范围（无效范围自动忽略）。</summary>
        public void Encapsulate(in HandRegion other)
        {
            if (!other.Valid) return;
            Encapsulate(other.Min);
            Encapsulate(other.Max);
        }

        /// <summary>各方向外扩固定余量（米）。</summary>
        public void Expand(float padding)
        {
            if (!Valid || padding <= 0f) return;
            var v = new Vector3(padding, padding, padding);
            Min -= v;
            Max += v;
        }

        public Bounds ToBounds() => new Bounds(Center, Size);

        /// <summary>该范围的 X 区间是否与给定区间相交（用于单手练习时的受限合并）。</summary>
        public bool IntersectsXRange(float minX, float maxX) => Valid && Min.x <= maxX && Max.x >= minX;
    }

    /// <summary>
    /// 手部特写取景参数（对应方案 4.2、审查 3.1B）。纯数据，便于 EditMode 单测直接构造。
    /// </summary>
    public struct CloseupFramingTuning
    {
        /// <summary>FOV 下限（度）：视线收紧的下限，避免"贴近时画面剧烈放大"。</summary>
        public float BaseFov;

        /// <summary>FOV 上限（度）：限制视野变化幅度，避免"呼吸感"过强（方案 4.2.2）。</summary>
        public float MaxFov;

        /// <summary>距离下限（米）。</summary>
        public float MinDistance;

        /// <summary>距离上限（米）：双手相距很远时不继续拉远（方案 4.2.2）。</summary>
        public float MaxDistance;

        /// <summary>取景余量倍数：保证活动手与即将按下的琴键不贴边（方案 4.2.5）。</summary>
        public float FrameMargin;

        /// <summary>手部范围外扩量（米）：覆盖掌心体积与指尖。</summary>
        public float HandPadding;

        /// <summary>焦点垂直参考高度（米，通常取键面高度）：限制手腕上下移动带来的焦点垂直漂移。</summary>
        public float VerticalReferenceY;

        /// <summary>焦点垂直跟随幅度上限（米，方案 4.2.7 / 审查 3.1B「限制垂直范围」）。</summary>
        public float VerticalFollowLimit;

        /// <summary>底部 UI 预留（米）：焦点下移使手部位于画面偏上，避免被控制栏遮挡。</summary>
        public float BottomUiInset;

        /// <summary>观察俯角（度，固定值：不随手部动作改变）。</summary>
        public float ViewPitch;

        /// <summary>观察水平角（度，固定值）。</summary>
        public float ViewYaw;

        /// <summary>单手练习时主手在取景框 X 宽度中的最小占比 (0,1]，用于"以主手为主并尽量保留另一只手可见性"。</summary>
        public float PrimaryShare;

        /// <summary>
        /// 俯角上限（度）：躯干进入特写画面时向上抬角的搜索上限（接触方案批次 3.2）。
        /// 高于 <see cref="ViewPitch"/>，仅在必要时使用。
        /// </summary>
        public float ViewPitchMax;

        /// <summary>
        /// 躯干避让余量（0..1，视口归一化坐标）：躯干包围盒中心必须落在「视口内缩该比例」之外才算不遮挡。
        /// </summary>
        public float TorsoMargin;

        /// <summary>默认参数（与 <c>CameraModeController</c> 的 Inspector 默认值保持一致）。</summary>
        public static CloseupFramingTuning Default()
        {
            return new CloseupFramingTuning
            {
                BaseFov = 30f,
                MaxFov = 44f,
                MinDistance = 0.20f,
                MaxDistance = 0.75f,
                FrameMargin = 1.28f,
                HandPadding = 0.045f,
                VerticalReferenceY = 0.75f,
                VerticalFollowLimit = 0.035f,
                BottomUiInset = 0.02f,
                ViewPitch = 34f,
                ViewYaw = 0f,
                PrimaryShare = 0.7f,
                ViewPitchMax = 70f,
                TorsoMargin = 0.02f
            };
        }
    }

    /// <summary>
    /// 全景取景参数（对应方案 4.3、审查 3.1C：前侧 3/4 角度 + 人物与钢琴联合包围盒）。
    /// </summary>
    public struct FullFramingTuning
    {
        /// <summary>水平角（度）：0 = 正前方，正值偏向 +X 侧（前侧 3/4 角度，替代纯右侧方）。</summary>
        public float Yaw;

        /// <summary>俯角（度）。</summary>
        public float Pitch;

        /// <summary>基准垂直视野（度）。</summary>
        public float FieldOfView;

        /// <summary>FOV 下限（度）。</summary>
        public float MinFov;

        /// <summary>FOV 上限（度）。</summary>
        public float MaxFov;

        /// <summary>取景余量倍数。</summary>
        public float FrameMargin;

        /// <summary>距离下限（米）。</summary>
        public float MinDistance;

        /// <summary>距离上限（米）。</summary>
        public float MaxDistance;

        /// <summary>焦点微调（世界向量，向上为正 = 画面内容下移），用于避让顶栏/底部控制栏。</summary>
        public Vector3 FocusOffset;

        /// <summary>基准身高（米）：身高/头身比轻量校正的参照（审查 3.1C）。</summary>
        public float ReferenceHeight;

        /// <summary>身高补偿系数（0 = 不校正，1 = 完全按比例）：轻量校正，不为每个角色写不同规则。</summary>
        public float HeightCompensation;

        /// <summary>「键盘无遮挡」教学子视角的水平角（度，默认关闭；方案 4.3 禁止把琴体透明化作为默认修复）。</summary>
        public float KeyboardUnobstructedYaw;

        /// <summary>「键盘无遮挡」教学子视角的俯角（度）。</summary>
        public float KeyboardUnobstructedPitch;

        /// <summary>「键盘无遮挡」教学子视角的焦点抬升（米）。</summary>
        public float KeyboardUnobstructedFocusLift;

        /// <summary>默认参数（与 <c>CameraModeController</c> 的 Inspector 默认值保持一致）。</summary>
        public static FullFramingTuning Default()
        {
            return new FullFramingTuning
            {
                Yaw = 34f,
                Pitch = 16f,
                FieldOfView = 50f,
                MinFov = 40f,
                MaxFov = 62f,
                FrameMargin = 1.12f,
                MinDistance = 1.2f,
                MaxDistance = 4.5f,
                FocusOffset = new Vector3(0f, 0.02f, 0f),
                ReferenceHeight = 1.45f,
                HeightCompensation = 0.35f,
                KeyboardUnobstructedYaw = 8f,
                KeyboardUnobstructedPitch = 42f,
                KeyboardUnobstructedFocusLift = 0.06f
            };
        }
    }

    /// <summary>
    /// 单次相机姿态过渡（方案 4.4 末条 / 审查 3.1D）。
    /// <para>
    /// 刻意做成结构体而非协程：全组件只持有<b>一个</b>过渡实例，
    /// 连续快速切换时再次 <see cref="Begin"/> 即以"当前实际姿态"为新起点，
    /// 不会叠加多个插值状态造成抖动。
    /// </para>
    /// </summary>
    public struct CameraPoseTransition
    {
        public CameraPose From;
        public CameraPose To;
        public float Duration;
        public float Elapsed;
        public bool Active;

        public void Begin(in CameraPose from, in CameraPose to, float duration)
        {
            From = from;
            To = to;
            Duration = Mathf.Max(duration, 0.01f);
            Elapsed = 0f;
            Active = true;
        }

        /// <summary>更新终点：动态预设（手部特写）持续改目标时不会重启过渡进度。</summary>
        public void SetTarget(in CameraPose to) => To = to;

        /// <summary>推进时间；返回是否仍在过渡中。</summary>
        public bool Advance(float dt)
        {
            if (!Active) return false;
            Elapsed += dt;
            if (Elapsed >= Duration)
            {
                Elapsed = Duration;
                Active = false;
            }
            return Active;
        }

        /// <summary>按 SmoothStep 求当前姿态；过渡结束后返回终点姿态。</summary>
        public CameraPose Evaluate()
        {
            if (Duration <= 0f) return To; // 未启动过（default 结构体）时不做 0/0
            float s = FramingPolicy.SmoothStep01(Elapsed / Duration);
            return new CameraPose
            {
                Position = Vector3.Lerp(From.Position, To.Position, s),
                Rotation = Quaternion.Slerp(From.Rotation, To.Rotation, s),
                FieldOfView = Mathf.Lerp(From.FieldOfView, To.FieldOfView, s)
            };
        }
    }

    /// <summary>
    /// 单个视角状态持有的全部参数（方案 4.1「每种状态都应有明确的目标点、相机位置/角度、视野范围、
    /// 近远裁剪面、阻尼参数、禁用输入规则和返回策略」；审查 3.1A 的显式三状态）。
    /// </summary>
    public sealed class CameraModePreset
    {
        /// <summary>对应视角模式。</summary>
        public CameraMode Mode;

        /// <summary>UI 展示名（"手部特写" / "全景" / "自由观察"）。</summary>
        public string Label;

        /// <summary>当前取景目标（每帧或每次重算刷新）。</summary>
        public CameraTarget Target;

        /// <summary>当前目标姿态。</summary>
        public CameraPose Pose;

        /// <summary>近裁剪面（米）。</summary>
        public float NearClip = 0.1f;

        /// <summary>远裁剪面（米）。</summary>
        public float FarClip = 60f;

        /// <summary>模式内跟随阻尼速率（1/s，指数平滑）。</summary>
        public float FollowDamping = 8f;

        /// <summary>FOV 每秒最大变化量（度/秒）：限制视野变化速度（方案 4.2.2）。</summary>
        public float MaxFovStepPerSecond = 20f;

        /// <summary>是否允许鼠标输入（禁用输入规则：只有自由观察为 true）。</summary>
        public bool AllowUserInput;

        /// <summary>返回策略：为 true 时该状态记录并可返回进入前的教学预设（仅自由观察）。</summary>
        public bool ReturnsToPreviousPreset;

        public CameraModePreset(CameraMode mode, string label)
        {
            Mode = mode;
            Label = label;
        }
    }

    /// <summary>
    /// 纯数学取景策略（方案 4.2 / 4.3 / 4.4，审查 3.1B / 3.1C）。
    /// <para>
    /// 全部为静态方法，只依赖 UnityEngine 的值类型（Vector3/Bounds/Mathf），
    /// 不持有场景引用、不含 MonoBehaviour，因此可在 EditMode 单测中直接验证取景数学。
    /// </para>
    /// <para>
    /// 约束 7.2：本类所有方法均无 LINQ、无字符串拼接、无堆分配。
    /// </para>
    /// </summary>
    public static class FramingPolicy
    {
        /// <summary>最小宽高比（防止极端窗口尺寸导致除零）。</summary>
        public const float MinAspect = 0.1f;

        /// <summary>相机距离被摄物的最小安全深度（米）。</summary>
        private const float MinDepth = 0.02f;

        // ==================== 基础几何 ====================

        /// <summary>由水平角/俯角求单位方向（焦点 → 相机）：yaw 0 = 正前方（+Z），pitch 正 = 相机在焦点上方。</summary>
        public static Vector3 DirectionFromAngles(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitch);
            return new Vector3(Mathf.Sin(yaw) * cosPitch, Mathf.Sin(pitch), Mathf.Cos(yaw) * cosPitch);
        }

        /// <summary>把角度规整到 (-180, 180]。</summary>
        public static float NormalizeYaw(float yawDeg) => Mathf.Repeat(yawDeg + 180f, 360f) - 180f;

        /// <summary>限制水平角范围；区间宽于 359° 时视为不限（仅规整）。</summary>
        public static float ClampYaw(float yawDeg, float minYaw, float maxYaw)
        {
            float yaw = NormalizeYaw(yawDeg);
            if (maxYaw - minYaw >= 359f) return yaw;
            return Mathf.Clamp(yaw, minYaw, maxYaw);
        }

        /// <summary>限制俯仰角（方案 4.4「通过旋转范围限制避免穿过琴体或人物」）。</summary>
        public static float ClampPitch(float pitchDeg, float minPitch, float maxPitch) => Mathf.Clamp(pitchDeg, minPitch, maxPitch);

        /// <summary>限制最低高度，防止相机穿地。</summary>
        public static Vector3 ClampMinHeight(Vector3 position, float minHeight) =>
            position.y >= minHeight ? position : new Vector3(position.x, minHeight, position.z);

        /// <summary>求包围盒相对给定焦点/方向的最大覆盖半径（米）。</summary>
        public static float CoverRadius(in Bounds box, Vector3 focus)
        {
            Vector3 min = box.min;
            Vector3 max = box.max;
            float radius = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                float d = (corner - focus).magnitude;
                if (d > radius) radius = d;
            }
            return radius;
        }

        /// <summary>
        /// 把相机位置推出包围盒（含 padding）。
        /// 用于自由观察防穿琴体/人物（方案 4.4）；点不在盒内时原样返回。
        /// </summary>
        public static Vector3 PushOutside(in Bounds bounds, float padding, Vector3 point)
        {
            float minX = bounds.min.x - padding, maxX = bounds.max.x + padding;
            float minY = bounds.min.y - padding, maxY = bounds.max.y + padding;
            float minZ = bounds.min.z - padding, maxZ = bounds.max.z + padding;

            if (point.x <= minX || point.x >= maxX ||
                point.y <= minY || point.y >= maxY ||
                point.z <= minZ || point.z >= maxZ)
            {
                return point;
            }

            float best = point.x - minX;
            var result = new Vector3(minX, point.y, point.z);

            float d = maxX - point.x;
            if (d < best) { best = d; result = new Vector3(maxX, point.y, point.z); }

            d = point.y - minY;
            if (d < best) { best = d; result = new Vector3(point.x, minY, point.z); }

            d = maxY - point.y;
            if (d < best) { best = d; result = new Vector3(point.x, maxY, point.z); }

            d = point.z - minZ;
            if (d < best) { best = d; result = new Vector3(point.x, point.y, minZ); }

            d = maxZ - point.z;
            if (d < best) { result = new Vector3(point.x, point.y, maxZ); }

            return result;
        }

        // ==================== 阻尼与限幅 ====================

        /// <summary>指数平滑系数：speed = 0 表示直接对齐。</summary>
        public static float DampFactor(float speed, float dt)
        {
            if (speed <= 0f) return 1f;
            if (dt <= 0f) return 0f;
            return Mathf.Clamp01(1f - Mathf.Exp(-speed * dt));
        }

        /// <summary>指数平滑跟随（不逐帧瞬跳；方案 4.2.3 / 审查 3.1B「阻尼跟随」）。</summary>
        public static float Damp(float current, float target, float speed, float dt) =>
            current + (target - current) * DampFactor(speed, dt);

        /// <summary>指数平滑跟随（向量版）。</summary>
        public static Vector3 Damp(Vector3 current, Vector3 target, float speed, float dt) =>
            Vector3.Lerp(current, target, DampFactor(speed, dt));

        /// <summary>单步变化限幅（用于 FOV：限制视野变化速度，抑制"呼吸感"）。</summary>
        public static float RateLimit(float current, float target, float maxStep)
        {
            if (maxStep <= 0f) return target;
            float delta = target - current;
            if (Mathf.Abs(delta) <= maxStep) return target;
            return current + Mathf.Sign(delta) * maxStep;
        }

        /// <summary>统一缓动曲线（0→1 单调，用于模式切换）。</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 自由观察是否接受鼠标输入（方案 4.4 / 审查 3.1D）：
        /// 指针悬停在 UI 面板/按钮/进度条上或未按下旋转键时，不得旋转场景。
        /// 纯函数，便于单测。
        /// </summary>
        public static bool ShouldAcceptOrbitInput(bool pointerOverUi, bool orbitButtonHeld) =>
            orbitButtonHeld && !pointerOverUi;

        // ==================== 取景求解 ====================

        /// <summary>构建观察基底：back（焦点→相机）、right、up。up 固定，因此画面永不出现滚转。</summary>
        private static void BuildBasis(Vector3 cameraBack, out Vector3 back, out Vector3 right, out Vector3 up)
        {
            back = cameraBack.sqrMagnitude > 1e-8f ? cameraBack.normalized : Vector3.forward;
            Vector3 forward = -back;
            right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            else right.Normalize();
            up = Vector3.Cross(right, forward).normalized;
        }

        /// <summary>给定垂直/水平半角的正切，求让包围盒完整入画所需的最小距离（精确按 8 个角点求解）。</summary>
        private static float RequiredDistanceForBox(in Bounds box, Vector3 focus, Vector3 back, Vector3 right, Vector3 up,
            float frameMargin, float tanVertical, float tanHorizontal)
        {
            float tanV = Mathf.Max(tanVertical, 1e-4f);
            float tanH = Mathf.Max(tanHorizontal, 1e-4f);
            Vector3 min = box.min;
            Vector3 max = box.max;
            float required = MinDepth;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                Vector3 q = corner - focus;
                float towardCamera = Vector3.Dot(q, back);
                float horizontal = Mathf.Abs(Vector3.Dot(q, right)) * frameMargin;
                float vertical = Mathf.Abs(Vector3.Dot(q, up)) * frameMargin;

                float d = towardCamera + vertical / tanV;
                float dh = towardCamera + horizontal / tanH;
                if (dh > d) d = dh;
                if (d > required) required = d;
            }
            return required;
        }

        /// <summary>给定距离，求让包围盒完整入画所需的垂直半角正切（取各角点需求的最大值）。</summary>
        private static float RequiredTanHalfVertical(in Bounds box, Vector3 focus, Vector3 back, Vector3 right, Vector3 up,
            float frameMargin, float distance, float aspect)
        {
            float safeAspect = Mathf.Max(aspect, MinAspect);
            Vector3 min = box.min;
            Vector3 max = box.max;
            float maxTan = 1e-3f;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);

                Vector3 q = corner - focus;
                float depth = Mathf.Max(distance - Vector3.Dot(q, back), MinDepth);
                float tanV = Mathf.Abs(Vector3.Dot(q, up)) * frameMargin / depth;
                float tanH = Mathf.Abs(Vector3.Dot(q, right)) * frameMargin / (depth * safeAspect);
                float t = Mathf.Max(tanV, tanH);
                if (t > maxTan) maxTan = t;
            }
            return maxTan;
        }

        /// <summary>
        /// 取景求解核心：以「距离为主、FOV 为辅」求解相机距离与视野（方案 4.2.2 / 审查 3.1B）。
        /// <list type="number">
        /// <item>先用 FOV 下限求基准距离 —— 手越远/范围越大，相机越远；</item>
        /// <item>距离被上限截断时用 FOV 补足，FOV 再限幅（避免视野变化过强）；</item>
        /// <item>用受限后的 FOV 反算最终距离；极端跨度时以限幅优先（不追求全覆盖，避免镜头剧烈拉远与抖动）。</item>
        /// </list>
        /// 纯函数，无分配。
        /// </summary>
        public static void SolveFraming(in Bounds box, Vector3 focus, Vector3 cameraBack,
            float fovMin, float fovMax, float distanceMin, float distanceMax, float frameMargin,
            float aspect, out float distance, out float fov)
        {
            float safeAspect = Mathf.Max(aspect, MinAspect);
            float margin = Mathf.Max(frameMargin, 0.01f);
            float minFov = Mathf.Clamp(fovMin, 1f, 179f);
            float maxFov = Mathf.Clamp(fovMax, minFov, 179f);
            float minDistance = Mathf.Max(distanceMin, MinDepth);
            float maxDistance = Mathf.Max(distanceMax, minDistance);

            BuildBasis(cameraBack, out Vector3 back, out Vector3 right, out Vector3 up);

            float tanMin = Mathf.Tan(minFov * Mathf.Deg2Rad * 0.5f);
            float baseDistance = RequiredDistanceForBox(box, focus, back, right, up, margin, tanMin, tanMin * safeAspect);
            distance = Mathf.Clamp(baseDistance, minDistance, maxDistance);

            float tanNeeded = RequiredTanHalfVertical(box, focus, back, right, up, margin, distance, safeAspect);
            fov = Mathf.Clamp(Mathf.Atan(tanNeeded) * 2f * Mathf.Rad2Deg, minFov, maxFov);

            float tan = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
            float required = RequiredDistanceForBox(box, focus, back, right, up, margin, tan, tan * safeAspect);
            distance = Mathf.Clamp(required, minDistance, maxDistance);
        }

        // ==================== 手部特写（方案 4.2 / 审查 3.1B） ====================

        /// <summary>
        /// 单手练习时的受限合并：以主手范围为主，向次手方向最多扩展到「主手宽度 / share」，
        /// 从而保留双手可见性，又保证主手始终占据画面主导（方案 4.2.4 / 审查 3.1B「单手练习」）。
        /// </summary>
        public static HandRegion MergeWithShare(in HandRegion primary, in HandRegion secondary, float share)
        {
            if (!primary.Valid) return secondary;
            if (!secondary.Valid) return primary;

            float shareClamped = Mathf.Clamp(share, 0.05f, 1f);
            float primaryWidth = Mathf.Max(primary.Max.x - primary.Min.x, 1e-4f);
            float allowance = primaryWidth * (1f / shareClamped - 1f); // 允许向次手方向扩展的最大距离

            var result = primary;
            float secondaryCenterX = (secondary.Min.x + secondary.Max.x) * 0.5f;
            float primaryCenterX = (primary.Min.x + primary.Max.x) * 0.5f;

            if (secondaryCenterX >= primaryCenterX)
            {
                float rightLimit = primary.Max.x + allowance;
                if (secondary.Min.x < rightLimit)
                {
                    result.Max = new Vector3(
                        Mathf.Max(primary.Max.x, Mathf.Min(secondary.Max.x, rightLimit)),
                        Mathf.Max(primary.Max.y, secondary.Max.y),
                        Mathf.Max(primary.Max.z, secondary.Max.z));
                    result.Min = new Vector3(primary.Min.x, Mathf.Min(primary.Min.y, secondary.Min.y), Mathf.Min(primary.Min.z, secondary.Min.z));
                }
            }
            else
            {
                float leftLimit = primary.Min.x - allowance;
                if (secondary.Max.x > leftLimit)
                {
                    result.Min = new Vector3(
                        Mathf.Min(primary.Min.x, Mathf.Max(secondary.Min.x, leftLimit)),
                        Mathf.Min(primary.Min.y, secondary.Min.y),
                        Mathf.Min(primary.Min.z, secondary.Min.z));
                    result.Max = new Vector3(primary.Max.x, Mathf.Max(primary.Max.y, secondary.Max.y), Mathf.Max(primary.Max.z, secondary.Max.z));
                }
            }
            return result;
        }

        /// <summary>
        /// 计算手部特写的联合取景范围：活动练习手范围 ∪ 当前及即将发生的琴键范围（方案 4.2.1）。
        /// <para>
        /// Both = 双手等权并集；LeftOnly / RightOnly = 以主手为主并按 <see cref="CloseupFramingTuning.PrimaryShare"/> 受限并入次手与琴键范围。
        /// </para>
        /// </summary>
        public static bool TryCombineCloseupRegion(in HandRegion left, in HandRegion right,
            in Bounds keyBounds, bool hasKeyBounds, PracticeHand hand, in CloseupFramingTuning tuning,
            out Bounds region)
        {
            region = default;

            var leftPadded = left;
            var rightPadded = right;
            leftPadded.Expand(tuning.HandPadding);
            rightPadded.Expand(tuning.HandPadding);

            HandRegion merged;
            if (hand == PracticeHand.Both)
            {
                merged = leftPadded;
                merged.Encapsulate(rightPadded);
            }
            else
            {
                var primary = hand == PracticeHand.LeftOnly ? leftPadded : rightPadded;
                var secondary = hand == PracticeHand.LeftOnly ? rightPadded : leftPadded;
                merged = primary.Valid ? MergeWithShare(primary, secondary, tuning.PrimaryShare) : secondary;
            }

            if (hasKeyBounds)
            {
                var keyRegion = HandRegion.FromBounds(keyBounds);
                if (!merged.Valid) merged = keyRegion;
                else if (hand == PracticeHand.Both) merged.Encapsulate(keyRegion);
                else merged = MergeWithShare(merged, keyRegion, tuning.PrimaryShare);
            }

            if (!merged.Valid) return false;
            region = merged.ToBounds();
            return true;
        }

        /// <summary>
        /// 计算焦点：X/Z 跟手平移，Y 以键面高度为参考并限制跟随幅度，
        /// 因此手腕轻微上下移动不会明显改变观察俯仰（方案 4.2.7 / 审查 3.1B）。
        /// </summary>
        public static Vector3 FocusWithVerticalLimit(in Bounds region, in CloseupFramingTuning tuning)
        {
            Vector3 center = region.center;
            float delta = Mathf.Clamp(center.y - tuning.VerticalReferenceY, -tuning.VerticalFollowLimit, tuning.VerticalFollowLimit);
            return new Vector3(center.x, tuning.VerticalReferenceY + delta - tuning.BottomUiInset, center.z);
        }

        /// <summary>
        /// 手部特写取景目标（方案 4.2）：联合范围 → 焦点 → 距离/FOV。
        /// 没有任何手部数据且无琴键范围时返回 false（调用方使用备用构图）。
        /// </summary>
        public static bool ComputeCloseupTarget(in HandRegion left, in HandRegion right,
            in Bounds keyBounds, bool hasKeyBounds, PracticeHand hand, in CloseupFramingTuning tuning,
            float aspect, out CameraTarget target)
        {
            return ComputeCloseupTarget(left, right, keyBounds, hasKeyBounds, hand, tuning, aspect,
                default, false, out target);
        }

        /// <summary>
        /// 世界点是否落在该取景目标的视口内。
        /// <para><paramref name="margin01"/> 为视口内缩比例（0 = 整幅画面），用于「躯干中心不得靠边」的判据。</para>
        /// </summary>
        public static bool IsPointInViewport(Vector3 point, in CameraTarget target, float aspect, float margin01 = 0f)
        {
            Vector3 back = target.Direction.sqrMagnitude > 1e-8f ? target.Direction.normalized : Vector3.forward;
            Vector3 cameraPosition = target.Focus + back * target.Distance;
            BuildBasis(back, out _, out Vector3 right, out Vector3 up);
            Vector3 forward = -back;

            Vector3 v = point - cameraPosition;
            float depth = Vector3.Dot(v, forward);
            if (depth <= MinDepth) return false; // 在相机后面（或贴在镜头上）：不算入画

            float tanV = Mathf.Tan(Mathf.Max(target.FieldOfView, 1f) * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Mathf.Max(aspect, MinAspect);
            float nx = Mathf.Abs(Vector3.Dot(v, right)) / (depth * tanH);
            float ny = Mathf.Abs(Vector3.Dot(v, up)) / (depth * tanV);
            float limit = 1f - Mathf.Clamp01(margin01);
            return nx <= limit && ny <= limit;
        }

        /// <summary>躯干包围盒中心是否在视口内（批次 3.3 的断言口径）。</summary>
        public static bool IsTorsoCenterInViewport(in Bounds torsoBounds, in CameraTarget target, float aspect, float margin01 = 0f)
        {
            return IsPointInViewport(torsoBounds.center, target, aspect, margin01);
        }

        /// <summary>
        /// 手部特写取景目标（含躯干避让，接触方案批次 3）。
        /// <para>
        /// 先按方案 4.2 求「手部 ∪ 琴键」构图；若躯干包围盒中心落入视口，则在两组候选里挑：
        /// <b>A</b> 沿视线后移（上限 <see cref="CloseupFramingTuning.MaxDistance"/>）、
        /// <b>B</b> 抬高俯角（<see cref="CloseupFramingTuning.ViewPitch"/> → <see cref="CloseupFramingTuning.ViewPitchMax"/>）。
        /// 只接受「躯干中心已出画」的候选，并按方案 3.2 取手部占屏（<c>Radius / Distance</c>）更大者；
        /// 两组都不满足时退回占屏更大的候选，由调用方与断言如实暴露。
        /// </para>
        /// </summary>
        public static bool ComputeCloseupTarget(in HandRegion left, in HandRegion right,
            in Bounds keyBounds, bool hasKeyBounds, PracticeHand hand, in CloseupFramingTuning tuning,
            float aspect, in Bounds torsoBounds, bool hasTorsoBounds, out CameraTarget target)
        {
            target = default;
            if (!TryCombineCloseupRegion(left, right, keyBounds, hasKeyBounds, hand, tuning, out Bounds region))
                return false;

            Vector3 focus = FocusWithVerticalLimit(region, tuning);
            Vector3 back = DirectionFromAngles(tuning.ViewYaw, tuning.ViewPitch);
            SolveFraming(region, focus, back, tuning.BaseFov, tuning.MaxFov,
                tuning.MinDistance, tuning.MaxDistance, tuning.FrameMargin, aspect, out float distance, out float fov);

            target = CameraTarget.Create(focus, CoverRadius(region, focus), back, distance, fov);

            // 没有躯干信息，或躯干中心本来就在画面外：保持原构图（绝大多数帧走这里，零额外成本）
            if (!hasTorsoBounds) return true;
            if (!IsTorsoCenterInViewport(torsoBounds, target, aspect)) return true;

            float bestCoverage = -1f;
            CameraTarget bestClear = default;
            bool hasClear = false;
            float fallbackCoverage = -1f;
            CameraTarget fallback = target;

            float margin = Mathf.Max(0f, tuning.TorsoMargin);
            float maxDistance = Mathf.Max(tuning.MaxDistance, distance);

            // 候选 A：沿视线后移（步长 0.1 m），只在距离上限内搜索
            for (float d = distance; d <= maxDistance + 1e-3f; d += 0.1f)
            {
                var candidate = CameraTarget.Create(focus, target.Radius, back, d, fov);
                float coverage = candidate.Radius / Mathf.Max(candidate.Distance, 0.01f);
                if (coverage > fallbackCoverage) { fallbackCoverage = coverage; fallback = candidate; }
                if (IsTorsoCenterInViewport(torsoBounds, candidate, aspect, margin)) continue;
                if (coverage > bestCoverage) { bestCoverage = coverage; bestClear = candidate; hasClear = true; }
            }

            // 候选 B：抬高俯角（每次 +5°，上限 ViewPitchMax）
            float pitchMax = Mathf.Max(tuning.ViewPitch, tuning.ViewPitchMax);
            for (float pitch = tuning.ViewPitch + 5f; pitch <= pitchMax + 1e-3f; pitch += 5f)
            {
                Vector3 steeper = DirectionFromAngles(tuning.ViewYaw, pitch);
                SolveFraming(region, focus, steeper, tuning.BaseFov, tuning.MaxFov,
                    tuning.MinDistance, tuning.MaxDistance, tuning.FrameMargin, aspect,
                    out float d, out float f);
                var candidate = CameraTarget.Create(focus, target.Radius, steeper, d, f);
                float coverage = candidate.Radius / Mathf.Max(candidate.Distance, 0.01f);
                if (coverage > fallbackCoverage) { fallbackCoverage = coverage; fallback = candidate; }
                if (IsTorsoCenterInViewport(torsoBounds, candidate, aspect, margin)) continue;
                if (coverage > bestCoverage) { bestCoverage = coverage; bestClear = candidate; hasClear = true; }
            }

            target = hasClear ? bestClear : fallback;
            return true;
        }

        // ==================== 全景（方案 4.3 / 审查 3.1C） ====================

        /// <summary>
        /// 全景取景目标：以「当前激活人物包围盒 ∪ 钢琴包围盒」构图，
        /// 保证 88 键（A0–C8）完整入画且人物头脚不出框；并按身高/头身比做轻量取景校正。
        /// <para>
        /// <paramref name="keyboardUnobstructed"/> 为明确标识的「键盘无遮挡」教学子视角（默认关闭），
        /// 不通过把琴体透明化实现（方案 4.3 明确禁止把透明化作为默认修复）。
        /// </para>
        /// </summary>
        public static CameraTarget ComputeFullTarget(in Bounds subjectBounds, in Bounds keyboardBounds,
            bool keyboardUnobstructed, in FullFramingTuning tuning, float aspect)
        {
            Vector3 back;
            Bounds box;
            Vector3 focus;
            float margin = Mathf.Max(tuning.FrameMargin, 0.01f);

            if (keyboardUnobstructed)
            {
                box = keyboardBounds;
                focus = keyboardBounds.center + new Vector3(
                    tuning.FocusOffset.x, tuning.KeyboardUnobstructedFocusLift, tuning.FocusOffset.z);
                back = DirectionFromAngles(tuning.KeyboardUnobstructedYaw, tuning.KeyboardUnobstructedPitch);
            }
            else
            {
                box = subjectBounds;
                box.Encapsulate(keyboardBounds); // 人物与 88 键必须同时入画
                focus = box.center + tuning.FocusOffset;
                back = DirectionFromAngles(tuning.Yaw, tuning.Pitch);

                // 身高/头身比轻量校正：只调取景余量，不为每个角色写完全不同的规则
                if (tuning.ReferenceHeight > 0.01f && tuning.HeightCompensation > 0f)
                {
                    float ratio = Mathf.Max(box.size.y, 0.01f) / tuning.ReferenceHeight;
                    margin *= Mathf.Lerp(1f, ratio, Mathf.Clamp01(tuning.HeightCompensation));
                }
            }

            float fov = Mathf.Clamp(tuning.FieldOfView, tuning.MinFov, tuning.MaxFov);
            SolveFraming(box, focus, back, fov, fov, tuning.MinDistance, tuning.MaxDistance, margin, aspect,
                out float distance, out _);

            return CameraTarget.Create(focus, CoverRadius(box, focus), back, distance, fov);
        }
    }
}
