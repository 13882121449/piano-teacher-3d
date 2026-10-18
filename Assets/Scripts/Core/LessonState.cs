namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 课程会话状态（方案 6.2）：把「有没有曲目 / 正在加载 / 就绪 / 播放 / 暂停 / 出错」
    /// 定义成显式、可验证的状态，避免不同按钮路径产生不同含义。
    /// <para>
    /// 与 <see cref="PlaybackState"/> 的关系：<see cref="PlaybackState"/> 是
    /// <see cref="LessonTimeline"/> 的内部时钟状态；<see cref="LessonState"/> 是会话对外暴露的
    /// 面向用户的整体状态，包含曲目可用性（NoSong / Error）与加载过程（Loading）。
    /// </para>
    /// </summary>
    public enum LessonState
    {
        /// <summary>没有可用曲目：显示空状态页与「导入 MIDI / 播放示例曲」。</summary>
        NoSong = 0,

        /// <summary>正在加载/解析曲目：操作呈加载或禁用状态，可取消。</summary>
        Loading = 1,

        /// <summary>曲目就绪但未播放（停止或首次加载完成）。</summary>
        Ready = 2,

        /// <summary>正在播放。</summary>
        Playing = 3,

        /// <summary>已暂停（保持当前播放位置）。</summary>
        Paused = 4,

        /// <summary>发生错误：保留上一份可用曲目或停留在空状态，等待用户恢复动作。</summary>
        Error = 5
    }

    /// <summary>会话状态相关的小工具。</summary>
    public static class LessonStateExtensions
    {
        /// <summary>该状态下是否已有可播放曲目。</summary>
        public static bool HasPlayableSong(this LessonState state)
            => state == LessonState.Ready || state == LessonState.Playing || state == LessonState.Paused;

        /// <summary>该状态下是否应禁用「播放/重播/循环」等需要曲目的操作。</summary>
        public static bool BlocksSongActions(this LessonState state)
            => state == LessonState.NoSong || state == LessonState.Loading || state == LessonState.Error;

        /// <summary>面向用户的状态文案。</summary>
        public static string ToDisplayText(this LessonState state) => state switch
        {
            LessonState.NoSong => "尚无曲目",
            LessonState.Loading => "正在加载",
            LessonState.Ready => "已就绪",
            LessonState.Playing => "播放中",
            LessonState.Paused => "已暂停",
            LessonState.Error => "出错",
            _ => "未知"
        };
    }
}
