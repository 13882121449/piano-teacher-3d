using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 角色选择持久化与恢复（写实化批次 1）。
    /// <para>
    /// 覆盖三条恢复路径：
    /// ① <b>完整闭环（内置角色）</b>：激活 → <c>OnCharacterViewChanged</c> 把 ID 写进会话内存偏好 →
    /// 无参 <c>RestoreCharacter</c> 读回并恢复同 ID（Rocketbox 资产补齐后同一链路自动生效于外部角色）；
    /// ② <b>资产不完整（Rocketbox 现状）</b>：恢复失败必须回落内置角色 0 并留 Warning——
    /// 「降级」本身成为新的持久化事实，不产生半成品（8 步事务保证）；
    /// ③ <b>未知 ID</b>：注册表解析失败回落 <c>AvatarIndex</c> 既有下标路径（旧偏好文件兼容）。
    /// </para>
    /// <para>
    /// batchmode 下 <c>ApplyPreferences</c> 按设计跳过（测试必须从确定性起点出发），
    /// 因此恢复语义一律通过公开的 <c>RestoreCharacter</c> 直测，不走启动链路。
    /// </para>
    /// </summary>
    public class CharacterPreferenceRestoreTests
    {
        [UnityTest]
        public IEnumerator RestoreCharacter_BuiltInRoundTrip_RestoresSameId()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(session.CharacterRegistry,
                "会话必须持有角色注册表（写实化批次 1：UI 与恢复共用同一实例）");

            // 激活写实女性角色：走 Prefab 路径加载 Resources 里的 FBX；
            // 成功触发 OnCharacterViewChanged → ID 写进会话内存偏好（与生产持久化同一代码路径）
            var registry = session.CharacterRegistry;
            Assert.IsTrue(registry.TryGet(CharacterRegistry.VroidGenkiId, out CharacterProfile female),
                "写实女性角色必须在注册表中");
            CharacterSwitchResult activate = session.ActivateCharacter(female);
            Assert.IsTrue(activate.Succeeded, "内置角色切换必须成功：" + activate.Error);
            Assert.AreEqual(CharacterRegistry.VroidGenkiId, session.CurrentCharacterView.CharacterId);

            // 无参重载读会话内存偏好（刚被持久化为卡通 ID）→ 恢复回同一角色
            session.RestoreCharacter();

            Assert.AreEqual(CharacterRegistry.VroidGenkiId, session.CurrentCharacterView.CharacterId,
                "恢复后必须回到持久化的角色");
        }

        [UnityTest]
        public IEnumerator RestoreCharacter_TexturedAsset_Succeeds()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(session);

            // 贴图已入库、门禁解除（原注释约定「届时移除 Expect、恢复直接成功」，此处即兑现）。
            // 降级路径（恢复失败 → LogWarning + 回落内置 0）仍保留在 RestoreCharacter 里，
            // 只是当前资产不再触发它。
            session.RestoreCharacter(CharacterRegistry.VroidGenkiId);

            Assert.AreEqual(CharacterRegistry.VroidGenkiId, session.CurrentCharacterView.CharacterId,
                "贴图入库后写实女性应可直接恢复（不再回落内置角色）");
        }

        [UnityTest]
        public IEnumerator RestoreCharacter_UnknownId_FallsBackToAvatarIndexPath()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(session);

            string before = session.CurrentCharacterView != null
                ? session.CurrentCharacterView.CharacterId
                : string.Empty;
            Assert.IsNotEmpty(before, "场景装配完成后应已有当前角色");

            // 未知 ID：注册表 TryGet 失败 → 走 AvatarIndex 下标路径（batchmode 起点 = 内置 0）
            session.RestoreCharacter("no.such.character");

            string after = session.CurrentCharacterView.CharacterId;
            Assert.AreEqual(CharacterRegistry.BuiltInIds[0], after,
                "未知 ID 应回落到 AvatarIndex 指向的内置角色");
            Assert.AreEqual(before, after, "回落不得破坏当前角色的可驱动状态");
        }
    }
}
