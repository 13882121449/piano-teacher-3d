using System.Collections.Generic;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 一次指法分配请求（对应方案 5.3「把 FingerPoseController 升级为可替换的 FingeringProvider」、
    /// 方案 10「可选能力提供器接口」、审查 1.2B「以连续音符序列为输入，而不是单独看一个音高」）。
    /// <para>
    /// 输入刻意包含 <see cref="RecentMelody"/>：指法不是逐音独立决定，而是与前后音构成的手型有关。
    /// 本结构为 <c>readonly struct</c> 且只持有引用，构造时不复制列表（避免 Update 路径分配）。
    /// </para>
    /// </summary>
    public readonly struct FingeringContext
    {
        /// <summary>是否为左手请求（左手音域 &lt; C4 = 60）。</summary>
        public readonly bool LeftHand;

        /// <summary>该手当前按下的音高，升序（由调用方排序，提供器必须只读访问）。</summary>
        public readonly IReadOnlyList<int> Pitches;

        /// <summary>最近的旋律音高序列（升序时间序，可 null）：用于判断相邻音、重复音与拇指穿越。</summary>
        public readonly IReadOnlyList<int> RecentMelody;

        /// <summary>统一时间轴时间（秒），供需要节奏信息的提供器使用。</summary>
        public readonly float Time;

        public FingeringContext(bool leftHand, IReadOnlyList<int> pitches, IReadOnlyList<int> recentMelody, float time)
        {
            LeftHand = leftHand;
            Pitches = pitches;
            RecentMelody = recentMelody;
            Time = time;
        }
    }

    /// <summary>
    /// 一次指法分配结果。<see cref="FingerByPitch"/> 与请求的 <c>Pitches</c> 等长、按下标一一对应。
    /// </summary>
    public sealed class FingeringPlan
    {
        /// <summary>每个音高对应的手指编号；0 = 拇指 … 4 = 小指；-1 = 未分配。</summary>
        public int[] FingerByPitch;

        /// <summary>置信度 0..1（越低越不可信）。</summary>
        public float Confidence;

        /// <summary>true = 估算指法（UI 不应把编号显示为确定/专业正确指法，方案 5.3 / 审查 1.2 验收目标表）。</summary>
        public bool IsEstimated;

        /// <summary>中文说明，供诊断面板与「练习演示」标注使用。</summary>
        public string Reason;

        /// <summary>构造一个「无指法」结果：长度为 <paramref name="count"/> 且全部为 -1，标记为估算。</summary>
        public static FingeringPlan Empty(int count)
        {
            if (count < 0) count = 0;
            var fingers = new int[count];
            for (int i = 0; i < count; i++) fingers[i] = -1;
            return new FingeringPlan
            {
                FingerByPitch = fingers,
                Confidence = 0f,
                IsEstimated = true,
                Reason = "无可用指法输入"
            };
        }
    }

    /// <summary>
    /// 可替换指法提供器（启发式 / 人工编辑 / 模型推断）。
    /// <para>
    /// 设计目的（方案 10 / 取舍 3）：AI 或人工指法属于可选增强，不能成为基本播放的单点故障；
    /// 默认实现为纯规则化的 <see cref="HeuristicFingeringProvider"/>，任何实现失败都应回退到它。
    /// </para>
    /// </summary>
    public interface IFingeringProvider
    {
        /// <summary>提供器名称（用于诊断与 UI 标注）。</summary>
        string Name { get; }

        /// <summary>为一次请求分配指法。实现必须保证：返回值非 null、<c>FingerByPitch.Length == context.Pitches.Count</c>、元素 ∈ [-1, 4]。</summary>
        FingeringPlan Assign(in FingeringContext context);
    }
}
