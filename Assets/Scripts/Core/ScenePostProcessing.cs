using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 运行时构建全局 Volume（Bloom / Vignette / Color Adjustments），并给主相机打开后处理。
    ///
    /// 为什么在代码里建而不是存成 asset：整个场景本来就是装配期程序化生成的
    /// （<see cref="LessonSessionController.BuildScene"/>），多一个二进制 profile 资产反而
    /// 让「场景长什么样」分散到看不见的地方；这里的三个数值也足够小，直接写成常量更易读。
    ///
    /// 只在装配期调用一次；运行期由 URP 内部每帧求值，这里不再有托管分配。
    /// </summary>
    public static class ScenePostProcessing
    {
        /// <summary>Bloom 阈值：低于它的亮度不参与泛光，避免白键面整体发灰。</summary>
        private const float BloomThreshold = 0.92f;

        /// <summary>Bloom 强度：只给黄铜踏板、琴键高光与窗外冷光一点溢出，不做「发光滤镜」。</summary>
        private const float BloomIntensity = 0.16f;

        /// <summary>暗角强度：把视线收拢到键盘区域，同时压掉画面四角的空墙。</summary>
        private const float VignetteIntensity = 0.18f;

        /// <summary>
        /// 建全局 Volume 并返回它的 profile。相机为空时也会建（音量本身与相机无关），
        /// 只是没有相机可用时后处理不会显示。
        /// </summary>
        public static VolumeProfile Ensure(Camera camera, Transform parent, bool bloomEnabled = true)
        {
            if (camera != null)
            {
                var cameraData = camera.GetUniversalAdditionalCameraData();
                if (cameraData != null) cameraData.renderPostProcessing = true;
            }

            var go = new GameObject("GlobalVolume");
            if (parent != null) go.transform.SetParent(parent, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            if (bloomEnabled)
            {
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(BloomThreshold);
                bloom.intensity.Override(BloomIntensity);
                bloom.scatter.Override(0.62f);
                // 低配档位下 Bloom 走廉价路径：不做高质量过滤，也不叠多级 mip
                bloom.highQualityFiltering.Override(false);
            }

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.06f, 0.05f, 0.04f));

            var adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.postExposure.Override(0.05f);
            adjustments.contrast.Override(9f);
            adjustments.saturation.Override(3f);
            // 暖色滤镜：暖木 UI 与琴房的木质色调要一致，否则 UI 与场景像两套皮肤
            adjustments.colorFilter.Override(new Color(1f, 0.97f, 0.93f));

            return profile;
        }
    }
}
