namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 显示模式（产品需求）：两种模式可自由切换，切换后立即生效且不残留状态。
    /// <list type="bullet">
    /// <item><b>展示模式（Showcase）</b>：保持完整的带手部动画效果（默认）。</item>
    /// <item><b>教学模式（Teaching）</b>：隐藏手部动画，只保留「被按下的琴键高亮」，
    /// 并用左手冷蓝 / 右手暖橙区分左右手。</item>
    /// </list>
    /// </summary>
    public enum DisplayMode
    {
        /// <summary>展示模式：完整手部动画 + 用户自选的高亮提示。</summary>
        Showcase = 0,

        /// <summary>教学模式：隐藏手部，高亮固定为「仅被按下的琴键」（左右手分色）。</summary>
        Teaching = 1
    }

    /// <summary>显示模式的文案与行为约定。</summary>
    public static class DisplayModeExtensions
    {
        /// <summary>简短文案（按钮 / 分段选择器用）。</summary>
        public static string ToDisplayText(this DisplayMode mode)
            => mode == DisplayMode.Teaching ? "教学" : "展示";

        /// <summary>完整说明（悬停提示 / 帮助用）。</summary>
        public static string Describe(this DisplayMode mode)
            => mode == DisplayMode.Teaching
                ? "教学模式：隐藏手部动画，仅高亮被按下的琴键（左手冷蓝 · 右手暖橙）"
                : "展示模式：完整手部动画与演奏动作（高亮提示按你的设置）";

        /// <summary>该模式下手部（含手臂）是否可见。</summary>
        public static bool ShowsHands(this DisplayMode mode) => mode != DisplayMode.Teaching;
    }
}
