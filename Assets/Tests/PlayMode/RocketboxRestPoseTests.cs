using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 真人角色（Rocketbox）静止手型实测。
    /// <para>
    /// 阶段 0 的特写基线 V17 显示：双手空闲时手指几乎伸直、五指张开并悬在键面上方。
    /// 本用例把「静止相位 / PIP 弯曲 / 指尖离键高度」量化并输出日志，
    /// 先确认问题出在姿势控制器（相位或关节轴）还是只在观感参数（收拢/腕高）。
    /// </para>
    /// </summary>
    public class RocketboxRestPoseTests
    {
        [UnityTest]
        public IEnumerator Rocketbox_RestingHand_HasVisibleCurl()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<LessonSessionController>();
            var avatar = Object.FindFirstObjectByType<AvatarManager>();
            var fingerPose = Object.FindFirstObjectByType<FingerPoseController>();
            var keyMap = Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(avatar);
            Assert.IsNotNull(fingerPose);
            Assert.IsNotNull(keyMap);

            CharacterProfile profile = null;
            Assert.IsTrue(session.CharacterRegistry != null &&
                          session.CharacterRegistry.TryGet(
                              CharacterRegistry.RocketboxFemaleId, out profile),
                "角色库缺少 Rocketbox 档案");

            var switchResult = session.ActivateCharacter(profile);
            Assert.IsTrue(switchResult.Succeeded,
                "Rocketbox 激活失败：" + switchResult.Error);

            session.Stop();
            yield return new WaitForSeconds(1.0f);   // 等关节二阶跟随收敛到静止姿态

            var left = avatar.LeftHand as IFingerJointRig;
            Assert.IsNotNull(left, "Rocketbox 左手应实现 IFingerJointRig");

            var sb = new StringBuilder("[RocketboxRestPose]\n");
            int curledFingers = 0;

            // 诊断：手位/朝向与各指关节高度（区分「整手偏高」与「掌心滚转」）
            var wrist = avatar.LeftHand.Wrist;
            var def = avatar.CurrentDefinition;
            var ori = avatar.LeftHand as IHandOrientationRig;
            string basisText = "n/a";
            if (ori != null)
            {
                Vector3 worldBack = wrist.rotation * ori.BackNormalLocal;
                Vector3 worldFwd = wrist.rotation * ori.FingerForwardLocal;
                basisText = string.Format(
                    "localFwd={0} localBack={1} worldFwd={2} worldBack={3}",
                    ori.FingerForwardLocal, ori.BackNormalLocal, worldFwd, worldBack);
            }
            sb.AppendLine(string.Format(
                "wristAboveKeys={0:F1}mm wristPos={1} keySurfaceY={2:F3} hOffset={3:F3} palmOffset={4:F3}",
                (wrist.position.y - keyMap.KeySurfaceY) * 1000f, wrist.position, keyMap.KeySurfaceY,
                def != null ? def.HandHeightOffset : 0f,
                def != null ? def.Hand.PalmKeyPlaneOffset : 0f));
            sb.AppendLine("basis: " + basisText);

            for (int f = 0; f < 5; f++)
            {
                Assert.IsTrue(left.TryGetJoints(f, out var proximal, out var intermediate, out var distal),
                    $"第 {f} 指缺少三关节");

                Vector3 a = intermediate.position - proximal.position;
                Vector3 b = distal.position - intermediate.position;
                float pipBend = Vector3.Angle(a, b);

                Transform tip = left.GetFingertip(f);
                Assert.IsNotNull(tip, $"第 {f} 指缺少指尖");
                float tipAboveKeysMm = (tip.position.y - keyMap.KeySurfaceY) * 1000f;

                FingerPhase phase = fingerPose.GetPhase(leftHand: true, fingerIndex: f);

                sb.AppendLine(string.Format(
                    "L{0}: phase={1} pipBend={2:F1}° tip={3:F1}mm prox={4:F1} mid={5:F1} dist={6:F1}",
                    f, phase, pipBend, tipAboveKeysMm,
                    (proximal.position.y - keyMap.KeySurfaceY) * 1000f,
                    (intermediate.position.y - keyMap.KeySurfaceY) * 1000f,
                    (distal.position.y - keyMap.KeySurfaceY) * 1000f));

                Assert.AreEqual(FingerPhase.Rest, phase,
                    $"空闲时第 {f} 指应处于 Rest 相位，实际 {phase}（有目标才会进入 Approach/Press）");

                if (pipBend > 10f) curledFingers++;
            }

            Debug.Log(sb.ToString());

            Assert.GreaterOrEqual(curledFingers, 3,
                "静止手型至少 3 指应有可见弯曲（PIP > 10°）；实际只有 " + curledFingers +
                " 指。若为 0，说明静止 C 形手没有作用到导入骨架。");
        }
    }
}
