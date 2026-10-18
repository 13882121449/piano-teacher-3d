using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 手腕姿态的允许范围（世界坐标，单位米）。
    /// 由 <see cref="HandIKController"/> 依据钢琴实际几何填充。
    /// </summary>
    public struct HandPoseLimits
    {
        /// <summary>允许的手腕最低高度（键面之上，防穿模）。</summary>
        public float MinY;

        /// <summary>允许的手腕最高高度。</summary>
        public float MaxY;

        /// <summary>键盘左端（世界 X）。</summary>
        public float MinX;

        /// <summary>键盘右端（世界 X）。</summary>
        public float MaxX;

        /// <summary>允许的最小深度（防穿入琴体）。</summary>
        public float MinZ;

        /// <summary>允许的最大深度（防手落到键前方）。</summary>
        public float MaxZ;

        public HandPoseLimits(float minY, float maxY, float minX, float maxX, float minZ, float maxZ)
        {
            MinY = minY;
            MaxY = maxY;
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }
    }

    /// <summary>
    /// 手部异常姿态检查（对应方案阶段五验收「异常姿态检查，避免动作失控或穿模」）。
    /// <para>
    /// 纯静态、不依赖场景与 Transform，输入世界坐标 + 允许范围，便于单元测试；
    /// 运行时由 <see cref="HandIKController"/> 用于自检与位置钳制（方案 8.2「手部是否穿过钢琴模型」）。
    /// </para>
    /// </summary>
    public static class HandPoseValidator
    {
        /// <summary>手腕在键盘左右两端之外的额外容差（米）。</summary>
        public const float LateralMargin = 0.06f;

        /// <summary>
        /// 所有边界比较的浮点容差（米）。
        /// 必须存在：<see cref="Clamp"/> 会把坐标压到边界值上，若校验用严格比较，
        /// 浮点舍入会让「刚被钳制过的位置」再次被判为越界（钳制与校验必须自洽）。
        /// </summary>
        public const float Tolerance = 0.002f;

        /// <summary>
        /// 校验手腕世界坐标是否落在允许范围内。
        /// 返回 null 表示正常；否则返回人类可读的问题描述。
        /// </summary>
        public static string Validate(Vector3 wrist, in HandPoseLimits limits)
        {
            if (wrist.y < limits.MinY - Tolerance)
                return $"手腕低于允许高度（y={wrist.y:F4} < {limits.MinY:F4}，存在穿入琴键风险）";

            if (wrist.y > limits.MaxY + Tolerance)
                return $"手腕高于允许高度（y={wrist.y:F4} > {limits.MaxY:F4}）";

            if (wrist.x < limits.MinX - LateralMargin - Tolerance)
                return $"手腕超出键盘左侧（x={wrist.x:F4} < {limits.MinX - LateralMargin:F4}）";

            if (wrist.x > limits.MaxX + LateralMargin + Tolerance)
                return $"手腕超出键盘右侧（x={wrist.x:F4} > {limits.MaxX + LateralMargin:F4}）";

            if (wrist.z < limits.MinZ - Tolerance)
                return $"手腕穿入琴体（z={wrist.z:F4} < {limits.MinZ:F4}）";

            if (wrist.z > limits.MaxZ + Tolerance)
                return $"手腕过于靠外（z={wrist.z:F4} > {limits.MaxZ:F4}）";

            return null;
        }

        /// <summary>把姿态钳制回允许范围（运行时防穿模）。</summary>
        public static Vector3 Clamp(Vector3 wrist, in HandPoseLimits limits)
        {
            return new Vector3(
                Mathf.Clamp(wrist.x, limits.MinX - LateralMargin, limits.MaxX + LateralMargin),
                Mathf.Clamp(wrist.y, limits.MinY, limits.MaxY),
                Mathf.Clamp(wrist.z, limits.MinZ, limits.MaxZ));
        }

        /// <summary>
        /// 两侧手腕是否发生交叉。
        /// <para>
        /// 【左右约定】角色面向 -Z，其左手在世界 <b>+X</b>（低音侧，也是教学画面左侧），
        /// 因此「交叉」= 左手的 X 反而小于右手。改造前该判定方向相反，会把正常手位误报为交叉。
        /// </para>
        /// </summary>
        public static bool AreHandsCrossed(Vector3 leftWrist, Vector3 rightWrist) => leftWrist.x < rightWrist.x;

        // ==================== 指尖目标误差与手指姿态异常（审查 1.2C / 第 4 章 HandPoseValidator 行） ====================
        // 全部为纯静态、不依赖场景与 MonoBehaviour，可单测；不修改任何既有签名。

        /// <summary>指尖到目标键面按压点的误差（米）。</summary>
        public static float FingertipError(Vector3 fingertip, Vector3 target) => Vector3.Distance(fingertip, target);

        /// <summary>
        /// 校验指尖是否落在目标键面容差内。返回 null 表示正常；否则返回人类可读的问题描述。
        /// </summary>
        public static string ValidateFingertip(Vector3 fingertip, Vector3 target, float tolerance)
        {
            float error = Vector3.Distance(fingertip, target);
            if (error > tolerance + Tolerance)
                return $"指尖偏离目标键面（误差 {error * 1000f:0.#} mm > 容差 {tolerance * 1000f:0.#} mm）";
            return null;
        }

        /// <summary>是否「离键悬空」：指尖高于目标键面超过允许高度（按键时不应出现）。</summary>
        public static bool IsHovering(Vector3 fingertip, Vector3 target, float hoverHeight)
            => fingertip.y - target.y > hoverHeight + Tolerance;

        /// <summary>
        /// 手指姿态异常检查：指根 → 指尖的实际方向与期望方向的夹角是否超出允许范围。
        /// 返回 null 表示正常；否则返回人类可读的问题描述（与 <see cref="Validate"/> 的返回约定一致）。
        /// </summary>
        public static string ValidateFingerPose(Vector3 fingerRoot, Vector3 fingerTip,
            Vector3 expectedDirection, float maxAngleDegrees)
        {
            var actual = fingerTip - fingerRoot;
            if (actual.sqrMagnitude < 1e-8f)
                return "手指姿态异常：指节长度退化（指根与指尖重合）";

            if (expectedDirection.sqrMagnitude < 1e-8f) return null;

            float angle = Vector3.Angle(actual, expectedDirection);
            if (angle > maxAngleDegrees)
                return $"手指姿态异常：指节偏离期望方向 {angle:0.#}°（上限 {maxAngleDegrees:0.#}°）";
            return null;
        }

        /// <summary>拇指轴与掌心前向的夹角（度）。任一方向退化时返回 0（视为无信息而不是异常）。</summary>
        public static float ThumbPalmAngle(Vector3 thumbDirection, Vector3 palmForward)
        {
            if (thumbDirection.sqrMagnitude < 1e-8f || palmForward.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(thumbDirection, palmForward);
        }

        /// <summary>拇指方向是否异常（夹角超过阈值）。</summary>
        public static bool IsThumbOrientationAbnormal(Vector3 thumbDirection, Vector3 palmForward, float maxAngleDegrees)
            => ThumbPalmAngle(thumbDirection, palmForward) > maxAngleDegrees;

        /// <summary>
        /// 统计一组手指的姿态异常数量（每根手指调用 <see cref="ValidateFingerPose"/>）。
        /// 数组长度不一致时以较短者为准，返回异常根数（便于批量单测与视觉抽查标记）。
        /// </summary>
        public static int CountFingerPoseIssues(Vector3[] fingerRoots, Vector3[] fingerTips,
            Vector3 expectedDirection, float maxAngleDegrees)
        {
            if (fingerRoots == null || fingerTips == null) return 0;
            int count = Mathf.Min(fingerRoots.Length, fingerTips.Length);
            int issues = 0;
            for (int i = 0; i < count; i++)
            {
                if (ValidateFingerPose(fingerRoots[i], fingerTips[i], expectedDirection, maxAngleDegrees) != null)
                    issues++;
            }
            return issues;
        }
    }
}
