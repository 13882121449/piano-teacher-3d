using UnityEngine;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 主菜单的实时 3D 背景（2026-11-18 精修）：课程同款的海底舞台与舞台三角琴，镜头缓慢环绕。
    /// <para>
    /// 毛玻璃界面需要「背后真的有画面」才成立：这里复用课程场景的 <see cref="PianoKeyMap"/> 与
    /// <see cref="PianoSceneEnvironment"/>，主菜单和课程看到的是同一座海底舞台。
    /// 「减少动效」打开时镜头停在构图位置，不再漂移（舞台里的海洋生物仍由舞台自身控制）。
    /// </para>
    /// <para>运行期无分配：Update 只做几次三角函数和两次 LookAt。</para>
    /// </summary>
    public sealed class MenuStageBackdrop : MonoBehaviour
    {
        // 镜头：从演奏侧右前方看向琴身与琴台，海底远景在琴后
        private static readonly Vector3 Focus = new Vector3(0.05f, 0.80f, -0.55f);
        private const float Distance = 4.6f;
        private const float BaseYaw = 34f;          // 绕 Y 轴，0 = 正对键盘（+Z 方向）
        private const float BasePitch = 13f;
        private const float YawSwing = 8f;
        private const float PitchSwing = 1.5f;
        private const float DriftSpeed = 0.045f;    // 一个来回约 140 秒
        private const float FieldOfView = 38f;
        private const float AimShift = 1.05f;       // 取景中心左移：钢琴落在画面右侧，左侧留给标题卡

        private Camera _camera;
        private PianoSceneEnvironment _environment;
        private float _time;
        private bool _reducedMotion;

        /// <summary>减少动效是否生效（镜头静止）。</summary>
        public bool IsMotionReduced => _reducedMotion;

        /// <summary>舞台环境（测试与诊断用）。</summary>
        public PianoSceneEnvironment Environment => _environment;

        /// <summary>搭建背景。场景里没有可用相机时返回 null，主菜单退回静态背景图。</summary>
        internal static MenuStageBackdrop Create(QualityTier tier)
        {
            var go = new GameObject("MenuStageBackdrop");
            var backdrop = go.AddComponent<MenuStageBackdrop>();
            if (backdrop.Build(tier)) return backdrop;
            Destroy(go);
            return null;
        }

        private bool Build(QualityTier tier)
        {
            _camera = Camera.main;
            if (_camera == null) return false;

            var piano = new GameObject("Piano");
            piano.transform.SetParent(transform, false);
            var keyMap = piano.AddComponent<PianoKeyMap>();
            keyMap.BuildKeys();
            piano.transform.localPosition = new Vector3(-keyMap.TotalWidth * 0.5f, 0f, 0f);

            _environment = gameObject.AddComponent<PianoSceneEnvironment>();
            _environment.Build(keyMap, QualityProfile.For(tier));
            keyMap.SetUnderwaterAppearance(true);

            // 与课程场景（LessonSessionController 第 9 段）一致的背景与三色环境光
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = UITheme.SceneBg;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.53f, 0.61f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.45f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.28f, 0.31f);
            ScenePostProcessing.Ensure(_camera, transform);

            _camera.fieldOfView = FieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 80f;
            PlaceCamera(0f);
            return true;
        }

        internal void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (reduced) PlaceCamera(0f);
        }

        private void Update()
        {
            if (_reducedMotion || _camera == null) return;
            _time += Time.unscaledDeltaTime;
            PlaceCamera(_time);
        }

        private void PlaceCamera(float time)
        {
            float phase = time * DriftSpeed * Mathf.PI * 2f;
            float yaw = (BaseYaw + Mathf.Sin(phase) * YawSwing) * Mathf.Deg2Rad;
            float pitch = (BasePitch + Mathf.Sin(phase * 0.5f + 1.3f) * PitchSwing) * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
            var t = _camera.transform;
            t.position = Focus + offset * Distance;
            t.LookAt(Focus, Vector3.up);
            t.LookAt(Focus - t.right * AimShift, Vector3.up);
        }
    }
}
