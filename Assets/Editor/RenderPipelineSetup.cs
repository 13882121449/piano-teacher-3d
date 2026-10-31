using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// 写实化批次 2：URP 管线资产一键生成（<c>-executeMethod</c> 入口，幂等可重跑）。
    /// <para>
    /// 职责：① 创建 Renderer Data（含 SSAO Renderer Feature）；② 创建 Standard / High 两档
    /// Pipeline Asset 并关联 Renderer；③ 色彩空间切 Linear（一次性，触发资产重导入）；
    /// ④ 按设备建议把对应档位赋给 <c>GraphicsSettings.defaultRenderPipelineAsset</c>。
    /// 与 <c>RocketboxAvatarSetup</c> 同一套 <c>-executeMethod</c> 模式：batchmode 可跑、失败非零退出。
    /// </para>
    /// <para>
    /// 用法：<c>Unity.exe -batchmode -nographics -projectPath &lt;项目&gt; -executeMethod
    /// PianoTeacher3D.EditorTools.RenderPipelineSetup.Setup -quit -logFile &lt;日志&gt;</c>
    /// </para>
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string RendererPath = SettingsFolder + "/PianoTeacher3D-URP-Renderer.asset";
        private const string HighPath = SettingsFolder + "/PianoTeacher3D-URP-High.asset";
        private const string StandardPath = SettingsFolder + "/PianoTeacher3D-URP-Standard.asset";

        /// <summary>命令行入口：幂等，重跑仅在缺失时补建资产。</summary>
        public static void Setup()
        {
            try
            {
                EnsureAssets();
                EnsureLinearColorSpace();
                AssignDefaultPipeline();
                Debug.Log("[RenderPipelineSetup] URP 管线资产就绪（Renderer + Standard/High 两档 + Linear 色彩空间）");
            }
            catch (Exception ex)
            {
                // 命令行可见性：非零退出码 + 详细错误，由调用方（verify / 人工）判断
                Debug.LogError("[RenderPipelineSetup] 失败：" + ex);
                EditorApplication.Exit(3);
            }
        }

        // ==================== 资产生成 ====================

        private static void EnsureAssets()
        {
            Directory.CreateDirectory(SettingsFolder);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                Debug.Log("[RenderPipelineSetup] 已创建 Renderer Data：" + RendererPath);
            }
            EnsureSSAOFeature(renderer);

            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighPath) == null)
            {
                var high = CreatePipelineAsset(renderer, high: true);
                AssetDatabase.CreateAsset(high, HighPath);
                Debug.Log("[RenderPipelineSetup] 已创建 High 档：" + HighPath);
            }

            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(StandardPath) == null)
            {
                var standard = CreatePipelineAsset(renderer, high: false);
                AssetDatabase.CreateAsset(standard, StandardPath);
                Debug.Log("[RenderPipelineSetup] 已创建 Standard 档：" + StandardPath);
            }

            AssetDatabase.SaveAssets();
        }

        private static UniversalRenderPipelineAsset CreatePipelineAsset(UniversalRendererData renderer, bool high)
        {
            var asset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();

            // m_RendererDataList / m_DefaultRendererIndex 为 internal 且无公开创建 API，
            // 反射关联是 URP 工具链的标准做法（官方模板同样按此结构序列化）。
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var indexField = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (listField == null || indexField == null)
                throw new InvalidOperationException("URP 内部结构变化：找不到 m_RendererDataList / m_DefaultRendererIndex（版本不兼容）");
            listField.SetValue(asset, new ScriptableRendererData[] { renderer });
            indexField.SetValue(asset, 0);

            // High 档（1080p 目标 60fps）：阴影 60m / 4 级联 / MSAA 4x；Standard（集显/旧机）：30m / 2 级联 / MSAA 2x
            // （对应 QualityProfile.DetectRecommended 的两档语义；HDR 两档都开——ACES 后处理需要）
            // 主光/补光渲染模式保持 URP 默认（PerUI PerPixel）；URP 14 的对应属性为只读，不可显式赋值。
            asset.shadowDistance = high ? 60f : 30f;
            asset.msaaSampleCount = high ? 4 : 2;
            asset.shadowCascadeCount = high ? 4 : 2;
            asset.supportsHDR = true;
            return asset;
        }

        private static void EnsureSSAOFeature(UniversalRendererData renderer)
        {
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                // 14.0.10 的 SSAO 类型是 internal，不能用于 is 模式匹配——按类型名比较做幂等检查
                if (renderer.rendererFeatures[i] != null &&
                    renderer.rendererFeatures[i].GetType().Name == "ScreenSpaceAmbientOcclusion") return;
            }

            // URP 14.0.10 的 SSAO Renderer Feature 是 internal 类型（14.0.11+ 才公开为 SSAORendererFeature），
            // 只能反射创建；默认参数已可用（intensity/radius 由美术调优阶段再定）。
            var ssaoType = typeof(ScriptableRendererFeature).Assembly.GetType(
                "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", throwOnError: false);
            if (ssaoType == null)
                throw new InvalidOperationException("URP 内部结构变化：找不到 internal ScreenSpaceAmbientOcclusion（版本不兼容，需改用公开 API）");

            var ssao = (ScriptableRendererFeature)Activator.CreateInstance(ssaoType, nonPublic: true);
            renderer.rendererFeatures.Add(ssao);
            // Renderer Feature 必须作为 Renderer Data 的子资产序列化，否则重载后丢失
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            EditorUtility.SetDirty(renderer);
            Debug.Log("[RenderPipelineSetup] 已添加 SSAO Renderer Feature（internal 类型，经反射创建）");
        }

        // ==================== 色彩空间与默认管线 ====================

        private static void EnsureLinearColorSpace()
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear) return;   // 幂等：同值不触发重导入
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // 注意：Gamma→Linear 会使全部贴图资产重导入（一次性成本），并改变全部颜色观感——
            // 这是有意为之（写实化的渲染地基），视觉基线重采安排在批次 7。
            Debug.Log("[RenderPipelineSetup] 色彩空间已切换为 Linear（全部资产将重导入一次）");
        }

        private static void AssignDefaultPipeline()
        {
            // 默认档按设备建议（编辑器开发机通常为 High；Player 上由 QualityProfile 运行时按档调制当前 asset 属性）
            var tier = QualityProfile.DetectRecommended();
            string path = tier == QualityTier.High ? HighPath : StandardPath;
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
                throw new InvalidOperationException("URP 资产创建失败，无法赋默认管线：" + path);

            // 默认管线赋值走 runtime 的 GraphicsSettings.defaultRenderPipelineAsset（get/set 均公开；
            // UnityEditor 侧没有同名类型，CS0234 已实测确认）。幂等：已是同一实例则跳过。
            // 2022.3 的正确 API 是 defaultRenderPipeline（defaultRenderPipelineAsset 是 2023+ 改名，
            // 本机 UnityEditor/UnityEngine 各模块均无该成员——已实测确认）
            if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != asset)
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = asset;
                AssetDatabase.SaveAssets();
                Debug.Log($"[RenderPipelineSetup] 默认渲染管线已指向 {path}（档位 {tier}）");
            }
        }
    }
}
