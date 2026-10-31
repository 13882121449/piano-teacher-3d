using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// Rocketbox 贴图库：按材质语义从 Resources 绑定颜色与法线贴图。
    ///
    /// <para>
    /// <b>为什么必须在运行时绑定</b>：FBX 内的贴图引用是
    /// <c>..\..\..\..\..\temp\Humans\with_opacity_version\f001\textures\f001_body_color.tga</c>
    /// 这种指向建模机本地临时目录的相对路径，Unity 导入时<b>永远解析不到</b>。
    /// 结果是所有导入材质的 <c>_MainTex</c> 为 null，角色渲染成白模 —— 资产门禁
    /// <see cref="CharacterAssetValidator.ValidateMaterials"/> 据此拒绝该角色并回落内置角色。
    /// 补齐贴图文件本身不解决问题（导入器找不到引用），必须由代码重新绑定。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不绑 specular 贴图</b>：Rocketbox 的 specular 是传统工作流（RGB = 高光色，
    /// A = 光泽度），而 Standard / URP-Lit 用的是 Metallic-Gloss（R = 金属度，A = 光泽度）。
    /// 直接把 specular 塞进 <c>_MetallicGlossMap</c> 会让皮肤的 R 通道被当成金属度，
    /// 整个人变成磨砂金属。皮肤是各向同性电介质，固定 <see cref="SkinSmoothness"/> 比错误映射更真实。
    /// </para>
    /// </summary>
    public static class RocketboxTextureLibrary
    {
        private const string ResourceRoot = "Characters/Rocketbox/";

        /// <summary>皮肤光泽度：来自 specular 贴图 A 通道的均值估算，硬编码以便跨平台一致。</summary>
        public const float SkinSmoothness = 0.32f;

        private static bool _warned;

        /// <summary>把贴图绑定到材质（幂等：已有主贴图时不覆盖，便于美术在 Inspector 覆写）。</summary>
        public static void Bind(Material mat, string materialName)
        {
            if (mat == null) return;

            var semantic = ClassifySemantic(materialName);
            var color = Load($"{ResourceRoot}f001_{semantic}_color");
            var normal = Load($"{ResourceRoot}f001_{semantic}_normal");

            // 颜色：内置 Standard 用 _MainTex，URP/Lit 用 _BaseMap，两者都设（不存在的属性 SetTexture 是安全的空操作）
            if (color != null)
            {
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", color);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", color);
                if (mat.HasProperty("_BaseColor") && mat.GetTexture("_BaseMap") == color)
                {
                    // 贴图存在时基色必须归白，否则贴图会被材质色整体压暗（新材质默认白，此处只防人为改过）
                    mat.SetColor("_BaseColor", Color.white);
                }
                if (mat.HasProperty("_Color") && mat.GetTexture("_MainTex") == color) mat.SetColor("_Color", Color.white);
            }

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
            }

            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", SkinSmoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", SkinSmoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            // 透明层（睫毛/眉毛/发片）：开 alpha clip，避免整片方形不透明
            if (semantic == "opacity")
            {
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
        }

        /// <summary>材质名 → 贴图语义。匹配不到时按 body 处理（body 是面积最大、兜底最安全的）。</summary>
        private static string ClassifySemantic(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return "body";
            var n = materialName.ToLowerInvariant();

            // 先判透明层：它同时可能含 "head" 字样（头部发片/睫毛），优先级必须最高
            if (n.Contains("opacity") || n.Contains("hair") || n.Contains("lash")
                || n.Contains("brow") || n.Contains("eye")) return "opacity";

            if (n.Contains("head")) return "head";
            return "body";
        }

        private static Texture2D Load(string path)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning($"[RocketboxTextureLibrary] 找不到贴图 Resources/{path}，" +
                                 "角色将以纯色渲染（请确认贴图位于 Assets/Characters/Rocketbox/Resources/ 下）。");
            }
            return tex;
        }
    }
}
