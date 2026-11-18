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

        private sealed class SingleThumbFingering : IFingeringProvider
        {
            public string Name => "拇指回归测试指法";
            public FingeringPlan Assign(in FingeringContext context) => new FingeringPlan
            {
                FingerByPitch = new[] { 0 },
                Confidence = 1f
            };
        }

        [UnityTest]
        public IEnumerator RightThumb_ReleasesToLightlyCurledPose()
        {
            _session.RestoreCharacter(Characters.CharacterRegistry.VroidGenkiId);
            yield return null;
            var rig = (IFingerJointRig)_avatar.RightHand;
            Assert.IsTrue(rig.TryGetJoints(0, out _, out var middle, out var tip));
            _fingerPose.Provider = new SingleThumbFingering();
            var note = new MidiNoteEvent(60, 0f, 1f, 85, 0, 0) { Hand = NoteHand.Right };
            _handIK.OnNoteOn(note);
            _fingerPose.OnNoteOn(note);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(60, _fingerPose.GetFingerPitch(false, 0));
            _handIK.OnNoteOff(note);
            _fingerPose.OnNoteOff(note);
            yield return new WaitForSeconds(0.65f);

            // 与绑定旋转比较，只测拇指屈曲，排除腕部移动和转动。
            var axes = ((Characters.ICharacterJointAxesProvider)_session.CurrentCharacterView).JointAxes;
            float curl = Quaternion.Angle(axes.CurlLocalRotation(middle, 1, 0f), middle.localRotation)
                + Quaternion.Angle(axes.CurlLocalRotation(tip, 2, 0f), tip.localRotation);
            Assert.That(curl, Is.InRange(17f, 28f), "松键后的中节与末节合计应保持轻弯");
        }

        [UnityTest]
        public IEnumerator ImportedThumb_ReachesWhiteAndBlackKeysOnBothHands()
        {
            var failures = new List<string>();
            var keys = _session.GetComponent<PianoKeyAnimator>();
            foreach (string id in Characters.CharacterRegistry.BuiltInIds)
            {
                _session.RestoreCharacter(id);
                yield return null;
                Assert.AreEqual(id, _session.CurrentCharacterView.CharacterId);
                _fingerPose.Provider = new SingleThumbFingering();
                Assert.IsTrue((_avatar.LeftHand as IThumbContactRig)?.HasThumbPad == true, id);
                Assert.IsTrue((_avatar.RightHand as IThumbContactRig)?.HasThumbPad == true, id);

                foreach (int pitch in new[] { 51, 52, 60, 61 })
                {
                    bool left = pitch < 60;
                    var note = new MidiNoteEvent(pitch, 0f, 1f, 85, 0, 0)
                    {
                        Hand = left ? NoteHand.Left : NoteHand.Right
                    };
                    keys.OnNoteOn(note);
                    _highlighter.OnNoteOn(note);
                    _handIK.OnNoteOn(note);
                    _fingerPose.OnNoteOn(note);
                    yield return new WaitForSeconds(0.40f);
                    Assert.AreEqual(pitch, _fingerPose.GetFingerPitch(left, 0));
                    Assert.IsTrue(_handIK.TryGetFingertipTarget(left, 0, out var target));
                    var rig = (IFingerJointRig)(left ? _avatar.LeftHand : _avatar.RightHand);
                    rig.TryGetJoints(0, out var proximal, out var intermediate, out var distal);
                    var pad = rig.GetFingertip(0).position;
                    var skin = MeasureThumbSkin(pitch, proximal, intermediate, distal);
                    Assert.IsTrue(_keyMap.TryGetKey(pitch, out var key));
                    var top = key.Find("Mesh").GetComponent<MeshFilter>();
                    float surfaceHeight = top.sharedMesh.bounds.max.y;
                    float height = top.transform.InverseTransformPoint(pad).y - surfaceHeight;
                    float horizontal = new Vector2(pad.x - target.x, pad.z - target.z).magnitude;
                    string result = $"{id} {(left ? "左" : "右")} {pitch}：水平 {horizontal * 1000:0.0} mm，" +
                        $"高度 {height * 1000:0.0} mm，" +
                        $"末节 {(top.transform.InverseTransformPoint(distal.position).y - surfaceHeight) * 1000:0.0} mm，" +
                        $"中节 {(top.transform.InverseTransformPoint(intermediate.position).y - surfaceHeight) * 1000:0.0} mm，" +
                        $"蒙皮触键 {skin.target * 1000:0.0} mm，邻键余量 {skin.nearby * 1000:0.0} mm";
                    Debug.Log("[THUMB] " + result);
                    if (height < -0.001f || height > 0.006f || horizontal > 0.009f ||
                        skin.target < -0.003f || skin.target > 0.004f || skin.nearby < -0.004f ||
                        Mathf.Min(top.transform.InverseTransformPoint(intermediate.position).y,
                            top.transform.InverseTransformPoint(distal.position).y) < surfaceHeight + 0.001f)
                        failures.Add(result);
                    keys.OnNoteOff(note);
                    _highlighter.OnNoteOff(note);
                    _handIK.OnNoteOff(note);
                    _fingerPose.OnNoteOff(note);
                    yield return new WaitForSeconds(0.12f);
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private (float target, float nearby) MeasureThumbSkin(int pitch, Transform proximal,
            Transform intermediate, Transform distal)
        {
            var tops = new MeshFilter[5];
            for (int i = 0; i < tops.Length; i++)
                if (_keyMap.TryGetKey(pitch + i - 2, out var key))
                    tops[i] = key.Find("Mesh")?.GetComponent<MeshFilter>();

            float target = float.PositiveInfinity;
            float nearby = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                foreach (var skinned in _session.CurrentCharacterView.RootTransform.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = skinned.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    var bones = skinned.bones;
                    int root = System.Array.IndexOf(bones, proximal);
                    int middle = System.Array.IndexOf(bones, intermediate);
                    int end = System.Array.IndexOf(bones, distal);
                    if (end < 0) continue;
                    var weights = mesh.boneWeights;
                    // useScale = true：顶点按渲染器完整变换（含缩放）烘焙，下面的 TransformPoint 才不会二次缩放
                    // （genki 的角色根缩放 1.08；不带缩放烘焙会让每个顶点偏移约 8%）
                    skinned.BakeMesh(baked, true);
                    var vertices = baked.vertices;

                    bool IsThumb(int index) => index == root || index == middle || index == end;
                    for (int vertex = 0; vertex < weights.Length; vertex++)
                    {
                        var w = weights[vertex];
                        float influence = (IsThumb(w.boneIndex0) ? w.weight0 : 0f) +
                            (IsThumb(w.boneIndex1) ? w.weight1 : 0f) +
                            (IsThumb(w.boneIndex2) ? w.weight2 : 0f) +
                            (IsThumb(w.boneIndex3) ? w.weight3 : 0f);
                        if (influence < 0.50f) continue;
                        var point = skinned.transform.TransformPoint(vertices[vertex]);
                        for (int i = 0; i < tops.Length; i++)
                        {
                            var top = tops[i];
                            if (top == null) continue;
                            var local = top.transform.InverseTransformPoint(point);
                            var bounds = top.sharedMesh.bounds;
                            const float Edge = 0.0013f;
                            if (local.x < bounds.min.x + Edge || local.x > bounds.max.x - Edge ||
                                local.z < bounds.min.z + Edge || local.z > bounds.max.z - Edge) continue;
                            float height = local.y - bounds.max.y;
                            if (i == 2) target = Mathf.Min(target, height);
                            else nearby = Mathf.Min(nearby, height);
                        }
                    }
                }
            }
            finally { Object.Destroy(baked); }
            return (target, nearby);
        }

        [UnityTest]
        public IEnumerator ImportedFinger_DoesNotTwistTowardAKeyBehindTheKnuckle()
        {
            const int pitch = 64;
            _session.RestoreCharacter(Characters.CharacterRegistry.VroidGenkiId);
            yield return null;
            var right = (IFingerJointRig)_avatar.RightHand;
            var note = new MidiNoteEvent(pitch, 0f, 2f, 80, 0, 0) { Hand = NoteHand.Right };
            _fingerPose.OnNoteOn(note);
            yield return null;
            int index = _fingerPose.GetFingerIndexForPitch(false, pitch);
            Assert.That(index, Is.InRange(0, 4));
            _fingerPose.UseAnatomicalContact = true;
            _fingerPose.TargetSource = new BehindKnuckleTarget(right, index);
            yield return new WaitForSeconds(0.35f);
            Assert.IsTrue(right.TryGetJoints(index, out var proximal, out var intermediate, out _));
            var axes = _fingerPose.JointAxes;
            var current = proximal.localRotation;
            float nearestHingeAngle = 180f;
            for (int curl = 0; curl <= 82; curl++)
                nearestHingeAngle = Mathf.Min(nearestHingeAngle,
                    Quaternion.Angle(current, axes.CurlLocalRotation(proximal, 0, curl)));
            Assert.LessOrEqual(nearestHingeAngle, index == 0 ? 25.5f : 15.5f,
                "够不到的琴键不应把掌指关节拉出合理的侧摆范围");
            Assert.Greater(Vector3.SqrMagnitude(intermediate.position - proximal.position), 0f);
        }

        private sealed class BehindKnuckleTarget : IFingertipTargetSource
        {
            private readonly IFingerJointRig _hand;
            private readonly int _finger;
            public BehindKnuckleTarget(IFingerJointRig hand, int finger)
            {
                _hand = hand;
                _finger = finger;
            }

            public bool TryGetFingertipTarget(bool leftHand, int fingerIndex, out Vector3 point)
            {
                point = default;
                return !leftHand && fingerIndex == _finger && TryGetKeyPressPoint(64, out point);
            }

            public bool TryGetKeyPressPoint(int pitch, out Vector3 point)
            {
                _hand.TryGetJoints(_finger, out var root, out _, out _);
                var tip = _hand.GetFingertip(_finger);
                point = root.position + Quaternion.AngleAxis(65f, Vector3.up) * (tip.position - root.position);
                return true;
            }
        }

        [UnityTest]
        public IEnumerator UpcomingNote_PreparesAFingerBeforeTheKeyMoves()
        {
            const int pitch = 64;
            Assert.IsTrue(_handIK.TryGetKeyPressPoint(pitch, out var surface));
            _handIK.SetUpcomingPitches(-1, pitch);
            // 两个组件挂在同一对象。只运行动作组件，保留测试注入的预备音。
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            typeof(HandIKController).GetMethod("Update", flags).Invoke(_handIK, null);
            typeof(FingerPoseController).GetMethod("Update", flags).Invoke(_fingerPose, null);

            int readyFinger = -1;
            for (int finger = 0; finger < 5; finger++)
            {
                if (_fingerPose.GetPhase(false, finger) != FingerPhase.Approach) continue;
                Assert.AreEqual(-1, readyFinger, "预备时只有负责下一音的手指取落点");
                readyFinger = finger;
            }

            Assert.GreaterOrEqual(readyFinger, 0, "手指应在音符起音前开始预备");
            Assert.IsTrue(_handIK.TryGetFingertipTarget(false, readyFinger, out var approach));
            Assert.AreEqual(surface.x, approach.x, 0.001f);
            Assert.Greater(approach.y, surface.y + 0.004f, "预备手指应悬在正确按键上方");
            Assert.AreEqual(-1, _fingerPose.GetFingerIndexForPitch(false, pitch), "预备阶段不应提交按键状态");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OverlappingMiddleC_ChangesHandsOnlyWhenTheOtherNoteEnds()
        {
            _highlighter.Mode = HighlightMode.Active;
            var left = new MidiNoteEvent(60, 0f, 2f, 80, 0, 0) { Hand = NoteHand.Left };
            var right = new MidiNoteEvent(60, 0.5f, 0.5f, 80, 1, 0) { Hand = NoteHand.Right };
            var animator = Object.FindFirstObjectByType<PianoKeyAnimator>();

            _handIK.OnNoteOn(left);
            _fingerPose.OnNoteOn(left);
            animator.OnNoteOn(left);
            yield return null;
            Assert.IsTrue(_handIK.IsActivePitchLeft(60));
            Assert.GreaterOrEqual(_fingerPose.GetFingerIndexForPitch(true, 60), 0);

            _handIK.OnNoteOn(right);
            _fingerPose.OnNoteOn(right);
            _highlighter.OnNoteOn(left);
            AssertColorsApproximately(PianoKeyHighlighter.LeftHandColor, KeyColor(60), "跨 C4 仍为左手色");
            _highlighter.OnNoteOn(right);
            animator.OnNoteOn(right);
            AssertColorsApproximately(PianoKeyHighlighter.RightHandColor, KeyColor(60), "同时同音沿用键的音域优先级");
            _highlighter.OnNoteOff(right);
            AssertColorsApproximately(PianoKeyHighlighter.LeftHandColor, KeyColor(60), "右手松开时恢复左手色");
            yield return null;
            Assert.IsFalse(_handIK.IsActivePitchLeft(60));

            _handIK.OnNoteOff(right);
            _fingerPose.OnNoteOff(right);
            animator.OnNoteOff(right);
            yield return null;
            Assert.IsTrue(_handIK.IsActivePitchLeft(60));
            Assert.AreEqual(1, animator.PressedKeyCount);
            Assert.GreaterOrEqual(_fingerPose.GetFingerIndexForPitch(true, 60), 0);
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
            AssertColorsApproximately(PianoKeyHighlighter.LeftHandColor, KeyColor(45),
                "低音键（左手侧）必须用左手色（冷蓝）高亮");
            Assert.Greater(KeyX(45), 0f, "低音键应位于世界 +X 侧（与左手同侧）");
            _highlighter.OnNoteOff(low);

            var high = new MidiNoteEvent(76, 0f, 2f, 90, 0, 0);
            _highlighter.OnNoteOn(high);
            AssertColorsApproximately(PianoKeyHighlighter.RightHandColor, KeyColor(76),
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
                // 指尖目标 = 该音的键面按压点 + 指腹半径（仅垂直方向）。
                // 横向/纵向必须完全一致：手指指向的琴键不能有任何错位；
                // 垂直方向高出一个指腹半径是因为贴键的是指腹（有厚度的软垫），
                // 骨骼末端继续压到键面上就会穿模。
                Assert.AreEqual(pressPoint.x, target.x, 1e-5f,
                    $"手指 {finger} 的指尖目标横向偏离音高 {pitch} 的键位");
                Assert.AreEqual(pressPoint.z, target.z, 1e-5f,
                    $"手指 {finger} 的指尖目标纵向偏离音高 {pitch} 的键位");
                Assert.AreEqual(pressPoint.y + _handIK.FingertipPadRadius, target.y, 1e-4f,
                    $"指尖目标应比键面高一个指腹半径（指腹贴键，不是骨骼末端穿键）");

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

        // Linear 色彩空间（写实化批次 2）下，颜色经 GPU/原生侧 sRGB↔线性往返会引入 ULP 级
        // 浮点差：实测 Expected 与 But was 显示到 3 位小数完全相同仍断言失败（位级不同）。
        // 断言意图是「用哪只手的颜色」而非位级相等，因此改用通道级容差（1e-3，远小于
        // Color32 量化步长 1/255 ≈ 3.9e-3，不可能掩盖真实的颜色错配）。
        private const float ColorTolerance = 1e-3f;

        private static void AssertColorsApproximately(Color expected, Color actual, string message)
        {
            float maxDiff = Mathf.Max(
                Mathf.Abs(expected.r - actual.r),
                Mathf.Abs(expected.g - actual.g),
                Mathf.Abs(expected.b - actual.b),
                Mathf.Abs(expected.a - actual.a));
            Assert.LessOrEqual(maxDiff, ColorTolerance, string.Format(
                "{0}（期望 {1}，实际 {2}，最大通道差 {3:E2}）", message, expected, actual, maxDiff));
        }
    }
}
