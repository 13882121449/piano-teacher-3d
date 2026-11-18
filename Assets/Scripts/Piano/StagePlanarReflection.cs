using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 琴台镜面倒影（2026-11-18，参考图二「地面倒映出演奏者和钢琴」）。
    /// <para>
    /// 一台隐藏的反射相机每帧在主相机移动之后（LateUpdate 1000）按琴台平面镜像主相机，半分辨率渲染到
    /// <c>_PT3D_PlanarReflection</c>；琴台着色器（UnderwaterScenery 的 _StageSurface 分支）按屏幕坐标取样、
    /// 加一点水纹扰动后按菲涅耳混合。
    /// </para>
    /// <para>
    /// <b>不用 GL.invertCulling</b>：镜像矩阵行列式为 −1 会把绕序翻反；再在投影里左右翻一次（行列式再乘 −1），
    /// 绕序恢复正常，着色器取样时把 u 翻回来即可。反射相机的 <c>cameraType</c> 设为 Reflection，
    /// 毛玻璃模糊等只对 Game 相机生效的渲染功能会自动跳过它。
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class StagePlanarReflection : MonoBehaviour
    {
        private static readonly int TextureId = Shader.PropertyToID("_PT3D_PlanarReflection");
        private static readonly int StrengthId = Shader.PropertyToID("_PT3D_PlanarReflectionStrength");
        private const int UiLayer = 5;

        private static StagePlanarReflection _active;

        /// <summary>反射平面的世界高度（琴台顶面）。</summary>
        public float PlaneY;

        /// <summary>反射强度（0 关闭）。</summary>
        public float Strength = 0.85f;

        /// <summary>相对主画面的分辨率比例。</summary>
        public float ResolutionScale = 0.5f;

        private Camera _camera;
        private RenderTexture _texture;

        /// <summary>手动为任意相机渲染一次倒影（截图测试在 <c>Camera.Render</c> 之前调用）。</summary>
        public static void RenderFor(Camera source)
        {
            if (_active != null && _active.isActiveAndEnabled && source != null) _active.RenderReflection(source);
        }

        private void OnEnable()
        {
            _active = this;
            Shader.SetGlobalFloat(StrengthId, 0f);
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
            Shader.SetGlobalFloat(StrengthId, 0f);
        }

        private void OnDestroy()
        {
            if (_camera != null) Destroy(_camera.gameObject);
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void LateUpdate()
        {
            var main = Camera.main;
            if (main != null) RenderReflection(main);
        }

        private void RenderReflection(Camera source)
        {
            // 无图形设备（命令行 -nographics 测试）时 Camera.Render 会让引擎原生崩溃，直接关闭倒影
            if (Strength <= 0.001f || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Shader.SetGlobalFloat(StrengthId, 0f);
                return;
            }
            int width = Mathf.Max(64, Mathf.RoundToInt(source.pixelWidth * ResolutionScale));
            int height = Mathf.Max(64, Mathf.RoundToInt(source.pixelHeight * ResolutionScale));
            EnsureResources(width, height);

            // 相机在平面下方时（例如自由视角钻到台下）不渲染倒影
            if (source.transform.position.y <= PlaneY + 0.01f)
            {
                Shader.SetGlobalFloat(StrengthId, 0f);
                return;
            }

            var plane = new Vector4(0f, 1f, 0f, -PlaneY);
            Matrix4x4 mirror = ReflectionMatrix(plane);
            _camera.CopyFrom(source);
            _camera.cameraType = CameraType.Reflection;
            _camera.enabled = false;
            _camera.targetTexture = _texture;
            _camera.cullingMask = source.cullingMask & ~(1 << UiLayer);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = source.backgroundColor;

            Vector3 position = mirror.MultiplyPoint(source.transform.position);
            Vector3 forward = mirror.MultiplyVector(source.transform.forward);
            Vector3 up = mirror.MultiplyVector(source.transform.up);
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            _camera.worldToCameraMatrix = source.worldToCameraMatrix * mirror;

            // 斜裁剪：近裁剪面换成反射平面，台面以下的东西不进倒影
            Vector4 clip = CameraSpacePlane(_camera.worldToCameraMatrix, new Vector3(0f, PlaneY, 0f), Vector3.up, 0.02f);
            Matrix4x4 projection = source.CalculateObliqueMatrix(clip);
            _camera.projectionMatrix = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * projection;

            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.renderType = CameraRenderType.Base;

            _camera.Render();
            Shader.SetGlobalTexture(TextureId, _texture);
            Shader.SetGlobalFloat(StrengthId, Strength);
        }

        private void EnsureResources(int width, int height)
        {
            if (_camera == null)
            {
                var go = new GameObject("StageReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                go.AddComponent<UniversalAdditionalCameraData>();
            }
            if (_texture != null && (_texture.width != width || _texture.height != height))
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
            }
            if (_texture == null)
            {
                _texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "StagePlanarReflection",
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _texture.Create();
            }
        }

        private static Matrix4x4 ReflectionMatrix(Vector4 p)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y; m.m02 = -2f * p.x * p.z; m.m03 = -2f * p.w * p.x;
            m.m10 = -2f * p.y * p.x; m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z; m.m13 = -2f * p.w * p.y;
            m.m20 = -2f * p.z * p.x; m.m21 = -2f * p.z * p.y; m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
            return m;
        }

        private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 point, Vector3 normal, float offset)
        {
            Vector3 p = worldToCamera.MultiplyPoint(point + normal * offset);
            Vector3 n = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
        }
    }
}
