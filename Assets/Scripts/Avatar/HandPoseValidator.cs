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

        /// <summary>两侧手腕是否发生交叉（左手跑到右手右侧）。左手应为较小 X。</summary>
        public static bool AreHandsCrossed(Vector3 leftWrist, Vector3 rightWrist) => leftWrist.x > rightWrist.x;
    }
}
