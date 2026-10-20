using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 「UI 不错位」与「特写不遮挡键盘」的量化门禁。
    ///
    /// <para>
    /// 用户反馈里这两条都是观感描述（"错位""挡住"），靠肉眼每次判断不可复现。
    /// 这里把它们翻成两条可执行的几何断言：
    /// ① 所有交互控件的外接矩形必须完整落在画布内（越界即"错位/被裁"）；
    /// ② 相机到键盘任一采样点的视线，到角色头部的距离必须大于头半径（否则就是头部挡键盘）。
    /// </para>
    /// </summary>
    public class CloseupOcclusionTests
    {
        private LessonSessionController _session;
        private Camera _camera;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            Assert.IsNotNull(_session);
            _camera = Camera.main;
            Assert.IsNotNull(_camera, "场景应有主相机");
        }

        [UnityTest]
        public IEnumerator Closeup_KeyboardIsNotOccludedByHead()
        {
            _session.SetCameraMode(CameraMode.Closeup);
            for (int i = 0; i < 5; i++) yield return null;

            var view = _session.CurrentCharacterView;
            Assert.IsNotNull(view);
            var animator = view.Animator;
            Assert.IsNotNull(animator, "当前角色应有 Animator");

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Assert.IsNotNull(head, "应能取到头部骨骼");

            // 头部遮挡体积：骨骼点在颈根附近，向上偏移到头心，半径取常见人头半宽
            Vector3 headCenter = head.position + Vector3.up * 0.06f;
            const float headRadius = 0.11f;

            var points = KeyboardSamplePoints();
            Assert.Greater(points.Count, 0, "应能取到键盘采样点");

            var camPos = _camera.transform.position;
            var occluded = new List<string>();

            for (int i = 0; i < points.Count; i++)
            {
                float d = DistancePointToSegment(headCenter, camPos, points[i]);
                if (d < headRadius)
                {
                    occluded.Add($"{points[i].ToString("F2")} 距离头部 {d * 100f:0.#} cm");
                }
            }

            Assert.IsEmpty(occluded,
                "特写时头部挡住了键盘采样点：" + string.Join("；", occluded) +
                $"。相机={camPos.ToString("F2")}，头部={headCenter.ToString("F2")}" +
                $"（相机需抬高/前移或加大俯角）");
        }

        [UnityTest]
        public IEnumerator FullView_KeyboardIsNotOccludedByHead()
        {
            _session.SetCameraMode(CameraMode.Full);
            for (int i = 0; i < 5; i++) yield return null;

            var animator = _session.CurrentCharacterView.Animator;
            Transform head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) Assert.Ignore("当前角色没有 Humanoid 头部骨骼，跳过");

            Vector3 headCenter = head.position + Vector3.up * 0.06f;
            var camPos = _camera.transform.position;
            var points = KeyboardSamplePoints();

            for (int i = 0; i < points.Count; i++)
            {
                float d = DistancePointToSegment(headCenter, camPos, points[i]);
                Assert.Greater(d, 0.11f,
                    $"全景下头部挡住了键盘采样点 {points[i].ToString("F2")}（距离 {d * 100f:0.#} cm）");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator UiControls_StayInsideCanvas_InBothCameraModes()
        {
            CameraMode[] modes = { CameraMode.Full, CameraMode.Closeup };
            var offenders = new List<string>();

            for (int m = 0; m < modes.Length; m++)
            {
                _session.SetCameraMode(modes[m]);
                for (int i = 0; i < 4; i++) yield return null;

                var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                for (int c = 0; c < canvases.Length; c++)
                {
                    var canvas = canvases[c];
                    if (canvas == null || !canvas.isActiveAndEnabled) continue;
                    var canvasRect = canvas.GetComponent<RectTransform>().rect;

                    var buttons = canvas.GetComponentsInChildren<Button>(true);
                    for (int b = 0; b < buttons.Length; b++)
                    {
                        var btn = buttons[b];
                        if (btn == null || !btn.isActiveAndEnabled) continue;

                        var rt = btn.GetComponent<RectTransform>();
                        var corners = new Vector3[4];
                        rt.GetWorldCorners(corners);

                        float minX = float.MaxValue, maxX = float.MinValue;
                        float minY = float.MaxValue, maxY = float.MinValue;
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 local = canvas.transform.InverseTransformPoint(corners[k]);
                            minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                            minY = Mathf.Min(minY, local.y); maxY = Mathf.Max(maxY, local.y);
                        }

                        const float tol = 1.5f;
                        bool inside = minX >= canvasRect.xMin - tol && maxX <= canvasRect.xMax + tol
                                      && minY >= canvasRect.yMin - tol && maxY <= canvasRect.yMax + tol;
                        if (!inside)
                        {
                            offenders.Add($"{modes[m]}/{canvas.name}/{btn.name} " +
                                          $"x[{minX:F0},{maxX:F0}] y[{minY:F0},{maxY:F0}] " +
                                          $"canvas x[{canvasRect.xMin:F0},{canvasRect.xMax:F0}] " +
                                          $"y[{canvasRect.yMin:F0},{canvasRect.yMax:F0}]");
                        }
                    }
                }
            }

            Assert.IsEmpty(offenders, "以下控件超出画布（会被裁切/错位）：\n" + string.Join("\n", offenders));
        }

        /// <summary>键盘采样点：白键顶面中线上取左 / 中 / 右若干点（键面 y 由 PianoKeyMap 提供）。</summary>
        private static List<Vector3> KeyboardSamplePoints()
        {
            var result = new List<Vector3>();
            var maps = Object.FindObjectsByType<PianoKeyMap>(FindObjectsSortMode.None);
            if (maps.Length == 0) return result;

            var map = maps[0];
            Transform t = map.transform;
            float y = map.KeySurfaceY;
            float half = map.TotalWidth * 0.5f;

            // 以钢琴本地坐标取点：中线 z=0 略靠前（白键长度的一半）
            float z = -map.WhiteLength * 0.35f;
            float[] xs = { -half * 0.75f, -half * 0.35f, 0f, half * 0.35f, half * 0.75f };
            for (int i = 0; i < xs.Length; i++)
                result.Add(t.TransformPoint(new Vector3(xs[i], y, z)));

            return result;
        }

        private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            Vector3 closest = a + ab * t;
            return Vector3.Distance(p, closest);
        }
    }
}
