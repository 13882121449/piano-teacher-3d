using System;
using System.Collections.Generic;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>只修改当前角色的外观副本，双手和踏板仍由样板自带的骨架驱动。</summary>
    internal static class PhotoAvatarAppearance
    {
        private static readonly Color[] Garment =
        {
            new Color(.86f, 1.00f, 1.18f), new Color(1.32f, .69f, .77f), new Color(.66f, 1.22f, .99f)
        };
        private static readonly float[] Tone = { 1f, .83f, 1.09f };

        public static bool Apply(GameObject root, Transform head, PhotoAvatarData data, CharacterResourceScope scope,
            out string error)
        {
            error = null;
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            scope.Own(atlas);
            try
            {
                var bytes = Convert.FromBase64String(data.FittedAtlas);
                if (!IsGeneratedPng(bytes) || !atlas.LoadImage(bytes) ||
                    atlas.width != PhotoAvatarFitter.AtlasSize || atlas.height != PhotoAvatarFitter.AtlasSize)
                {
                    error = "脸部贴图损坏，请从角色库创建一份新角色。";
                    return false;
                }

                var painted = new Dictionary<Texture2D, Texture2D>();
                bool headUpdated = false;
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    var skinParts = new List<int>();
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (material == null) continue;
                        var semantic = RocketboxTextureLibrary.ClassifySemantic(material.name);
                        bool isHead = semantic == "head";
                        if (!isHead && semantic != "body") continue;
                        string property = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                        if (!material.HasProperty(property) ||
                            !(material.GetTexture(property) is Texture2D original) || !original.isReadable) continue;
                        if (!painted.TryGetValue(original, out var merged))
                        {
                            merged = Compose(original, atlas, data, isHead);
                            scope.Own(merged);
                            painted.Add(original, merged);
                        }
                        material.SetTexture(property, merged);
                        if (!isHead) continue;
                        if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", .56f);
                        headUpdated = true;
                        skinParts.Add(i);
                    }
                    if (skinParts.Count > 0 && head != null && renderer.sharedMesh != null &&
                        renderer.sharedMesh.isReadable)
                        ShapeFace(root.transform, head, renderer, skinParts, data.FaceScale, scope);
                }
                if (headUpdated) return true;
                error = "角色样板缺少可替换的脸部材质。";
                return false;
            }
            catch (Exception e) when (e is UnityException || e is FormatException || e is ArgumentException)
            {
                error = "无法读取拟合角色外观，请返回角色库重新创建。";
                return false;
            }
        }

        private static bool IsGeneratedPng(byte[] bytes)
        {
            if (bytes.Length < 24 || PhotoInputValidator.SniffSignature(bytes) != PhotoFormat.Png) return false;
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return width == PhotoAvatarFitter.AtlasSize && height == PhotoAvatarFitter.AtlasSize;
        }

        private static Texture2D Compose(Texture2D original, Texture2D atlas, PhotoAvatarData data, bool isHead)
        {
            int size = PhotoAvatarFitter.AtlasSize;
            var pixels = new Color32[size * size];
            var source = original.GetPixels32();
            var face = isHead ? atlas.GetPixels32() : null;
            for (int y = 0; y < size; y++)
            {
                int row = (y * original.height / size) * original.width;
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    Color color = source[row + x * original.width / size];
                    float alpha = color.a;
                    if (isHead)
                    {
                        Color layer = face[index];
                        color = Color.Lerp(color, layer, layer.a);
                    }
                    bool skin = color.r > color.g * 1.10f && color.g > color.b * 1.04f && color.r > .37f;
                    if (skin) color *= Tone[data.Complexion];
                    else if (!isHead) color *= Garment[data.Outfit];
                    color.a = alpha;
                    pixels[index] = color;
                }
            }
            var result = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = isHead ? "照片拟合脸部" : "照片角色演出服",
                filterMode = FilterMode.Trilinear,
                wrapMode = original.wrapMode
            };
            result.SetPixels32(pixels);
            result.Apply(true, true);
            return result;
        }

        private static void ShapeFace(Transform root, Transform head, SkinnedMeshRenderer renderer,
            List<int> skinParts, Vector2 scale, CharacterResourceScope scope)
        {
            var source = renderer.sharedMesh;
            var uv = source.uv;
            var vertices = source.vertices;
            if (uv.Length != vertices.Length) return;
            var visited = new bool[vertices.Length];
            foreach (int part in skinParts)
            {
                if (part >= source.subMeshCount) continue;
                foreach (int index in source.GetIndices(part))
                {
                    if (visited[index]) continue;
                    visited[index] = true;
                    var coord = uv[index];
                    // 只在脸颊、下颌附近修改顶点。眼球、嘴内、头发与颈部接缝保持样板位置。
                    float across = Mathf.Clamp01(1f - Mathf.Abs(coord.x - .5f) / .21f);
                    float down = Mathf.Clamp01(1f - Mathf.Abs(coord.y - .59f) / .11f);
                    float weight = Mathf.SmoothStep(0f, 1f, across * down);
                    if (weight <= .001f) continue;
                    Vector3 inWorld = renderer.transform.TransformPoint(vertices[index]);
                    Vector3 delta = inWorld - head.position;
                    inWorld += root.right * (Vector3.Dot(delta, root.right) * (scale.x - 1f) * weight);
                    inWorld += root.up * (Vector3.Dot(delta, root.up) * (scale.y - 1f) * weight);
                    vertices[index] = renderer.transform.InverseTransformPoint(inWorld);
                }
            }
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "照片角色脸型";
            scope.Own(mesh);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            renderer.sharedMesh = mesh;
        }
    }
}
