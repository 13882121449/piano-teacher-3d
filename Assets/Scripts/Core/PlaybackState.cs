namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 播放状态（对应方案 4.3：Stopped / Playing / Paused）。
    /// 切换角色或视角不应改变此状态。
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }
}
