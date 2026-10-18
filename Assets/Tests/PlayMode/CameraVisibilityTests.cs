using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.UI;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 视角可用性测试（对应**审查 3.2**「建议增加『视角可用性』测试，不只验证相机状态切换」的
    /// 9 个必测场景与观察标准，以及**审查 6.2** 相机验收清单）。
    /// <para>
    /// 与既有 <c>PlayModeIntegrationTests</c> 的区别：那些测试只验证「切换视角不改变播放状态」，
    /// 本类改为对**取景本身**做可断言检查 —— 活动手 / 指尖 / 即将按下的琴键是否落在安全视口内、
    /// 手部是否被 UI 遮挡、双手跨度变化时是否突然缩放、seek 与 A-B 循环后相机能否收敛。
    /// </para>
    /// <para>
    /// <b>不依赖渲染</b>：全部断言基于 <see cref="Camera.WorldToViewportPoint"/> 的投影数学与
    /// <see cref="HandIKController"/> 的目标状态，因此在 <c>-nographics</c>（无图形设备）下同样执行，
    /// 不会像 <c>VisualSnapshotTests</c> 那样被跳过。
    /// </para>
    /// <para>
    /// <b>阈值来源</b>：审查 3.2 只给了观察标准（「是否同时可见」「是否被遮挡」「是否突然缩放」），
    /// 因此本类把观察标准量化成阈值并在每条断言消息里带上实际值与阈值，便于回归时快速定位。
    /// 具体取值见各私有方法的文档注释。
    /// </para>
    /// </summary>
    public class CameraVisibilityTests
    {
        // ==================== 量化阈值（对应审查 3.2 观察标准 / 6.2 相机验收清单） ====================

        /// <summary>活动手「核心点」（手腕 / 掌心）的安全视口内边距：必须落在 [0.05, 0.95]。</summary>
        private const float HandCoreMargin = 0.05f;

        /// <summary>指根等次级采样点的视口内边距（比核心点略宽，允许手指略贴边）。</summary>
        private const float HandFingerMargin = 0.03f;

        /// <summary>特写下「临近琴键」键根的视口内边距：必须落在 [0.02, 0.98]。</summary>
        private const float CloseupKeyMargin = 0.02f;

        /// <summary>全景下「琴键两端 / 人物包围盒」的视口内边距：审查 3.2 要求落在 [0.0, 1.0]。</summary>
        private const float FullViewMargin = 0f;

        /// <summary>相机距离单帧变化上限（米/帧，文档建议值；低帧率下按 <see cref="MaxDistanceRate"/> 与
        /// <see cref="MaxDistanceStepLowFps"/> 放宽）。</summary>
        private const float MaxDistanceStepPerFrame = 0.15f;

        /// <summary>低帧率保护：距离单帧变化上限不会放宽到超过该值（米/帧）。</summary>
        private const float MaxDistanceStepLowFps = 0.6f;

        /// <summary>相机距离等效变化速率上限（米/秒）：避免把「帧率低」误判为「镜头突缩」。</summary>
        private const float MaxDistanceRate = 6f;

        /// <summary>FOV 单帧变化上限（度/帧，文档建议值）。</summary>
        private const float MaxFovStepPerFrame = 8f;

        /// <summary>低帧率保护：FOV 单帧变化上限不会放宽到超过该值（度/帧）。</summary>
        private const float MaxFovStepLowFps = 24f;

        /// <summary>FOV 等效变化速率上限（度/秒）。</summary>
        private const float MaxFovRate = 60f;

        /// <summary>相机收敛判据：连续两帧位置变化小于该值（米）即视为构图稳定。</summary>
        private const float ConvergePositionEpsilon = 0.002f;

        // ==================== 场景装配 ====================

        private LessonSessionController _session;
        private CameraModeController _camera;
        private AvatarManager _avatar;
        private PianoKeyMap _keyMap;
        private MidiEventScheduler _scheduler;
        private LessonTimeline _timeline;
        private HandIKController _handIK;
        private Camera _cam;

        // 由协程辅助方法写入、供调用方断言的测量结果（避免在嵌套协程里断言）
        private int _convergeFrames = -1;
        private float _maxDistanceStepRatio;
        private float _maxFovStepRatio;
        private float _worstDistanceStep;
        private float _worstFovStep;
        private int _measuredFrames;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return new WaitForSeconds(1.0f); // 等场景装配（BuildScene + Start 载曲）落地

            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _camera = Object.FindFirstObjectByType<CameraModeController>();
            _avatar = Object.FindFirstObjectByType<AvatarManager>();
            _keyMap = Object.FindFirstObjectByType<PianoKeyMap>();
            _scheduler = Object.FindFirstObjectByType<MidiEventScheduler>();
            _timeline = Object.FindFirstObjectByType<LessonTimeline>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _cam = Camera.main;

            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
            Assert.IsNotNull(_camera, "PianoLesson 场景应包含 CameraModeController");
            Assert.IsNotNull(_avatar, "PianoLesson 场景应包含 AvatarManager");
            Assert.IsNotNull(_keyMap, "PianoLesson 场景应包含 PianoKeyMap");
            Assert.IsNotNull(_scheduler, "PianoLesson 场景应包含 MidiEventScheduler");
            Assert.IsNotNull(_timeline, "PianoLesson 场景应包含 LessonTimeline");
            Assert.IsNotNull(_handIK, "PianoLesson 场景应包含 HandIKController");
            Assert.IsNotNull(_cam, "PianoLesson 场景应有主摄像机");

            NormalizeCameraAspect(_cam);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        /// <summary>
        /// <c>-nographics</c> 与极小窗口下 <c>Screen</c> 尺寸可能退化，使 <c>Camera.aspect</c> 无效，
        /// 进而让 <see cref="Camera.WorldToViewportPoint"/> 的投影矩阵失去意义。
        /// 这里只在宽高比明显非法时补一个 16:9（与 <c>CameraModeController</c> 内部的
        /// <c>Aspect</c> 兜底值一致），**不修改任何业务源码**。
        /// </summary>
        private static void NormalizeCameraAspect(Camera cam)
        {
            float aspect = cam.aspect;
            if (float.IsNaN(aspect) || aspect < 0.5f || aspect > 4f)
                cam.aspect = 1280f / 720f;
        }

        // ==================== 可诊断信息 ====================

        /// <summary>每条失败断言都带上这一串：实际值 + 阈值 + 当前相机状态（审查 3.2「可重复的检查」）。</summary>
        private string Diag()
        {
            string mode = _camera != null ? string.Format("{0}/{1}", _camera.Mode, _camera.ModeLabel) : "n/a";
            string transition = _camera != null ? _camera.IsTransitioning.ToString() : "n/a";
            string distance = _camera != null ? _camera.CurrentDistance.ToString("F3") : "n/a";
            string fov = _cam != null ? _cam.fieldOfView.ToString("F1") : "n/a";
            string pos = _cam != null
                ? string.Format("({0:F2},{1:F2},{2:F2})",
                    _cam.transform.position.x, _cam.transform.position.y, _cam.transform.position.z)
                : "n/a";
            string hand = _session != null ? _session.PracticeHand.ToString() : "n/a";
            string playback = _session != null ? _session.PlaybackState.ToString() : "n/a";
            string time = _timeline != null ? _timeline.CurrentTime.ToString("F2") : "n/a";
            string loop = _session != null ? _session.IsLooping.ToString() : "n/a";

            return string.Format(
                "［相机］Mode={0}, IsTransitioning={1}, CurrentDistance={2}m, FOV={3}°, pos={4}; " +
                "［会话］练习手={5}, 播放={6}@{7}s, 循环={8}, 窗口={9}×{10}",
                mode, transition, distance, fov, pos, hand, playback, time, loop, Screen.width, Screen.height);
        }

        // ==================== 视口断言 ====================

        /// <summary>把世界点投影到视口坐标并断言落在 <c>[margin, 1-margin]</c> 内且位于相机前方（z &gt; 0）。</summary>
        private void AssertPointInViewport(Vector3 world, string what, float margin)
        {
            Vector3 vp = _cam.WorldToViewportPoint(world);
            Assert.IsFalse(float.IsNaN(vp.x) || float.IsNaN(vp.y) || float.IsNaN(vp.z),
                string.Format("{0} 的视口投影为 NaN（相机投影矩阵异常）。world=({1:F3},{2:F3},{3:F3})。{4}",
                    what, world.x, world.y, world.z, Diag()));
            Assert.Greater(vp.z, 0f,
                string.Format("{0} 落在相机后方（z={1:F3} ≤ 0）。world=({2:F3},{3:F3},{4:F3})。{5}",
                    what, vp.z, world.x, world.y, world.z, Diag()));
            Assert.That(vp.x, Is.InRange(margin, 1f - margin),
                string.Format("{0} 视口 x={1:F3} 超出阈值区间 [{2:F2}, {3:F2}]。world=({4:F3},{5:F3},{6:F3})。{7}",
                    what, vp.x, margin, 1f - margin, world.x, world.y, world.z, Diag()));
            Assert.That(vp.y, Is.InRange(margin, 1f - margin),
                string.Format("{0} 视口 y={1:F3} 超出阈值区间 [{2:F2}, {3:F2}]。world=({4:F3},{5:F3},{6:F3})。{7}",
                    what, vp.y, margin, 1f - margin, world.x, world.y, world.z, Diag()));
        }

        /// <summary>断言某只手的「活动手可见性」：手腕 / 掌心落在安全视口，指根放宽一级。</summary>
        private void AssertHandVisible(IHandRig hand, string what)
        {
            Assert.IsNotNull(hand, string.Format("{0}：IHandRig 不可用。{1}", what, Diag()));
            Assert.IsNotNull(hand.Wrist, string.Format("{0}：Wrist 为空。{1}", what, Diag()));

            AssertPointInViewport(hand.Wrist.position, what + "·手腕", HandCoreMargin);
            if (hand.Palm != null)
                AssertPointInViewport(hand.Palm.position, what + "·掌心", HandCoreMargin);

            IReadOnlyList<Transform> fingers = hand.Fingers;
            Assert.IsNotNull(fingers, string.Format("{0}：Fingers 为空。{1}", what, Diag()));
            Assert.Greater(fingers.Count, 0, string.Format("{0}：Fingers 为空列表。{1}", what, Diag()));
            for (int i = 0; i < fingers.Count; i++)
            {
                Transform finger = fingers[i];
                if (finger == null) continue;
                AssertPointInViewport(finger.position, string.Format("{0}·指根{1}", what, i), HandFingerMargin);
            }
        }

        /// <summary>
        /// 按当前练习手断言「活动手」可见（审查 3.2 观察标准 1、2 / 6.2「手部特写能同时覆盖活动手与主要指尖」）。
        /// 双手模式对两只手同时断言：特写取景就是「双手范围 ∪ 琴键范围」，双手都应在框内。
        /// </summary>
        private void AssertPracticeHandVisible(string checkpoint)
        {
            switch (_session.PracticeHand)
            {
                case PracticeHand.LeftOnly:
                    AssertHandVisible(_avatar.LeftHand, checkpoint + "·活动手(左手)");
                    break;
                case PracticeHand.RightOnly:
                    AssertHandVisible(_avatar.RightHand, checkpoint + "·活动手(右手)");
                    break;
                default:
                    AssertHandVisible(_avatar.RightHand, checkpoint + "·活动手(双手·右手)");
                    AssertHandVisible(_avatar.LeftHand, checkpoint + "·活动手(双手·左手)");
                    break;
            }

            AssertHeadNotOccludingHands(checkpoint);
        }

        /// <summary>
        /// 断言「角色头部不在相机到双手的视线上」（审查 3.2 观察标准「手部是否被 UI、琴体或画面边缘遮挡」）。
        /// <para>
        /// 手部特写需要「从后上方越过头顶」看琴键；若俯角不足，相机水平后移量会超过
        /// 焦点到头部的水平距离，头部就正好落在相机→双手的连线上——画面正中会被头发挡住。
        /// 这类问题<b>不会</b>被「投影点是否在视口内」的断言发现（投影点在视口内，只是被挡住了），
        /// 因此单独做几何遮挡检查，作为回归守护。
        /// </para>
        /// </summary>
        private void AssertHeadNotOccludingHands(string checkpoint)
        {
            if (_session.CameraMode != CameraMode.Closeup) return;   // 全景/自由观察不适用

            var skeleton = _avatar.CurrentSkeleton;
            if (skeleton == null || skeleton.Head == null) return;
            if (_avatar.LeftHand == null || _avatar.RightHand == null) return;

            // 头部半径量级（含发型体积）：写实女性 headR≈0.094，男性≈0.110，取 0.10 作为判定半径
            const float headRadius = 0.10f;

            Vector3 head = skeleton.Head.position;
            Vector3 camPos = _cam.transform.position;
            Vector3 hands = (_avatar.LeftHand.Wrist.position + _avatar.RightHand.Wrist.position) * 0.5f;

            float clearance = DistanceToSegment(head, camPos, hands);
            Assert.Greater(clearance, headRadius, string.Format(
                "{0}：角色头部挡住了相机到双手的视线（头部到视线距离 {1:F3}m ≤ {2:F3}m ⇒ 俯角不足）。" +
                "head={3:F3} cam={4:F3} hands={5:F3}。{6}",
                checkpoint, clearance, headRadius, head, camPos, hands, Diag()));
        }

        /// <summary>点到线段的最短距离（用于几何遮挡判定）。</summary>
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-8f) return Vector3.Distance(point, a);
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lenSq);
            return Vector3.Distance(point, a + ab * t);
        }

        /// <summary>断言某个音高的键根（键后端 pivot，见 <c>PianoKeyMap</c>）在视口内。</summary>
        private void AssertKeyInViewport(int pitch, string what, float margin)
        {
            Assert.IsTrue(_keyMap.TryGetKey(pitch, out Transform key) && key != null,
                string.Format("{0}：找不到键根（pitch={1}）。{2}", what, pitch, Diag()));
            AssertPointInViewport(key.position, what, margin);
        }

        /// <summary>断言全景下 88 键两端（A0=21 / C8=108）都在 [0,1] 视口内（审查 3.2 「全景是否能看到完整键盘」）。</summary>
        private void AssertFullKeyboardVisible(string checkpoint)
        {
            Assert.AreEqual(21, PianoKeyMap.MinPitch, "88 键最低音应为 A0=21（PianoKeyMap 契约变化需同步本测试）");
            Assert.AreEqual(108, PianoKeyMap.MaxPitch, "88 键最高音应为 C8=108（PianoKeyMap 契约变化需同步本测试）");
            AssertKeyInViewport(PianoKeyMap.MinPitch, checkpoint + "·全景·A0(21) 键根", FullViewMargin);
            AssertKeyInViewport(PianoKeyMap.MaxPitch, checkpoint + "·全景·C8(108) 键根", FullViewMargin);
        }

        /// <summary>断言角色 Renderer 包围盒的 8 个角点全部入画（审查 3.2「角色未被裁切」/ 6.2「人物不出画面」）。</summary>
        private void AssertAvatarBoundsInViewport(string checkpoint)
        {
            Renderer[] renderers = _avatar.GetComponentsInChildren<Renderer>(false);
            Assert.Greater(renderers.Length, 0,
                string.Format("{0}：当前角色没有任何 Renderer。{1}", checkpoint, Diag()));

            bool has = false;
            Bounds box = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (has) box.Encapsulate(r.bounds);
                else { box = r.bounds; has = true; }
            }
            Assert.IsTrue(has, string.Format("{0}：无法计算角色包围盒。{1}", checkpoint, Diag()));

            Vector3 min = box.min;
            Vector3 max = box.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                AssertPointInViewport(corner, string.Format("{0}·包围盒角点{1}", checkpoint, i), FullViewMargin);
            }
        }

        /// <summary>
        /// 断言「即将按下的琴键」可见（审查 3.2 观察标准 1「活动手掌、主要指尖和对应琴键是否同时可见」）。
        /// <para>
        /// 需要 <see cref="HighlightMode.Upcoming"/>（或 Both）：只有此时会话才会把琴键音高范围喂给相机
        /// （<c>LessonSessionController.ForwardUpcomingKeySpan</c> → <c>CameraModeController.SetFramingKeySpan</c>）。
        /// 因此查询窗口必须与会话的预提示窗口**完全一致**（<see cref="LessonSessionController.HighlightLookAhead"/>），
        /// 否则会断言到根本没被并入取景范围的琴键。
        /// </para>
        /// <para>本方法只在<see cref="PracticeHand.Both"/>下使用：单手练习的取景按方案 4.2.4 以活动手为主并限制合并范围，
        /// 远端琴键被有意排除在取景框外，对它断言可见会与设计冲突。</para>
        /// </summary>
        private void AssertUpcomingKeysInViewport(string checkpoint, int maxKeys, float margin)
        {
            float horizon = _session.HighlightLookAhead;
            var buffer = new List<MidiNoteEvent>();
            _scheduler.CollectNotesInRange(_timeline.CurrentTime, _timeline.CurrentTime + horizon, buffer);
            Assert.Greater(buffer.Count, 0,
                string.Format("{0}：当前时间点 {1:F2}s 起 {2:F2}s（=HighlightLookAhead）内没有音符，无法验证「临近琴键可见」。{3}",
                    checkpoint, _timeline.CurrentTime, horizon, Diag()));

            int count = Mathf.Min(buffer.Count, maxKeys);
            for (int i = 0; i < count; i++)
                AssertKeyInViewport(buffer[i].Pitch,
                    string.Format("{0}·临近琴键 pitch={1}", checkpoint, buffer[i].Pitch), margin);
        }

        // ==================== UI 遮挡检查 ====================

        /// <summary>
        /// 取屏幕上某个 UI 矩形在**视口坐标**下的区域。
        /// <para>
        /// 做法：Overlay 画布的世界坐标即屏幕像素坐标，因此先用画布自身的角点求出「屏幕像素 → 视口」的
        /// 换算（origin + size），再把目标 <see cref="RectTransform"/> 的世界角点换算过去。
        /// 这样整个换算**不依赖 <c>Screen.width/height</c>**，在 <c>-nographics</c> 下同样有效。
        /// </para>
        /// <para>
        /// 若画布坐标不可用（<c>-nographics</c> 下画布可能尚未布局），退化为按
        /// <see cref="UITheme.Layout"/> 设计令牌估算矩形（见 <see cref="TryGetFallbackUiRect"/>），
        /// 保证这条检查不会因为「取不到矩形」而变成恒真。
        /// </para>
        /// </summary>
        private bool TryGetUiViewportRect(string gameObjectName, out Rect rect)
        {
            rect = default;
            if (_cam.rect.width < 0.99f || _cam.rect.height < 0.99f) return false; // 相机只画一部分屏幕时坐标系不一致

            GameObject go = GameObject.Find(gameObjectName);
            if (go == null || !go.activeInHierarchy) return false; // 未展开的面板不参与遮挡检查

            RectTransform target = go.transform as RectTransform;
            if (target == null) return TryGetFallbackUiRect(gameObjectName, out rect);

            if (!TryGetCanvasViewportSpace(out Vector2 origin, out Vector2 size))
                return TryGetFallbackUiRect(gameObjectName, out rect);

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = (corners[i].x - origin.x) / size.x;
                float y = (corners[i].y - origin.y) / size.y;
                if (float.IsNaN(x) || float.IsNaN(y)) return TryGetFallbackUiRect(gameObjectName, out rect);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            if (rect.width > 0.001f && rect.height > 0.001f) return true;
            return TryGetFallbackUiRect(gameObjectName, out rect);
        }

        /// <summary>
        /// 退路：按 <see cref="UITheme.Layout"/> 的参考分辨率（1280×720，与 CanvasScaler.referenceResolution 一致，
        /// 且 <c>matchWidthOrHeight = 1</c>）换算出主播放栏 / 分析面板的视口矩形。
        /// 仅在目标对象**存在且激活**但画布坐标不可用时使用；窗口不是 16:9 时该估算会有偏差。
        /// </summary>
        private bool TryGetFallbackUiRect(string gameObjectName, out Rect rect)
        {
            rect = default;
            if (_cam == null) return false;

            float refW = UITheme.Layout.ReferenceResolution.x;
            float refH = UITheme.Layout.ReferenceResolution.y;
            float margin = UITheme.Layout.ScreenMargin;
            if (refW <= 1f || refH <= 1f) return false;

            if (gameObjectName == "MainPlayBar")
            {
                float barHeight = _camera != null && _camera.Mode == CameraMode.Closeup
                    ? UITheme.Layout.MainBarHeightCompact
                    : UITheme.Layout.MainBarHeightNormal;
                rect = Rect.MinMaxRect(margin / refW, margin / refH,
                    (refW - margin) / refW, (margin + barHeight) / refH);
                return true;
            }

            if (gameObjectName == "AnalysisPanel")
            {
                float panelWidth = UITheme.Layout.AnalysisPanelWidth;
                rect = Rect.MinMaxRect((refW - margin - panelWidth) / refW, margin / refH,
                    (refW - margin) / refW, (refH - margin) / refH);
                return true;
            }

            return false;
        }

        /// <summary>求任意一个 Overlay 画布在屏幕像素坐标下的原点与尺寸（不用 Screen API）。</summary>
        private static bool TryGetCanvasViewportSpace(out Vector2 origin, out Vector2 size)
        {
            origin = Vector2.zero;
            size = Vector2.zero;

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                RectTransform rt = canvas.transform as RectTransform;
                if (rt == null) continue;

                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    if (corners[k].x < minX) minX = corners[k].x;
                    if (corners[k].x > maxX) maxX = corners[k].x;
                    if (corners[k].y < minY) minY = corners[k].y;
                    if (corners[k].y > maxY) maxY = corners[k].y;
                }
                if (maxX - minX > 1f && maxY - minY > 1f)
                {
                    origin = new Vector2(minX, minY);
                    size = new Vector2(maxX - minX, maxY - minY);
                    return true;
                }
            }
            return false;
        }

        /// <summary>收集某只手全部可采样点（手腕 / 掌心 / 五指指根），返回是否成功。</summary>
        private static bool TryCollectHandPoints(IHandRig hand, List<Vector3> into)
        {
            if (hand == null || hand.Wrist == null) return false;
            into.Add(hand.Wrist.position);
            if (hand.Palm != null) into.Add(hand.Palm.position);
            IReadOnlyList<Transform> fingers = hand.Fingers;
            if (fingers != null)
            {
                for (int i = 0; i < fingers.Count; i++)
                    if (fingers[i] != null) into.Add(fingers[i].position);
            }
            return true;
        }

        /// <summary>把一组世界点投影后求视口质心；有任一点在相机后方或投影为 NaN 时返回 false。</summary>
        private bool TryViewportCentroid(List<Vector3> world, out Vector2 centroid)
        {
            centroid = Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < world.Count; i++)
            {
                Vector3 vp = _cam.WorldToViewportPoint(world[i]);
                if (float.IsNaN(vp.x) || float.IsNaN(vp.y) || vp.z <= 0f) return false;
                sum += new Vector2(vp.x, vp.y);
            }
            centroid = sum / world.Count;
            return true;
        }

        /// <summary>
        /// 断言活动手不被 UI 覆盖（审查 3.2 观察标准 2「手部是否被 UI、琴体或画面边缘遮挡」/
        /// 6.2 UI「1280×720 下布局仍可操作，主画面没有被控制栏过度覆盖」）。
        /// <para>
        /// 检查两个常驻区域：底部主播放栏（<c>MainPlayBar</c>，始终存在）与右下分析面板
        /// （<c>AnalysisPanel</c>，仅展开时检查）。为避免「矩形取不到 → 断言恒真」，
        /// 先校验矩形本身是否落在预期的屏幕位置与尺寸区间。
        /// </para>
        /// <para>
        /// 底部主播放栏对**全部**采样点断言（它常驻且压在画面上，遮挡任何手部点都是缺陷）；
        /// 右下分析面板只对**活动手质心**断言 —— 它是用户主动打开的临时浮层，压住画面右侧属预期行为，
        /// 对整片采样点断言会把「远端指根贴到面板下」误判为缺陷。
        /// </para>
        /// </summary>
        private void AssertActiveHandsNotUnderUi(string checkpoint)
        {
            var world = new List<Vector3>();
            switch (_session.PracticeHand)
            {
                case PracticeHand.LeftOnly:
                    TryCollectHandPoints(_avatar.LeftHand, world);
                    break;
                case PracticeHand.RightOnly:
                    TryCollectHandPoints(_avatar.RightHand, world);
                    break;
                default:
                    TryCollectHandPoints(_avatar.RightHand, world);
                    TryCollectHandPoints(_avatar.LeftHand, world);
                    break;
            }
            Assert.Greater(world.Count, 0, string.Format("{0}：采样不到活动手点位。{1}", checkpoint, Diag()));

            var viewports = new List<Vector2>(world.Count);
            for (int i = 0; i < world.Count; i++)
            {
                Vector3 vp = _cam.WorldToViewportPoint(world[i]);
                if (float.IsNaN(vp.x) || float.IsNaN(vp.y) || vp.z <= 0f)
                {
                    // 活动手可见性由 AssertPracticeHandVisible 负责报错，这里只跳过无法判断的点
                    continue;
                }
                viewports.Add(new Vector2(vp.x, vp.y));
            }
            if (viewports.Count == 0) return; // 全部点在相机后方：交由可见性断言报错

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < viewports.Count; i++) centroid += viewports[i];
            centroid /= viewports.Count;

            if (TryGetUiViewportRect("MainPlayBar", out Rect bar))
            {
                // 防恒真：主播放栏必须确实贴在屏幕底部、且宽度接近全屏
                Assert.Less(bar.yMax, 0.35f, string.Format(
                    "{0}：主播放栏矩形不贴底（yMax={1:F3}），UI 矩形换算可能失效。{2}", checkpoint, bar.yMax, Diag()));
                Assert.Greater(bar.width, 0.5f, string.Format(
                    "{0}：主播放栏矩形宽度异常（{1:F3}）。{2}", checkpoint, bar.width, Diag()));

                for (int i = 0; i < viewports.Count; i++)
                {
                    Assert.IsFalse(bar.Contains(viewports[i]), string.Format(
                        "{0}：活动手采样点 {1} 落在底部主播放栏内（矩形 {2:F3},{3:F3}–{4:F3},{5:F3}）。{6}",
                        checkpoint, viewports[i], bar.xMin, bar.yMin, bar.xMax, bar.yMax, Diag()));
                }
            }

            if (TryGetUiViewportRect("AnalysisPanel", out Rect panel))
            {
                // 防恒真：分析面板必须确实在右侧、且宽度接近设计值 420/1280 ≈ 0.33
                Assert.Greater(panel.width, 0.1f, string.Format(
                    "{0}：分析面板矩形宽度异常（{1:F3}）。{2}", checkpoint, panel.width, Diag()));
                Assert.Greater(panel.xMin, 0.4f, string.Format(
                    "{0}：分析面板未贴在屏幕右侧（xMin={1:F3}）。{2}", checkpoint, panel.xMin, Diag()));

                // 分析面板是「用户主动打开的临时浮层」，压住画面右侧是预期行为；
                // 因此只对「活动手质心」断言（这是手在画面上实际所在的位置），
                // 不对整片采样点断言，避免把「远端指根压到面板下」误判为缺陷。
                Assert.IsFalse(panel.Contains(centroid), string.Format(
                    "{0}：活动手质心 {1} 落在右侧分析面板内（矩形 {2:F3},{3:F3}–{4:F3},{5:F3}）。{6}",
                    checkpoint, centroid, panel.xMin, panel.yMin, panel.xMax, panel.yMax, Diag()));
            }
        }

        // ==================== 协程辅助（测量结果写入字段，断言交给调用方） ====================

        /// <summary>
        /// 等待相机收敛：连续两帧位置变化 &lt; <see cref="ConvergePositionEpsilon"/> 且不在过渡中。
        /// 结果写入 <see cref="_convergeFrames"/>（-1 = 未在预算内收敛）。
        /// </summary>
        private IEnumerator WaitCameraConverged(int maxFrames)
        {
            _convergeFrames = -1;
            Vector3 prev = _cam.transform.position;
            for (int i = 0; i < maxFrames; i++)
            {
                yield return null;
                Vector3 now = _cam.transform.position;
                if (!_camera.IsTransitioning && Vector3.Distance(now, prev) < ConvergePositionEpsilon)
                {
                    _convergeFrames = i + 1;
                    yield break;
                }
                prev = now;
            }
        }

        /// <summary>
        /// 连续若干帧测量 <c>CurrentDistance</c> 与 FOV 的单帧变化，写入
        /// <see cref="_maxDistanceStepRatio"/> / <see cref="_maxFovStepRatio"/>（&gt; 1 即超出阈值）。
        /// <para>
        /// 阈值 = <c>clamp(速率 × dt, 文档建议的单帧上限, 低帧率保护上限)</c>：
        /// 距离 <c>clamp(6 m/s × dt, 0.15 m/帧, 0.6 m/帧)</c>，FOV <c>clamp(60 °/s × dt, 8°/帧, 24°/帧)</c>。
        /// 正常帧率下等价于文档建议的 0.15 m/帧 与 8°/帧；帧率很低时按 dt 放宽，
        /// 避免把「批量模式帧率低」误判为「相机突缩」。
        /// </para>
        /// <para>过渡帧（<c>IsTransitioning</c>）跳过：模式切换本来就是「一次性移动」而不是抖动。</para>
        /// </summary>
        private IEnumerator MeasureZoomStability(int frames)
        {
            _maxDistanceStepRatio = 0f;
            _maxFovStepRatio = 0f;
            _worstDistanceStep = 0f;
            _worstFovStep = 0f;
            _measuredFrames = 0;

            float prevDistance = _camera.CurrentDistance;
            float prevFov = _cam.fieldOfView;

            for (int i = 0; i < frames; i++)
            {
                yield return null;
                if (_camera.IsTransitioning)
                {
                    prevDistance = _camera.CurrentDistance;
                    prevFov = _cam.fieldOfView;
                    continue;
                }

                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                float distance = _camera.CurrentDistance;
                float fov = _cam.fieldOfView;

                float distanceStep = Mathf.Abs(distance - prevDistance);
                float fovStep = Mathf.Abs(fov - prevFov);
                float distanceLimit = Mathf.Clamp(MaxDistanceRate * dt, MaxDistanceStepPerFrame, MaxDistanceStepLowFps);
                float fovLimit = Mathf.Clamp(MaxFovRate * dt, MaxFovStepPerFrame, MaxFovStepLowFps);

                if (distanceLimit > 1e-6f)
                {
                    float ratio = distanceStep / distanceLimit;
                    if (ratio > _maxDistanceStepRatio) _maxDistanceStepRatio = ratio;
                }
                if (fovLimit > 1e-6f)
                {
                    float ratio = fovStep / fovLimit;
                    if (ratio > _maxFovStepRatio) _maxFovStepRatio = ratio;
                }
                if (distanceStep > _worstDistanceStep) _worstDistanceStep = distanceStep;
                if (fovStep > _worstFovStep) _worstFovStep = fovStep;
                _measuredFrames++;

                prevDistance = distance;
                prevFov = fov;
            }
        }

        /// <summary>断言 <see cref="MeasureZoomStability"/> 的结果（审查 3.2「两手跨度变化时画面是否突然缩放」/ 6.2「不会突然大幅缩放」）。</summary>
        private void AssertZoomStable(string checkpoint)
        {
            Assert.Greater(_measuredFrames, 0,
                string.Format("{0}：没有采到有效帧（全部处于过渡态？）。{1}", checkpoint, Diag()));
            Assert.LessOrEqual(_maxDistanceStepRatio, 1f, string.Format(
                "{0}：相机距离出现突变，单帧最大变化 {1:F4} m 超过阈值 min({2:F2} m/帧, {3:F1} m/s × dt)。{4}",
                checkpoint, _worstDistanceStep, MaxDistanceStepPerFrame, MaxDistanceRate, Diag()));
            Assert.LessOrEqual(_maxFovStepRatio, 1f, string.Format(
                "{0}：相机 FOV 出现突变，单帧最大变化 {1:F3}° 超过阈值 min({2:F1}°/帧, {3:F1}°/s × dt)。{4}",
                checkpoint, _worstFovStep, MaxFovStepPerFrame, MaxFovRate, Diag()));
            Debug.Log(string.Format(
                "[CameraVisibility] {0}：{1} 帧内最大距离步进 {2:F4} m（占阈值 {3:P0}）、最大 FOV 步进 {4:F3}°（占阈值 {5:P0}）",
                checkpoint, _measuredFrames, _worstDistanceStep, _maxDistanceStepRatio, _worstFovStep, _maxFovStepRatio));
        }

        /// <summary>点一下名字为 <paramref name="buttonName"/> 的按钮（不注入鼠标事件，直接触发 onClick）。</summary>
        private static bool ClickButton(string buttonName)
        {
            GameObject go = GameObject.Find(buttonName);
            if (go == null || !go.activeInHierarchy) return false;
            Button button = go.GetComponent<Button>();
            if (button == null || !button.interactable) return false;
            button.onClick.Invoke();
            return true;
        }

        /// <summary>把曲内音符扫成一份平铺列表（用于挑选「和弦 / 最大跨度 / 最大跳进 / 最快片段」等时刻）。</summary>
        private List<MidiNoteEvent> ScanAllNotes()
        {
            return NoteScan.CollectAllNotes(_scheduler, _timeline.TotalDuration);
        }

        private void RequireSong()
        {
            if (!_session.HasSong)
                Assert.Ignore("示例曲目未加载，视角可用性用例无法执行（需要 Assets/StreamingAssets/Songs 下的示例曲）");
        }

        /// <summary>
        /// 找「左右手同时有音符按住」且持续时间最长的时刻，并给出该时刻左手最低音 / 右手最高音
        /// （与 <c>HandIKController.FindHandTarget</c> 的取法一致）。同时输出可供安全播放的时长
        /// （保证这段时间内两组音符都仍被按住）。
        /// 用于确定性地验证「按当前时间轴重建手部目标」，而不是依赖「目标必须发生位移」这种脆弱假设
        /// （示例曲音符之间有天然空档，空档期手停在默认手位是正确行为）。
        /// </summary>
        private static bool TryFindBothHandsHeldTime(List<MidiNoteEvent> notes, out float time,
            out float safePlaySeconds, out int leftPitch, out int rightPitch, float minHold = 0.20f)
        {
            time = 0f; safePlaySeconds = 0f; leftPitch = -1; rightPitch = -1;
            if (notes == null || notes.Count == 0) return false;

            float bestHold = 0f;

            for (int i = 0; i < notes.Count; i++)
            {
                float t = notes[i].StartTime;
                int minLeft = int.MaxValue, maxRight = int.MinValue;
                float holdUntil = float.MaxValue;

                for (int j = 0; j < notes.Count; j++)
                {
                    MidiNoteEvent m = notes[j];
                    if (m.StartTime > t || m.EndTime <= t) continue;   // 该时刻未按住

                    if (m.Pitch < PianoNoteFilter.SplitPitch)
                    {
                        if (m.Pitch < minLeft) minLeft = m.Pitch;
                    }
                    else if (m.Pitch > maxRight)
                    {
                        maxRight = m.Pitch;
                    }
                    if (m.EndTime < holdUntil) holdUntil = m.EndTime;
                }

                if (minLeft == int.MaxValue || maxRight == int.MinValue) continue;  // 必须两手都有

                float hold = holdUntil - t;
                if (hold <= bestHold) continue;

                bestHold = hold;
                time = t + 0.02f;                     // 稍微错开起点，避免落在事件边界上
                safePlaySeconds = Mathf.Clamp(hold - 0.06f, 0.05f, 0.35f);
                leftPitch = minLeft;
                rightPitch = maxRight;
            }

            return bestHold >= minHold;
        }

        // ==================================================================
        // 审查 3.2 必测场景 1：双手相距很远的低音—高音跳跃段
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene1_WideLeap_BothHandsStayFramed()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetHighlightMode(HighlightMode.Upcoming); // 让特写取景并入「即将发生的琴键」（方案 4.2.1）
            _session.ClearLoop();
            yield return new WaitForSeconds(0.6f);

            List<MidiNoteEvent> notes = ScanAllNotes();
            Assert.Greater(notes.Count, 0, "曲目没有音符，" + Diag());
            bool found = NoteScan.TryFindWidestSpanTime(notes, 0.5f, out float time, out int span, out int low, out int high);
            Assert.IsTrue(found, "曲目内找不到可判定跨度的片段，" + Diag());
            Debug.Log(string.Format("[CameraVisibility] 3.2-1 选中 t={0:F2}s，0.5s 窗口内跨度 {1} 半音（pitch {2}–{3}）",
                time, span, low, high));

            _session.Seek(time);
            _session.Play();
            yield return new WaitForSeconds(0.8f);
            _session.Pause();
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0, "3.2-1：拖到大幅跨度片段后相机未在预算内收敛。" + Diag());

            AssertPracticeHandVisible("3.2-1 大幅跨度");
            AssertUpcomingKeysInViewport("3.2-1 大幅跨度", 6, CloseupKeyMargin);
            AssertActiveHandsNotUnderUi("3.2-1 大幅跨度");
        }

        // ==================================================================
        // 审查 3.2 必测场景 2：双手集中在中音区的和弦段
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene2_MiddleChord_HandsKeysAndUiAllClear()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetHighlightMode(HighlightMode.Upcoming);

            List<MidiNoteEvent> notes = ScanAllNotes();
            Assert.Greater(notes.Count, 0, "曲目没有音符，" + Diag());
            bool found = NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float time, out int size);
            if (!found)
                Assert.Ignore("内置示例曲里没有同时发声 ≥2 音的中音区和弦片段，无法验证 3.2-2（请用有和弦的测试曲重跑）");
            Debug.Log(string.Format("[CameraVisibility] 3.2-2 选中 t={0:F2}s，同时发声 {1} 音", time, size));

            // 先在全景下展开「曲目分析」面板（手部特写会折叠练习工具栏，按钮不可见）
            _session.SetCameraMode(CameraMode.Full);
            yield return new WaitForSeconds(0.6f);
            bool panelOpened = ClickButton("Btn_曲目分析");
            yield return new WaitForSeconds(1.0f);

            _session.SetCameraMode(CameraMode.Closeup);
            _session.Seek(time);
            _session.Play();
            yield return new WaitForSeconds(0.8f);
            _session.Pause();
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0, "3.2-2：中音区和弦片段相机未在预算内收敛。" + Diag());

            AssertPracticeHandVisible("3.2-2 中音区和弦");
            AssertUpcomingKeysInViewport("3.2-2 中音区和弦", 6, CloseupKeyMargin);
            // 分析面板已展开 → 这次会真正检查右下分析面板（未展开时该检查自动跳过）
            AssertActiveHandsNotUnderUi("3.2-2 中音区和弦");
            Debug.Log("[CameraVisibility] 3.2-2 分析面板展开=" + panelOpened + "（展开时才会检查右侧面板遮挡）");
        }

        // ==================================================================
        // 审查 3.2 必测场景 3：只有左手有音符的片段
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene3_LeftHandOnly_CameraFocusesLeftHand()
        {
            RequireSong();
            if (_session.Analysis == null || _session.Analysis.LeftHandCount == 0)
                Assert.Ignore("当前曲目没有左手音符（音高 < C4），无法验证「只有左手有音符的片段」");

            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetHighlightMode(HighlightMode.Upcoming);
            List<MidiNoteEvent> notes = ScanAllNotes();
            float time = NoteScan.FirstStartTimeByHand(notes, leftHand: true, fallback: _timeline.TotalDuration * 0.3f);

            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetPracticeHand(PracticeHand.LeftOnly);
            _session.Seek(time);
            _session.Play();
            yield return new WaitForSeconds(0.5f);
            yield return MeasureZoomStability(30);
            AssertZoomStable("3.2-3 左手练习");
            _session.Pause();
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0, "3.2-3：左手练习特写未在预算内收敛。" + Diag());

            Assert.AreEqual(PracticeHand.LeftOnly, _session.PracticeHand, "练习手应保持左手。" + Diag());
            AssertHandVisible(_avatar.LeftHand, "3.2-3 左手练习(活动手)");
            // 单手练习下不检查「临近琴键可见」：方案 4.2.4 的受限合并（PrimaryShare=0.7）
            // 有意让活动手优先、把远端琴键排除在取景框外，对它断言可见会与设计冲突。

            // 「相机聚焦活动手」的可量化判据：活动手（左手）应比另一只手更靠近画面中心
            var leftWorld = new List<Vector3>();
            var rightWorld = new List<Vector3>();
            if (TryCollectHandPoints(_avatar.LeftHand, leftWorld) &&
                TryCollectHandPoints(_avatar.RightHand, rightWorld) &&
                TryViewportCentroid(leftWorld, out Vector2 leftCenter) &&
                TryViewportCentroid(rightWorld, out Vector2 rightCenter))
            {
                Vector2 screenCenter = new Vector2(0.5f, 0.5f);
                float leftDistance = Vector2.Distance(leftCenter, screenCenter);
                float rightDistance = Vector2.Distance(rightCenter, screenCenter);
                Assert.LessOrEqual(leftDistance, rightDistance + 0.05f, string.Format(
                    "3.2-3：左手练习时活动手应比另一只手更靠画面中心（左手距中心 {0:F3}，右手 {1:F3}）。{2}",
                    leftDistance, rightDistance, Diag()));
            }
        }

        // ==================================================================
        // 审查 3.2 必测场景 4：只有右手有音符的片段
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene4_RightHandOnly_CameraFocusesRightHand()
        {
            RequireSong();
            if (_session.Analysis == null || _session.Analysis.RightHandCount == 0)
                Assert.Ignore("当前曲目没有右手音符（音高 ≥ C4），无法验证「只有右手有音符的片段」");

            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetHighlightMode(HighlightMode.Upcoming);
            List<MidiNoteEvent> notes = ScanAllNotes();
            float time = NoteScan.FirstStartTimeByHand(notes, leftHand: false, fallback: _timeline.TotalDuration * 0.3f);

            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetPracticeHand(PracticeHand.RightOnly);
            _session.Seek(time);
            _session.Play();
            yield return new WaitForSeconds(0.5f);
            yield return MeasureZoomStability(30);
            AssertZoomStable("3.2-4 右手练习");
            _session.Pause();
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0, "3.2-4：右手练习特写未在预算内收敛。" + Diag());

            Assert.AreEqual(PracticeHand.RightOnly, _session.PracticeHand, "练习手应保持右手。" + Diag());
            AssertHandVisible(_avatar.RightHand, "3.2-4 右手练习(活动手)");
            // 同上：右手练习下只检查活动手本身，不检查远端琴键。
            AssertActiveHandsNotUnderUi("3.2-4 右手练习");
        }

        // ==================================================================
        // 审查 3.2 必测场景 5：连续快速音阶
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene5_FastScale_NoSuddenZoom()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetHighlightMode(HighlightMode.Upcoming);
            yield return new WaitForSeconds(0.6f);

            List<MidiNoteEvent> notes = ScanAllNotes();
            Assert.Greater(notes.Count, 0, "曲目没有音符，" + Diag());
            bool found = NoteScan.TryFindDensestWindowTime(notes, 0.6f, out float time, out int count);
            Assert.IsTrue(found, "曲目内找不到音符密度可判定的片段，" + Diag());
            Debug.Log(string.Format("[CameraVisibility] 3.2-5 选中 t={0:F2}s，0.6s 窗口内 {1} 个音符起点", time, count));

            _session.Seek(time);
            _session.Play();
            yield return new WaitForSeconds(0.4f); // 先让过渡结束，避免把模式切换算成抖动
            yield return MeasureZoomStability(45);
            AssertZoomStable("3.2-5 连续快速音阶");

            AssertPracticeHandVisible("3.2-5 连续快速音阶");
            _session.Pause();
        }

        // ==================================================================
        // 审查 3.2 必测场景 6：暂停后切换视角再继续
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene6_PauseThenSwitchViewThenResume_RecomputesHandTargets()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Full);
            _session.ClearLoop();

            List<MidiNoteEvent> notes = ScanAllNotes();
            float start = NoteScan.TryFindDensestWindowTime(notes, 0.6f, out float dense, out _)
                ? dense
                : _timeline.TotalDuration * 0.4f;

            _session.Seek(start);
            _session.Play();
            yield return new WaitForSeconds(0.8f);
            _session.Pause();
            yield return new WaitForSeconds(0.2f);

            PlaybackState pausedState = _session.PlaybackState;
            float pausedTime = _timeline.CurrentTime;
            Assert.AreEqual(PlaybackState.Paused, pausedState, "前置条件：应处于暂停态。" + Diag());

            // 暂停期间连续切换视角：状态与时间都不得变化（审查 3.2 / 6.2「切换期间音乐、MIDI、琴键动作与角色动作持续同步」）
            CameraMode[] sequence = { CameraMode.Closeup, CameraMode.Full, CameraMode.Closeup, CameraMode.Full };
            for (int i = 0; i < sequence.Length; i++)
            {
                _session.SetCameraMode(sequence[i]);
                yield return null;
                yield return null;
                Assert.AreEqual(pausedState, _session.PlaybackState,
                    string.Format("3.2-6：第 {0} 次切换视角后播放状态被改变。{1}", i + 1, Diag()));
                Assert.AreEqual(pausedTime, _timeline.CurrentTime, 0.05f,
                    string.Format("3.2-6：第 {0} 次切换视角后暂停时间被推进/回退。{1}", i + 1, Diag()));
            }

            // 继续播放：时间应正常推进
            _session.Play();
            yield return new WaitForSeconds(1.0f);

            Assert.AreEqual(PlaybackState.Playing, _session.PlaybackState, "3.2-6：继续后应回到播放态。" + Diag());
            Assert.Greater(_timeline.CurrentTime, pausedTime + 0.2f,
                string.Format("3.2-6：继续后时间未正常推进（暂停于 {0:F2}s，现在 {1:F2}s）。{2}",
                    pausedTime, _timeline.CurrentTime, Diag()));

            // 按当前时间轴重建手部目标（方案 4.2「暂停/停止/拖动/循环跳回后先按当前时间轴重算手部目标」）。
            // 注意：不能断言「目标必须发生位移」——示例曲的旋律只有 0.25s、和弦 0.5s，音符之间本来就存在空档，
            // 空档期两只手都停在默认手位（此时位移为 0 是正确行为）。
            // 因此改为确定性验证：跳到「左右手同时有音符按住」的时刻，两只手的目标必须落在各自对应的琴键上。
            if (!TryFindBothHandsHeldTime(notes, out float heldTime, out float heldPlaySeconds,
                    out int heldLeftPitch, out int heldRightPitch))
            {
                Assert.Ignore("本曲目找不到「左右手同时按住音符」的片段，无法确定性验证手部目标重建。" + Diag());
            }

            _session.Seek(heldTime);
            _session.Play();
            yield return new WaitForSeconds(heldPlaySeconds);
            _session.Pause();

            Assert.IsTrue(_keyMap.TryGetKey(heldLeftPitch, out var leftKey) && leftKey != null,
                string.Format("3.2-6：左手目标音高 {0} 找不到对应琴键。{1}", heldLeftPitch, Diag()));
            Assert.IsTrue(_keyMap.TryGetKey(heldRightPitch, out var rightKey) && rightKey != null,
                string.Format("3.2-6：右手目标音高 {0} 找不到对应琴键。{1}", heldRightPitch, Diag()));

            Assert.AreEqual(leftKey.position.x, _handIK.LeftSmoothedTarget.x, 0.04f, string.Format(
                "3.2-6：按当前时间轴重建后，左手目标应落在「当前按住的最低音」琴键上（{0}，x={1:F4}），实际 x={2:F4}。{3}",
                heldLeftPitch, leftKey.position.x, _handIK.LeftSmoothedTarget.x, Diag()));
            Assert.AreEqual(rightKey.position.x, _handIK.RightSmoothedTarget.x, 0.04f, string.Format(
                "3.2-6：按当前时间轴重建后，右手目标应落在「当前按住的最高音」琴键上（{0}，x={1:F4}），实际 x={2:F4}。{3}",
                heldRightPitch, rightKey.position.x, _handIK.RightSmoothedTarget.x, Diag()));

            AssertPracticeHandVisible("3.2-6 继续后");
            _session.Pause();
        }

        // ==================================================================
        // 审查 3.2 必测场景 7：拖动进度条至曲目中段
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene7_SeekToMiddle_ConvergesWithHandsAndKeysVisible()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetHighlightMode(HighlightMode.Upcoming);
            _session.ClearLoop();

            _session.Play();
            yield return new WaitForSeconds(0.4f);

            float mid = Mathf.Clamp(_timeline.TotalDuration * 0.5f, 0f, Mathf.Max(0f, _timeline.TotalDuration - 0.5f));
            _session.Seek(mid);          // 等价于把进度条拖到中段
            _session.Pause();            // 暂停后目标静止，收敛判据才是确定性的
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0,
                string.Format("3.2-7：拖动到中段（{0:F2}s）后相机未在预算内收敛。{1}", mid, Diag()));

            AssertPracticeHandVisible("3.2-7 拖到中段");
            AssertUpcomingKeysInViewport("3.2-7 拖到中段", 6, CloseupKeyMargin);
            AssertActiveHandsNotUnderUi("3.2-7 拖到中段");
        }

        // ==================================================================
        // 审查 3.2 必测场景 8：A-B 循环边界
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene8_LoopBoundary_ReturnsToAAndConverges()
        {
            RequireSong();
            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetCameraMode(CameraMode.Closeup);
            _session.SetHighlightMode(HighlightMode.Upcoming);

            float total = _timeline.TotalDuration;
            float a = Mathf.Clamp(total * 0.3f, 0f, Mathf.Max(0f, total - 1.0f));
            float b = Mathf.Min(a + 0.8f, total);
            Assert.Greater(b - a, 0.3f, string.Format("3.2-8：曲目过短，无法建立 A-B 区间（{0:F2}s–{1:F2}s）。{2}", a, b, Diag()));

            _session.SetLoop(a, b);
            Assert.IsTrue(_session.IsLooping,
                string.Format("3.2-8：SetLoop({0:F2}, {1:F2}) 未生效。{2}", a, b, Diag()));

            float probe = a + (b - a) * 0.8f;
            _session.Seek(probe);
            _session.Play();

            // 观察到确实跨越 B 点回到 A 点附近
            bool wrapped = false;
            float wrappedAt = -1f;
            for (int i = 0; i < 600; i++)
            {
                yield return null;
                if (_timeline.CurrentTime < a + (b - a) * 0.5f)
                {
                    wrapped = true;
                    wrappedAt = _timeline.CurrentTime;
                    break;
                }
            }
            Assert.IsTrue(wrapped, string.Format(
                "3.2-8：未在预算内观察到循环跳回 A 点附近（A={0:F2}s, B={1:F2}s, probe={2:F2}s）。{3}",
                a, b, probe, Diag()));
            Assert.That(wrappedAt, Is.InRange(a - 0.25f, a + (b - a) * 0.5f), string.Format(
                "3.2-8：循环跳转落点异常（wrappedAt={0:F3}s，期望 [{1:F2}, {2:F2}]）。{3}",
                wrappedAt, a - 0.25f, a + (b - a) * 0.5f, Diag()));

            // 跳转后相机应在合理时间内收敛（不追旧目标）
            _session.Pause();
            yield return WaitCameraConverged(240);
            Assert.GreaterOrEqual(_convergeFrames, 0,
                "3.2-8：循环跳回 A 点后相机未在预算内收敛（疑似仍在追旧目标）。" + Diag());

            AssertPracticeHandVisible("3.2-8 A-B 边界");
            _session.ClearLoop();
        }

        // ==================================================================
        // 审查 3.2 必测场景 9：三种角色分别处于手部特写和全景
        // ==================================================================
        [UnityTest]
        public IEnumerator Scene9_ThreeAvatars_X_TwoViews_AllFullyFramed()
        {
            RequireSong();
            Assert.AreEqual(3, AvatarManager.AvatarCount, "本用例按「三个角色」设计，角色数量变化需同步更新");

            _session.SetPracticeHand(PracticeHand.Both);
            _session.SetHighlightMode(HighlightMode.Upcoming);
            _session.ClearLoop();

            List<MidiNoteEvent> notes = ScanAllNotes();
            float time = NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float chord, out _)
                ? chord
                : Mathf.Clamp(_timeline.TotalDuration * 0.4f, 0f, Mathf.Max(0f, _timeline.TotalDuration - 0.5f));

            for (int index = 0; index < AvatarManager.AvatarCount; index++)
            {
                _session.SwitchAvatar(index);
                yield return null;
                Assert.AreEqual(index, _avatar.CurrentIndex, "角色切换未生效。" + Diag());
                string avatarName = _avatar.CurrentName;

                // ---- 手部特写 ----
                // 必须先真的进入 Playing 再 Pause：PlaybackState.Stopped 下会话不会把琴键音高范围喂给相机
                // （LessonSessionController.UpdateHighlightUpcoming 对 Stopped 直接返回），
                // 特写取景就会退化为「只手部范围」，对「临近琴键可见」的断言失去意义。
                _session.SetCameraMode(CameraMode.Closeup);
                _session.Seek(time);
                _session.Play();
                yield return new WaitForSeconds(0.45f);
                _session.Pause();
                yield return WaitCameraConverged(300);
                Assert.GreaterOrEqual(_convergeFrames, 0, string.Format(
                    "{0}（角色 {1}）手部特写未在预算内收敛。{2}", avatarName, index, Diag()));
                AssertPracticeHandVisible(string.Format("3.2-9 {0}·特写", avatarName));
                AssertUpcomingKeysInViewport(string.Format("3.2-9 {0}·特写", avatarName), 6, CloseupKeyMargin);
                AssertActiveHandsNotUnderUi(string.Format("3.2-9 {0}·特写", avatarName));

                // ---- 全景：完整键盘 + 人物完整入画（审查 3.2「全景是否能看到完整键盘与必要的演奏姿态」） ----
                _session.SetCameraMode(CameraMode.Full);
                _session.Seek(time);
                _session.Play();
                yield return new WaitForSeconds(0.45f);
                _session.Pause();
                yield return WaitCameraConverged(300);
                Assert.GreaterOrEqual(_convergeFrames, 0, string.Format(
                    "{0}（角色 {1}）全景未在预算内收敛。{2}", avatarName, index, Diag()));
                AssertFullKeyboardVisible(string.Format("3.2-9 {0}", avatarName));
                AssertAvatarBoundsInViewport(string.Format("3.2-9 {0}", avatarName));
            }
        }
    }

    /// <summary>
    /// 曲内音符扫描辅助（纯逻辑，供 <see cref="CameraVisibilityTests"/> 与
    /// <c>VisualSnapshotTests</c> 共用）：从 <see cref="MidiEventScheduler"/> 取出曲目音符，
    /// 再挑选「和弦 / 最大跨度 / 最大跳进 / 最密集」等有代表性的时刻，
    /// 使「固定截图/用例」在**内置示例曲**上也是确定且可复现的。
    /// <para>
    /// 说明：调度器只持有**当前练习手过滤后**的音符，因此扫描前必须先设为
    /// <see cref="PracticeHand.Both"/>。
    /// </para>
    /// </summary>
    internal static class NoteScan
    {
        private const float SampleStep = 0.1f;

        /// <summary>把曲目音符平铺成一个列表（按起始时间近似升序；同一音符不会被重复收集）。</summary>
        public static List<MidiNoteEvent> CollectAllNotes(MidiEventScheduler scheduler, float totalDuration)
        {
            var all = new List<MidiNoteEvent>();
            if (scheduler == null || totalDuration <= 0f) return all;

            var buffer = new List<MidiNoteEvent>();
            int steps = Mathf.Max(1, Mathf.CeilToInt(totalDuration / SampleStep) + 1);
            for (int i = 0; i < steps; i++)
            {
                float from = i * SampleStep;
                scheduler.CollectNotesInRange(from, from + SampleStep, buffer);
                for (int k = 0; k < buffer.Count; k++) all.Add(buffer[k]);
            }
            return all;
        }

        /// <summary>找同时发声最多的中音区和弦：<paramref name="window"/> 秒内、音高落在 [lo,hi] 的音符数最大处。</summary>
        public static bool TryFindChordTime(List<MidiNoteEvent> notes, int minSimultaneous,
            int loPitch, int hiPitch, float window, out float time, out int size)
        {
            time = 0f;
            size = 0;
            if (notes == null || notes.Count == 0) return false;

            for (int i = 0; i < notes.Count; i++)
            {
                MidiNoteEvent anchor = notes[i];
                if (anchor.Pitch < loPitch || anchor.Pitch > hiPitch) continue;

                int count = 0;
                for (int k = 0; k < notes.Count; k++)
                {
                    MidiNoteEvent other = notes[k];
                    if (other.Pitch < loPitch || other.Pitch > hiPitch) continue;
                    if (Mathf.Abs(other.StartTime - anchor.StartTime) <= window) count++;
                }
                if (count > size)
                {
                    size = count;
                    time = anchor.StartTime;
                }
            }
            return size >= minSimultaneous;
        }

        /// <summary>找音域跨度最大的时刻：<paramref name="window"/> 秒内所有音符的 pitch 极差最大处。</summary>
        public static bool TryFindWidestSpanTime(List<MidiNoteEvent> notes, float window,
            out float time, out int span, out int low, out int high)
        {
            time = 0f;
            span = 0;
            low = -1;
            high = -1;
            if (notes == null || notes.Count == 0) return false;

            for (int i = 0; i < notes.Count; i++)
            {
                float center = notes[i].StartTime;
                int min = int.MaxValue;
                int max = int.MinValue;
                for (int k = 0; k < notes.Count; k++)
                {
                    MidiNoteEvent other = notes[k];
                    if (Mathf.Abs(other.StartTime - center) > window) continue;
                    if (other.Pitch < min) min = other.Pitch;
                    if (other.Pitch > max) max = other.Pitch;
                }
                if (min == int.MaxValue) continue;
                if (max - min > span)
                {
                    span = max - min;
                    time = center;
                    low = min;
                    high = max;
                }
            }
            return span > 0;
        }

        /// <summary>找相邻音符之间音程跳进最大的时刻（近似「手臂运动最明显」，审查 6.1 V03）。</summary>
        public static bool TryFindLargestLeapTime(List<MidiNoteEvent> notes, out float time, out int leap)
        {
            time = 0f;
            leap = 0;
            if (notes == null || notes.Count < 2) return false;

            for (int i = 1; i < notes.Count; i++)
            {
                int d = Mathf.Abs(notes[i].Pitch - notes[i - 1].Pitch);
                if (d > leap)
                {
                    leap = d;
                    time = notes[i].StartTime;
                }
            }
            return leap > 0;
        }

        /// <summary>找音符起点最密集的窗口（近似「连续快速音阶」，审查 3.2-5）。</summary>
        public static bool TryFindDensestWindowTime(List<MidiNoteEvent> notes, float window,
            out float time, out int count)
        {
            time = 0f;
            count = 0;
            if (notes == null || notes.Count == 0) return false;

            for (int i = 0; i < notes.Count; i++)
            {
                float center = notes[i].StartTime;
                int c = 0;
                for (int k = 0; k < notes.Count; k++)
                    if (notes[k].StartTime >= center && notes[k].StartTime < center + window) c++;
                if (c > count)
                {
                    count = c;
                    time = center;
                }
            }
            return count > 0;
        }

        /// <summary>取指定手别第一个音符的起始时间（没有该手音符时返回 <paramref name="fallback"/>）。</summary>
        public static float FirstStartTimeByHand(List<MidiNoteEvent> notes, bool leftHand, float fallback)
        {
            if (notes != null)
            {
                for (int i = 0; i < notes.Count; i++)
                {
                    if (PianoNoteFilter.IsLeftHand(notes[i].Pitch) == leftHand)
                        return notes[i].StartTime;
                }
            }
            return fallback;
        }
    }
}
