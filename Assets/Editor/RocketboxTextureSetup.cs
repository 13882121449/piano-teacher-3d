using UnityEditor;
using UnityEngine;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// Rocketbox 贴图导入配置：把「贴图类型 / sRGB / 压缩 / Alpha」设成正确值。
    ///
    /// <para>
    /// 为什么需要这个脚本：Unity 对 TGA 的默认导入是「Default 类型 + sRGB 开 + 不压缩」。
    /// 对 color 贴图正确，但对法线贴图是<b>错误</b>的——法线是方向数据不是颜色，
    /// 走 sRGB 解码会让法线整体偏亮（相当于法线朝上偏），皮肤会出现「发灰发平」的塑料感；
    /// 不标 NormalMap 类型则 Unity 不会做正确的法线解包。
    /// </para>
    ///
    /// <para>
    /// 幂等：配置与目标一致时跳过，不触发重导入。
    /// 用法：<c>Unity.exe -batchmode -executeMethod PianoTeacher3D.EditorTools.RocketboxTextureSetup.Run</c>
    /// </para>
    /// </summary>
    public static class RocketboxTextureSetup
    {
        private const string Dir = "Assets/Characters/Rocketbox/Resources/Characters/Rocketbox";

        private struct Rule
        {
            public string Suffix;
            public TextureImporterType Type;
            public bool Srgb;
            public bool AlphaIsTransparency;
            public TextureImporterCompression Compression;
            /// <summary>非 2 次幂缩放策略。None = 保持原始像素比例（缩略图必须用它，见 _portrait 规则）</summary>
            public TextureImporterNPOTScale NPOT;
            /// <summary>最大尺寸；≤0 表示沿用脚本里的 1024 默认值</summary>
            public int MaxSize;
        }

        private static readonly Rule[] Rules =
        {
            // 颜色贴图：sRGB 必须开（它在 sRGB 空间里），压缩用 BC7（带 alpha 的好质量）
            new Rule { Suffix = "_color", Type = TextureImporterType.Default, Srgb = true,
                       AlphaIsTransparency = false, Compression = TextureImporterCompression.CompressedHQ },
            // 法线：NormalMap 类型 + sRGB 关，压缩 BC5（两通道，质量远好于 DXT5）
            new Rule { Suffix = "_normal", Type = TextureImporterType.NormalMap, Srgb = false,
                       AlphaIsTransparency = false, Compression = TextureImporterCompression.CompressedHQ },
            // specular：线性数据，sRGB 关（本期不绑定到材质，但保持配置正确以备后续使用）
            new Rule { Suffix = "_specular", Type = TextureImporterType.Default, Srgb = false,
                       AlphaIsTransparency = false, Compression = TextureImporterCompression.Compressed },
            // 人像缩略图：颜色数据，但**必须关掉 NPOT 缩放**。
            // 源图 384×512（3:4），Unity 默认 NPOT=ToNearest 会把它拉伸成 512×512，
            // 脸被横向拉宽三分之一——角色库卡片上看起来像「胖了」。
            new Rule { Suffix = "_portrait", Type = TextureImporterType.Default, Srgb = true,
                       AlphaIsTransparency = false, Compression = TextureImporterCompression.CompressedHQ,
                       NPOT = TextureImporterNPOTScale.None, MaxSize = 512 },
        };

        [MenuItem("Tools/PianoTeacher3D/Rocketbox 贴图导入配置")]
        public static void Run()
        {
            int changed = 0;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Dir });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);

                Rule? rule = null;
                foreach (var r in Rules)
                {
                    if (name.EndsWith(r.Suffix, System.StringComparison.OrdinalIgnoreCase)) { rule = r; break; }
                }
                // opacity 单独处理：它是带 alpha 的颜色贴图
                bool isOpacity = name.IndexOf("opacity", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isOpacity)
                {
                    rule = new Rule
                    {
                        Suffix = "_opacity",
                        Type = TextureImporterType.Default,
                        Srgb = true,
                        AlphaIsTransparency = true,
                        Compression = TextureImporterCompression.CompressedHQ
                    };
                }
                if (rule == null) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool dirty = false;
                if (importer.textureType != rule.Value.Type) { importer.textureType = rule.Value.Type; dirty = true; }
                if (importer.sRGBTexture != rule.Value.Srgb) { importer.sRGBTexture = rule.Value.Srgb; dirty = true; }
                if (importer.alphaIsTransparency != rule.Value.AlphaIsTransparency)
                {
                    importer.alphaIsTransparency = rule.Value.AlphaIsTransparency; dirty = true;
                }
                if (importer.textureCompression != rule.Value.Compression)
                {
                    importer.textureCompression = rule.Value.Compression; dirty = true;
                }
                if (importer.mipmapEnabled != true) { importer.mipmapEnabled = true; dirty = true; }

                // 非 2 次幂：缩略图必须保留原始比例，否则 384×512 会被拉伸成 512×512
                if (importer.npotScale != rule.Value.NPOT) { importer.npotScale = rule.Value.NPOT; dirty = true; }

                // 模型贴图 1024 已是我们降采样后的尺寸，显式锁住防止后续被误设更大；
                // 缩略图按规则单独给 512。
                int maxSize = rule.Value.MaxSize > 0 ? rule.Value.MaxSize : 1024;
                if (importer.maxTextureSize != maxSize) { importer.maxTextureSize = maxSize; dirty = true; }

                if (dirty)
                {
                    importer.SaveAndReimport();
                    changed++;
                    Debug.Log($"[RocketboxTextureSetup] 已配置 {name}：type={rule.Value.Type} sRGB={rule.Value.Srgb} " +
                              $"压缩={rule.Value.Compression} alphaIsTransparency={rule.Value.AlphaIsTransparency}");
                }
            }
            Debug.Log($"[RocketboxTextureSetup] 完成，修改 {changed} 张（共扫描 {guids.Length} 张）。");
        }
    }
}
