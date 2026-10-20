using System;

namespace PianoTeacher3D.Characters
{
    /// <summary>角色美术风格（方案 7.1 / 12.1：卡片上显示风格标签）。</summary>
    public enum CharacterStyle
    {
        /// <summary>现有程序化角色（写实女性/男性）。</summary>
        Realistic = 0,

        /// <summary>现有程序化卡通角色。</summary>
        Cartoon = 1,

        /// <summary>导入的动漫风格角色。</summary>
        Anime = 2,

        /// <summary>用户照片个性化后的自定义外观。</summary>
        Custom = 3
    }

    /// <summary>角色资源来源。决定 <c>CharacterSwitchService</c> 走哪条加载路径。</summary>
    public enum CharacterSource
    {
        /// <summary>由 <c>AvatarManager</c> 用代码构建的内置角色（不依赖预制体）。</summary>
        ProceduralBuiltIn = 0,

        /// <summary>外部导入的预制体（FBX/预制体 + Animator）。</summary>
        Prefab = 1
    }

    /// <summary>
    /// 允许被照片生成结果替换的外观槽位（方案 9.5）。
    /// 只有 <c>CharacterProfile</c> 声明支持、且实现确实能安全替换的槽位才会在 UI 上出现「应用」。
    /// </summary>
    public enum AppearanceSlot
    {
        /// <summary>角色库卡片缩略图（最安全，任何角色都支持）。</summary>
        Thumbnail = 0,

        /// <summary>头部/脸部基础贴图（要求目标模型 UV 兼容）。</summary>
        FaceTexture = 1,

        /// <summary>头部材质变体（要求材质槽明确且可写）。</summary>
        HeadMaterial = 2,

        /// <summary>整体色调（衣服/皮肤的整体染色，用于快速体现个性化）。</summary>
        BodyTint = 3
    }

    /// <summary>校验结果。避免各处返回裸 bool + 字符串，便于测试断言具体原因。</summary>
    public readonly struct CharacterValidation
    {
        /// <summary>是否通过。</summary>
        public readonly bool Ok;

        /// <summary>失败原因（通过时为空字符串）。面向用户的短句，不含堆栈。</summary>
        public readonly string Message;

        private CharacterValidation(bool ok, string message)
        {
            Ok = ok;
            Message = ok ? string.Empty : (message ?? "未知原因");
        }

        /// <summary>通过。</summary>
        public static CharacterValidation Pass => new CharacterValidation(true, null);

        /// <summary>失败并给出原因。</summary>
        public static CharacterValidation Fail(string message) => new CharacterValidation(false, message);

        /// <summary>便于日志输出。</summary>
        public override string ToString() => Ok ? "OK" : Message;

        /// <summary>合并两个校验结果（任一失败即失败，保留首个失败原因）。</summary>
        public CharacterValidation And(in CharacterValidation other) => Ok ? other : this;
    }

    /// <summary>角色切换的状态，供 UI 显示与测试断言（方案 7.3）。</summary>
    public enum CharacterSwitchState
    {
        /// <summary>空闲。</summary>
        Idle = 0,

        /// <summary>正在加载目标角色资源。</summary>
        Loading = 1,

        /// <summary>资源已加载，正在校验骨骼与材质。</summary>
        Validating = 2,

        /// <summary>正在激活新角色并接管手部控制。</summary>
        Activating = 3,

        /// <summary>切换成功，已提交。</summary>
        Committed = 4,

        /// <summary>切换失败，已回滚到原角色。</summary>
        RolledBack = 5,

        /// <summary>被新的切换请求打断（旧请求已取消）。</summary>
        Cancelled = 6
    }

    /// <summary>角色切换结果。</summary>
    public readonly struct CharacterSwitchResult
    {
        /// <summary>最终状态。</summary>
        public readonly CharacterSwitchState State;

        /// <summary>失败原因（成功时为空）。</summary>
        public readonly string Error;

        /// <summary>切换前的角色 ID（用于回滚断言）。</summary>
        public readonly string PreviousCharacterId;

        /// <summary>切换后的角色 ID（失败时等于 PreviousCharacterId）。</summary>
        public readonly string CurrentCharacterId;

        /// <summary>构造。</summary>
        public CharacterSwitchResult(CharacterSwitchState state, string error,
            string previousCharacterId, string currentCharacterId)
        {
            State = state;
            Error = error ?? string.Empty;
            PreviousCharacterId = previousCharacterId ?? string.Empty;
            CurrentCharacterId = currentCharacterId ?? string.Empty;
        }

        /// <summary>是否成功提交。</summary>
        public bool Succeeded => State == CharacterSwitchState.Committed;

        /// <summary>便于日志输出。</summary>
        public override string ToString() =>
            $"{State} prev={PreviousCharacterId} cur={CurrentCharacterId}" +
            (string.IsNullOrEmpty(Error) ? string.Empty : $" err={Error}");
    }

    /// <summary>角色 ID 的合法性规则（方案 7.1：唯一、稳定，不能靠显示名称区分）。</summary>
    public static class CharacterIdRules
    {
        /// <summary>最大长度。</summary>
        public const int MaxLength = 64;

        /// <summary>
        /// 校验角色 ID：小写字母/数字开头，允许小写字母、数字、点、下划线、连字符；长度 1–64。
        /// 不接受空格、中文与大写（避免不同平台的文件名与字典键差异）。
        /// </summary>
        public static CharacterValidation Validate(string id)
        {
            if (string.IsNullOrEmpty(id)) return CharacterValidation.Fail("角色 ID 为空。");
            if (id.Length > MaxLength) return CharacterValidation.Fail($"角色 ID 超过 {MaxLength} 字符。");

            char first = id[0];
            bool firstOk = (first >= 'a' && first <= 'z') || (first >= '0' && first <= '9');
            if (!firstOk) return CharacterValidation.Fail("角色 ID 必须以小写字母或数字开头。");

            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return CharacterValidation.Fail($"角色 ID 含非法字符 '{c}'。");
            }

            return CharacterValidation.Pass;
        }
    }
}
