using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 规则化启发式指法提供器（方案 5.3「第一阶段按音域、左右手、相邻音、最大跨度和拇指穿越规则生成启发式候选」
    /// / 审查 1.2B「以连续音符序列为输入，而不是单独看一个音高」）。
    /// <para>
    /// <b>它不是什么</b>：不承诺专业演奏指法，不引入任何 AI 或数据模型。它把「音高比例直接映射手指」升级为
    /// 序列感知的规则分配，并在无法可靠判断时输出低置信度 + <see cref="FingeringPlan.IsEstimated"/> = true，
    /// 让 UI 标注「估算指法」而不是冒充正确指法（方案 2.3 教学诚实性）。
    /// </para>
    /// <para>
    /// <b>规则集</b>（命中后写入中文 <see cref="FingeringPlan.Reason"/>，供诊断面板显示）：
    /// ① 音域就近：以中间手指为锚点、按半音跨度换算手指；
    /// ② 相邻音：旋律相邻音落在相邻/同一手指（由单调映射保证）；
    /// ③ 跨度大：远离锚点时向外展开手指；
    /// ④ 黑键可达：黑键避免短指（拇指/食指），改用较长手指；
    /// ⑤ 拇指穿越：最近旋律跨度达到一个八度以上时用拇指换位；
    /// ⑥ 重复音：与上一旋律音相同则沿用同一手指；
    /// ⑦ 和弦：使用一组固定模板（thumb-middle-little 等）保证手型一致；
    /// ⑧ 左右手分界：含对手侧音高时降置信度并标注。
    /// </para>
    /// <para>
    /// <b>无状态</b>：同一个 <see cref="FingeringContext"/> 必然得到同一个结果（便于单测与复现），
    /// 也不持有任何历史缓存，因此不会在播放/拖动进度后产生状态残留。
    /// </para>
    /// </summary>
    public sealed class HeuristicFingeringProvider : IFingeringProvider
    {
        /// <summary>提供器名称（用于诊断与 UI 标注）。</summary>
        public string Name => "启发式指法（规则化序列分配）";

        /// <summary>左右手分界音高，与 <see cref="PianoNoteFilter.SplitPitch"/> 保持一致（中央 C4 = 60）。</summary>
        public int SplitPitch = PianoNoteFilter.SplitPitch;

        /// <summary>右手中间手指（中指）锚点音高。</summary>
        public int RightAnchorPitch = 60;

        /// <summary>左手中间手指锚点音高（低八度 C3）。</summary>
        public int LeftAnchorPitch = 48;

        /// <summary>每根手指覆盖的半音跨度：越小手指变化越频繁，越大越倾向长时间停留同一手指。</summary>
        public float SemitonesPerFinger = 3f;

        /// <summary>低置信度阈值：低于该值时标记 <see cref="FingeringPlan.IsEstimated"/>。</summary>
        public float LowConfidenceThreshold = 0.55f;

        /// <summary>触发拇指穿越所需的最近旋律音域跨度（半音，含当前音）。</summary>
        public int ThumbCrossingSpan = 12;

        /// <summary>判定「跨度较大需要展开」的锚点偏移（半音）。</summary>
        public int WideStretchSemitones = 6;

        /// <summary>1 指 … 5 指（拇指 … 小指）：供 Reason 与 UI 显示。</summary>
        private static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        // 和弦模板（n ≤ 5）：保证同一组音高得到同一组手指（手型一致，不会逐音独立映射）
        private static readonly int[] Triad = { 0, 2, 4 };
        private static readonly int[] TetradNarrow = { 0, 1, 2, 4 };
        private static readonly int[] TetradWide = { 0, 1, 3, 4 };
        private static readonly int[] Pentad = { 0, 1, 2, 3, 4 };
        private static readonly int[] DyadClose = { 0, 2 };
        private static readonly int[] DyadMid = { 0, 3 };
        private static readonly int[] DyadWide = { 0, 4 };

        // ==================== 公开 API ====================

        /// <summary>该音高是否落在给定手的「对手侧」（用于左右手争抢告警）。</summary>
        public bool IsWrongSide(bool leftHand, int pitch) =>
            leftHand ? pitch >= SplitPitch : pitch < SplitPitch;

        /// <summary>
        /// 分配指法。保证：返回值非 null、<c>FingerByPitch.Length == context.Pitches.Count</c>、元素 ∈ [-1,4]。
        /// </summary>
        public FingeringPlan Assign(in FingeringContext context)
        {
            var pitches = context.Pitches;
            int count = pitches?.Count ?? 0;

            if (count == 0) return FingeringPlan.Empty(0);
            if (count == 1) return AssignSingle(in context, pitches[0]);
            return AssignChord(in context, pitches);
        }

        // ==================== 单音 ====================

        private FingeringPlan AssignSingle(in FingeringContext ctx, int pitch)
        {
            int anchor = ctx.LeftHand ? LeftAnchorPitch : RightAnchorPitch;
            var fingers = new int[1];
            float confidence = 0.92f;
            string reason;

            // ① 最近的旋律统计（重复音 / 拇指穿越都以最近序列为依据）
            bool hasRecent = ctx.RecentMelody != null && ctx.RecentMelody.Count > 0;
            int recentMin = pitch;
            int recentMax = pitch;
            bool repeated = false;
            if (hasRecent)
            {
                var recent = ctx.RecentMelody;
                for (int i = 0; i < recent.Count; i++)
                {
                    int p = recent[i];
                    if (p < recentMin) recentMin = p;
                    if (p > recentMax) recentMax = p;
                }
                repeated = recent[recent.Count - 1] == pitch;
            }

            bool crossing = hasRecent && (recentMax - recentMin) >= ThumbCrossingSpan;

            if (crossing && !repeated)
            {
                // ⑤ 拇指穿越：整体音域超过一个八度，说明发生了换位，用拇指越位最稳定
                fingers[0] = 0;
                confidence = 0.62f;
                reason = $"拇指穿越：最近旋律跨度 {recentMax - recentMin} 半音（≥{ThumbCrossingSpan}），使用拇指换位";
            }
            else
            {
                // ① 音域就近（单调映射：锚点 → 中间手指；离锚点越远手指越向外）
                float raw = 2f + (pitch - anchor) / SemitonesPerFinger;
                int rounded = RoundHalfUp(raw);
                int ideal = Mathf.Clamp(rounded, 0, 4);
                bool clamped = ideal != rounded;

                fingers[0] = ideal;

                if (repeated)
                {
                    // ⑥ 重复音：同音同指，避免同音跳指造成的抖动
                    confidence = 0.95f;
                    reason = $"重复音：{NoteName(pitch)} 沿用同一手指（第 {ideal + 1} 指）";
                }
                else if (clamped)
                {
                    // 超出单手舒适音域：顺延到极限手指并降低置信度（教学诚实性）
                    confidence = 0.45f;
                    reason = $"音域超出单手舒适范围：{NoteName(pitch)} 顺延到第 {ideal + 1} 指（估算）";
                }
                else if (Mathf.Abs(pitch - anchor) >= WideStretchSemitones)
                {
                    // ③ 跨度大：向外展开手指
                    confidence = 0.85f;
                    reason = $"跨度较大：{NoteName(pitch)} 距锚点 {Mathf.Abs(pitch - anchor)} 半音，向外展开到第 {ideal + 1} 指";
                }
                else
                {
                    reason = $"音高就近：按音域映射到第 {ideal + 1} 指";
                }

                // ④ 黑键可达：黑键避免短指（拇指 1 / 食指 2），改用较长手指，命中后覆盖 Reason
                bool black = PianoNoteFilter.IsBlackKey(pitch);
                if (black && fingers[0] <= 1)
                {
                    fingers[0] = 2;
                    confidence = Mathf.Min(confidence, 0.70f);
                    reason = $"黑键可达：{NoteName(pitch)} 使用较长的第 3 指（短指易误触黑键）";
                }
            }

            // ⑧ 左右手分界校验：落在对手侧时明确标注（不改变结果，仅降置信度）
            if (IsWrongSide(ctx.LeftHand, pitch))
            {
                confidence = Mathf.Min(confidence, 0.50f);
                reason += "；左右手分界检查：该音高位于对手侧（估算）";
            }

            return Finish(fingers, confidence, reason);
        }

        // ==================== 和弦 ====================

        private FingeringPlan AssignChord(in FingeringContext ctx, System.Collections.Generic.IReadOnlyList<int> pitches)
        {
            int n = pitches.Count;
            var fingers = new int[n];
            int span = pitches[n - 1] - pitches[0];

            int[] template = TemplateFor(n, span);
            for (int i = 0; i < n; i++) fingers[i] = template[i];

            float confidence = 0.90f;

            // ⑦ 和弦可按性：短指（拇指/小指）落在黑键上会降低可按性（只降置信度，不改手型，保证手型一致）
            int shortOnBlack = 0;
            for (int i = 0; i < n; i++)
            {
                if (!PianoNoteFilter.IsBlackKey(pitches[i])) continue;
                if (fingers[i] <= 0 || fingers[i] >= 4) shortOnBlack++;
            }
            if (shortOnBlack > 0) confidence -= 0.12f * shortOnBlack;

            // ⑧ 左右手分界：和弦含对手侧音高时属于「合理争抢」以外的异常输入
            int wrongSide = 0;
            for (int i = 0; i < n; i++)
            {
                if (IsWrongSide(ctx.LeftHand, pitches[i])) wrongSide++;
            }
            if (wrongSide > 0) confidence -= 0.20f;

            if (n > 5) confidence -= 0.25f; // 超出手指数：必须复用手指，属于估算

            string reason = $"和弦手型：{NoteNamesOf(pitches)} 用 {FingerText(fingers)} 指";
            if (n > 5) reason += "（音数超过手指数，复用手指，估算）";
            if (wrongSide > 0) reason += "；左右手分界检查：含对手侧音高（估算）";

            return Finish(fingers, confidence, reason);
        }

        /// <summary>和弦模板：以音数与跨度分档，保证同一组音高得到一致手型。</summary>
        private static int[] TemplateFor(int n, int span)
        {
            switch (n)
            {
                case 2:
                    if (span <= 4) return DyadClose;   // 三度内：拇指 + 中指
                    if (span <= 6) return DyadMid;     // 三全音附近：拇指 + 无名指
                    return DyadWide;                   // 五度以上：拇指 + 小指
                case 3:
                    return Triad;                      // 经典 1-3-5
                case 4:
                    return span <= 8 ? TetradNarrow : TetradWide;
                case 5:
                    return Pentad;
                default:
                    return EvenTemplate(n);            // 音数超过手指数：等比复用（单调不减，不越界）
            }
        }

        /// <summary>音数超过 5 时：把音高均匀映射到 5 根手指（单调不减，全部 ∈ [0,4]）。</summary>
        private static int[] EvenTemplate(int n)
        {
            var fingers = new int[n];
            float step = 4f / Mathf.Max(1, n - 1);
            for (int i = 0; i < n; i++)
            {
                int f = RoundHalfUp(i * step);
                if (f < 0) f = 0;
                if (f > 4) f = 4;
                fingers[i] = f;
            }
            return fingers;
        }

        // ==================== 输出整理 ====================

        private FingeringPlan Finish(int[] fingers, float confidence, string reason)
        {
            confidence = Mathf.Clamp01(confidence);
            return new FingeringPlan
            {
                FingerByPitch = fingers,
                Confidence = confidence,
                IsEstimated = confidence < LowConfidenceThreshold,
                Reason = reason ?? string.Empty
            };
        }

        /// <summary>四舍五入（0.5 向上），避免 <c>Mathf.RoundToInt</c> 的银行家舍入让相邻音出现反向跳动。</summary>
        private static int RoundHalfUp(float value) => Mathf.FloorToInt(value + 0.5f);

        private static string NoteName(int pitch)
        {
            int pc = ((pitch % 12) + 12) % 12;
            return NoteNames[pc] + (pitch / 12 - 1);
        }

        private static string NoteNamesOf(System.Collections.Generic.IReadOnlyList<int> pitches)
        {
            // 仅诊断文本，允许字符串拼接（不在 Update 每帧路径上会调用：由 FingerPoseController 按「按键变化」触发）
            var sb = new System.Text.StringBuilder(24);
            for (int i = 0; i < pitches.Count; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(NoteName(pitches[i]));
            }
            return sb.ToString();
        }

        private static string FingerText(int[] fingers)
        {
            var sb = new System.Text.StringBuilder(fingers.Length * 2);
            for (int i = 0; i < fingers.Length; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(fingers[i] + 1);
            }
            return sb.ToString();
        }
    }
}
