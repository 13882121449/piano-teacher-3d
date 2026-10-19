using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 「左手 / 右手」与「手指 ↔ 琴键」对应关系的永久回归测试。
    /// <para>
    /// 覆盖三类曾经真实出错、且只靠"看代码"发现不了的对应关系：
    /// ① 左右手空间位置（角色面向 -Z ⇒ 左手在世界 +X = 低音侧 = 教学画面左侧）；
    /// ② 左右手颜色标识与所在方位一致（低音/左手 = 冷蓝）；
    /// ③ 弯曲的手指、指尖目标、真正按下的琴键三者严格一一对应，且指尖确实到达键面。
    /// </para>
    /// </summary>
    public class HandFingerCorrespondenceTests
    {
        private LessonSessionController _session;
        private HandIKController _handIK;
        private FingerPoseController _fingerPose;
        private PianoKeyHighlighter _highlighter;
        private AvatarManager _avatar;
        private PianoKeyMap _keyMap;
        private Camera _cam;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _fingerPose = Object.FindFirstObjectByType<FingerPoseController>();
            _highlighter = Object.FindFirstObjectByType<PianoKeyHighlighter>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            _keyMap = Object.FindFirstObjectByType<PianoKeyMap>();
            _cam = Camera.main;

            Assert.IsNotNull(_session); Assert.IsNotNull(_handIK);
            Assert.IsNotNull(_fingerPose); Assert.IsNotNull(_highlighter);
            Assert.IsNotNull(_avatar); Assert.IsNotNull(_keyMap); Assert.IsNotNull(_cam);

            _session.Stop();                 // 停掉调度，后面的断言只受"我们主动喂的音符"影响
            _session.SetDisplayMode(DisplayMode.Showcase);
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Full);
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        // ==================================================================
        // ① 左右手空间位置
        // ==================================================================

        [UnityTest]
        public IEnumerator Hands_LeftHandIsOnLowNoteSide()
        {
            // 角色面向 -Z：其左手应在世界 +X（= 低音键一侧）
            Assert.Greater(_avatar.LeftHand.Wrist.position.x, _avatar.RightHand.Wrist.position.x,
                "左手应位于世界 +X 侧（角色面向 -Z 时的真正左手侧）");

            var skeleton = _avatar.CurrentSkeleton;
            Assert.IsNotNull(skeleton);
            Assert.Greater(skeleton.LeftHand.position.x, skeleton.RightHand.position.x,
                "Humanoid 骨架的 LeftHand 也应位于 +X 侧（命名与实际解剖位置必须一致）");

            yield return null;
        }

        /// <summary>
        /// 解剖学手性回归（优化方案 §7-5 / P0-02 补充第 1、4 条）：
        /// 拇指必须落在<b>掌部内侧</b>（朝演奏者身体中线），小指在外侧；拇指指向也必须朝内。
        /// <para>
        /// 位置正确 ≠ 手性正确：改造前两只手虽然分别落在低音侧/高音侧（位置对），
        /// 但手部网格与拇指链是按相反的镜像生成的 —— 左手看起来像右手（拇指长在小指一侧），
        /// 这正是「左右手反置」的真实根因。该缺陷不会被「哪只手弹哪个键」的测试发现。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Hands_ThumbFacesBodyMidline_OnBothHands()
        {
            var skeleton = _avatar.CurrentSkeleton;
            Assert.IsNotNull(skeleton, "需要 Humanoid 骨架才能校验骨骼层面的手性");

            AssertThumbIsMedial("左手", _avatar.LeftHand, skeleton.LeftFingers[0], skeleton.LeftHand);
            AssertThumbIsMedial("右手", _avatar.RightHand, skeleton.RightFingers[0], skeleton.RightHand);

            yield return null;
        }

        /// <summary>
        /// 校验一只手的解剖学朝向：
        /// ① 拇指根比小指根更靠近身体中线（|x| 更小）；
        /// ② 拇指链的前向带有朝中线的分量（不朝身体外侧张开）。
        /// 可见手部与 Humanoid 骨架都要满足，二者不能各自使用不同的镜像约定。
        /// </summary>
        private void AssertThumbIsMedial(string label, IHandRig hand, Transform skeletonThumb, Transform skeletonWrist)
        {
            Assert.IsNotNull(hand, label + "缺少手部 Rig");
            Assert.IsNotNull(skeletonThumb, label + "骨架缺少拇指骨");
            Assert.IsNotNull(skeletonWrist, label + "骨架缺少腕骨");

            Vector3 wrist = hand.Wrist.position;
            Transform thumb = hand.Fingers[0];
            Transform little = hand.Fingers[4];
            Assert.IsNotNull(thumb);
            Assert.IsNotNull(little);

            // 身体中线在世界 X = 0（角色根节点位于场景原点附近）；「内侧」= 朝这个平面
            float inwardSign = wrist.x >= 0f ? -1f : 1f;
            var inward = new Vector3(inwardSign, 0f, 0f);

            float thumbInward = (thumb.position - wrist).x * inwardSign;
            float littleInward = (little.position - wrist).x * inwardSign;

            Assert.Greater(thumbInward, littleInward, string.Format(
                "{0}：拇指根应比小指根更靠近身体中线（拇指 {1:+0.000;-0.000} m，小指 {2:+0.000;-0.000} m）" +
                " ⇒ 手部网格镜像方向反了，看起来会像另一只手", label, thumbInward, littleInward));

            // 拇指链（Fingers[0] 的第一个子节点 = 解剖朝向节点）的前向必须带朝中线的分量
            Transform chain = thumb.childCount > 0 ? thumb.GetChild(0) : thumb;
            float directionDot = Vector3.Dot(chain.forward, inward);
            Assert.Greater(directionDot, 0.3f, string.Format(
                "{0}：拇指指向应朝身体中线（与内侧轴的点积 {1:F2}，正常约 0.5–0.9）" +
                " ⇒ 拇指朝身体外侧张开，是左右手手性反置的直接证据", label, directionDot));

            // 骨架侧：拇指根同样应比小指根更靠近中线
            var skeletonLittle = skeletonThumb.parent != null
                ? skeletonThumb.parent.Find(skeletonThumb.name.Replace("ThumbProximal", "LittleProximal"))
                : null;
            if (skeletonLittle != null)
            {
                float skelThumbInward = (skeletonThumb.position - skeletonWrist.position).x * inwardSign;
                float skelLittleInward = (skeletonLittle.position - skeletonWrist.position).x * inwardSign;
                Assert.Greater(skelThumbInward, skelLittleInward, string.Format(
                    "{0}骨架：拇指近节应比小指近节更靠近身体中线（{1:+0.000;-0.000} vs {2:+0.000;-0.000}）",
                    label, skelThumbInward, skelLittleInward));
            }

            // 骨架拇指链的朝向：近节 → 中节 的方向同样必须朝身体中线（否则拇指会折向掌心）
            if (skeletonThumb.childCount > 0)
            {
                Vector3 skelThumbDirection = skeletonThumb.GetChild(0).position - skeletonThumb.position;
                if (skelThumbDirection.sqrMagnitude > 1e-8f)
                {
                    float skelDot = Vector3.Dot(skelThumbDirection.normalized, inward);
                    Assert.Greater(skelDot, 0.3f, string.Format(
                        "{0}骨架：拇指链方向应朝身体中线（与内侧轴的点积 {1:F2}）⇒ 拇指链偏航符号与手性不一致",
                        label, skelDot));
                }
            }
        }

        [UnityTest]
        public IEnumerator Hands_LowNotesArePlayedByLeftHand()
        {
            yield return PressAndWait(45);   // 低音
            float leftAfterLow = _avatar.LeftHand.Wrist.position.x;
            Assert.AreEqual(KeyX(45), leftAfterLow, 0.06f,
                "低音应由左手演奏：左手腕应移向该键，而不是右手");

            _handIK.ReleaseAll(); _fingerPose.ReleaseAll(); _highlighter.ClearAll();
            yield return null;

            yield return PressAndWait(76);   // 高音
            Assert.AreEqual(KeyX(76), _avatar.RightHand.Wrist.position.x, 0.08f,
                "高音应由右手演奏：右手腕应移向该键，而不是左手");

            _handIK.ReleaseAll(); _fingerPose.ReleaseAll();
        }

        [UnityTest]
        public IEnumerator Screen_LowNotesAppearOnTheLeft()
        {
            foreach (CameraMode mode in new[] { CameraMode.Closeup, CameraMode.Full })
            {
                _session.SetCameraMode(mode);
                yield return new WaitForSeconds(0.5f);

                float a0 = ViewportX(PianoKeyMap.MinPitch);
                float c8 = ViewportX(PianoKeyMap.MaxPitch);

                Assert.Less(a0, c8, string.Format(
                    "{0}：低音 A0 必须出现在画面左侧、高音 C8 在右侧（钢琴/乐谱通用方向）；" +
                    "实测 A0 viewportX={1:F3}、C8 viewportX={2:F3}", mode, a0, c8));
            }
        }

        // ==================================================================
        // ② 颜色标识与方位一致
        // ==================================================================

        [UnityTest]
        public IEnumerator Highlight_LeftHandColorOnLowKeys_RightHandColorOnHighKeys()
        {
            _session.SetHighlightMode(HighlightMode.Active);
            Assert.AreEqual(HighlightMode.Active, _session.KeyHighlight, "前置条件：高亮应为「仅发声」");

            // 颜色必须在"按下音符的同一帧内"读取：会话处于 Stopped 状态时会每帧清空琴键高亮
            // （Stop 语义为完全复位），而真实运行时的着色时机也正是 Note On 当帧。
            var low = new MidiNoteEvent(45, 0f, 2f, 90, 0, 0);
            _highlighter.OnNoteOn(low);
            Assert.AreEqual(PianoKeyHighlighter.LeftHandColor, KeyColor(45),
                "低音键（左手侧）必须用左手色（冷蓝）高亮");
            Assert.Greater(KeyX(45), 0f, "低音键应位于世界 +X 侧（与左手同侧）");
            _highlighter.OnNoteOff(low);

            var high = new MidiNoteEvent(76, 0f, 2f, 90, 0, 0);
            _highlighter.OnNoteOn(high);
            Assert.AreEqual(PianoKeyHighlighter.RightHandColor, KeyColor(76),
                "高音键（右手侧）必须用右手色（暖橙）高亮");
            Assert.Less(KeyX(76), 0f, "高音键应位于世界 -X 侧（与右手同侧）");
            _highlighter.OnNoteOff(high);

            yield return null;
            _session.SetHighlightMode(HighlightMode.Off);
        }

        // ==================================================================
        // ③ 手指 ↔ 琴键 一一对应
        // ==================================================================

        [UnityTest]
        public IEnumerator Fingers_EachPressedKeyHasExactlyOneFingerPointingAtIt()
        {
            _session.SetPracticeHand(PracticeHand.Both);
            int[] chord = { 48, 52, 55, 64 };

            for (int i = 0; i < chord.Length; i++)
            {
                var n = new MidiNoteEvent(chord[i], 0f, 2f, 90, 0, 0);
                _fingerPose.OnNoteOn(n);
                _handIK.OnNoteOn(n);
            }

            for (int f = 0; f < 60; f++) yield return null;   // 让手掌反馈收敛

            for (int i = 0; i < chord.Length; i++)
            {
                int pitch = chord[i];
                bool left = PianoNoteFilter.IsLeftHand(pitch);

                int finger = _fingerPose.GetFingerIndexForPitch(left, pitch);
                Assert.GreaterOrEqual(finger, 0, $"音高 {pitch} 没有被分配到任何手指");

                // 指法控制器的分配表与共用分配必须自洽
                Assert.AreEqual(pitch, _fingerPose.GetFingerPitch(left, finger),
                    $"手指 {finger} 的分配表与音高 {pitch} 不一致（左右手隔离或复用出错）");

                // 指尖目标必须就是该音的键面按压点
                Assert.IsTrue(_handIK.TryGetKeyPressPoint(pitch, out Vector3 pressPoint),
                    $"找不到音高 {pitch} 的键面按压点");
                Assert.IsTrue(_handIK.TryGetFingertipTarget(left, finger, out Vector3 target),
                    $"手指 {finger} 没有指尖目标（弯的手指与伸的手指不同步）");
                Assert.AreEqual(pressPoint, target,
                    $"手指 {finger} 的指尖目标不是音高 {pitch} 的键面按压点（手指与琴键错配）");

                // 该音不应被第二根手指同时认领
                for (int other = 0; other < 5; other++)
                {
                    if (other == finger) continue;
                    Assert.AreNotEqual(pitch, _fingerPose.GetFingerPitch(left, other),
                        $"音高 {pitch} 被多根手指重复认领（{finger} 与 {other}）");
                }
            }

            _handIK.ReleaseAll(); _fingerPose.ReleaseAll();
        }

        [UnityTest]
        public IEnumerator Fingers_NearestFingertipIsTheAssignedFinger()
        {
            int[] chord = { 48, 52, 55 };

            for (int i = 0; i < chord.Length; i++)
            {
                var n = new MidiNoteEvent(chord[i], 0f, 2f, 90, 0, 0);
                _fingerPose.OnNoteOn(n);
                _handIK.OnNoteOn(n);
            }
            for (int f = 0; f < 60; f++) yield return null;

            var tipRig = _avatar.LeftHand as IFingertipRig;
            Assert.IsNotNull(tipRig, "左手应提供指尖节点（IFingertipRig）");

            for (int i = 0; i < chord.Length; i++)
            {
                int pitch = chord[i];
                _handIK.TryGetKeyPressPoint(pitch, out Vector3 press);
                int assigned = _fingerPose.GetFingerIndexForPitch(true, pitch);

                int nearest = -1;
                float nearestDist = float.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    Transform tip = tipRig.GetFingertip(f);
                    if (tip == null) continue;
                    float d = Vector3.Distance(tip.position, press);
                    if (d < nearestDist) { nearestDist = d; nearest = f; }
                }

                Assert.AreEqual(assigned, nearest, string.Format(
                    "音高 {0}：离键面最近的手指是 {1}，但被分配的手指是 {2} ⇒ 视觉与指法错配",
                    pitch, nearest, assigned));

                // 收敛后的"手指确实落在键上"：静态和弦下每根被分配手指的指尖误差应 < 40mm
                Transform assignedTip = tipRig.GetFingertip(assigned);
                Assert.IsNotNull(assignedTip, "被分配的手指应有指尖节点：" + pitch);
                float tipError = Vector3.Distance(assignedTip.position, press);
                Assert.Less(tipError, 0.04f, string.Format(
                    "音高 {0}（手指 {1}）收敛后指尖仍离键面 {2:F1}mm，手指没有真正落在键上",
                    pitch, assigned, tipError * 1000f));
            }

            _handIK.ReleaseAll(); _fingerPose.ReleaseAll();
        }

        [UnityTest]
        public IEnumerator Fingers_ActuallyReachTheKeys_WhilePlaying()
        {
            if (!_session.HasSong) Assert.Ignore("示例曲目未加载，跳过指尖贴键统计");

            _session.SetHighlightMode(HighlightMode.Off);
            _session.Seek(0f);
            _session.Play();
            yield return new WaitForSeconds(3f);
            _session.Pause();

            var report = _session.HandMotion;
            Assert.IsNotNull(report);
            Assert.Greater(report.FingertipSamples, 60, "指尖误差应有足够样本，实际 " + report.FingertipSamples);
            Debug.Log("[FINGER] " + report.ToLogString());

            // 口径：指尖（每指末端节点）到「该指被分配的那个键的键面按压点」的距离。
            // 阈值 0.04 m 略高于 FingertipErrorTolerance（0.035 m），用于捕获"手指大范围够不到键"的回归。
            // 口径：指尖（每指末端节点）到「该指被分配的那个键的键面按压点」的距离，逐帧取样后取平均。
            // 关键区分：这是**播放期间**的平均值，包含换位途中的过渡帧，必然大于静态收敛值；
            // 静态收敛后的严格门槛见 Fingers_NearestFingertipIsTheAssignedFinger（< 40mm）。
            // 修复前该均值为 ~99mm（手指整体够不到键）；修复后本机约 38–41mm。
            // 阈值取 50mm：既能捕获"手指够不到键"的整体回归，也不会被换位过渡帧误报。
            Assert.Less(report.FingertipMeanError, 0.05f, string.Format(
                "播放期间指尖到琴键的平均误差应小于 50mm（手指应贴在键上而非悬在半空），实际 {0:F1}mm",
                report.FingertipMeanError * 1000f));
        }

        // ==================== 辅助 ====================

        private IEnumerator PressAndWait(int pitch)
        {
            _handIK.ReleaseAll(); _fingerPose.ReleaseAll();
            yield return null;

            // 与真实接线保持一致：琴键动画 / 音频 / 手部 / 指法 / 高亮都由同一个 Note On 驱动。
            // 这里直接调用各订阅者的处理函数（会话处于 Stop 状态，调度器不派发事件）。
            var note = new MidiNoteEvent(pitch, 0f, 2f, 90, 0, 0);
            _handIK.OnNoteOn(note);
            _fingerPose.OnNoteOn(note);
            _highlighter.OnNoteOn(note);

            for (int f = 0; f < 30; f++) yield return null;
        }

        private float KeyX(int pitch)
        {
            Assert.IsTrue(_keyMap.TryGetKey(pitch, out Transform key) && key != null,
                "找不到键根：" + pitch);
            return key.position.x;
        }

        private float ViewportX(int pitch) => _cam.WorldToViewportPoint(KeyFor(pitch).position).x;

        private Transform KeyFor(int pitch)
        {
            Assert.IsTrue(_keyMap.TryGetKey(pitch, out Transform key) && key != null,
                "找不到键根：" + pitch);
            return key;
        }

        /// <summary>读取琴键当前实际渲染色（来自 MaterialPropertyBlock 的 _Color；未着色时为全零）。</summary>
        private static Color KeyColor(int pitch)
        {
            var map = Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsTrue(map.TryGetKey(pitch, out Transform key) && key != null, "找不到键根：" + pitch);

            var renderer = key.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(renderer, "琴键缺少渲染器：" + pitch);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_Color"));
        }
    }
}
