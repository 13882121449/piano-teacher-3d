namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 练习模式（对应方案 2.2「左右手单独练习」）：
    /// Both = 双手，LeftOnly = 仅左手（&lt;C4），RightOnly = 仅右手（&gt;=C4）。
    /// </summary>
    public enum PracticeHand
    {
        Both,
        LeftOnly,
        RightOnly
    }
}
