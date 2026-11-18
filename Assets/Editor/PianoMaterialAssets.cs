#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// 生成钢琴的共享材质资产（<c>Assets/Resources/PianoMaterials</c>）。
    /// <para>
    /// 琴键键面用 URP Complex Lit 的 Clear Coat：底层略粗糙、上面一层光滑透明漆，
    /// 对应真实钢琴键面（亚克力白键、乌木漆黑键）「底色 + 一层亮面」的观感。
    /// 存成资产是为了让清漆变体进入独立运行版（运行时 new 的材质在构建里可能缺这个变体）。
    /// </para>
    /// <para>
    /// 运行：菜单 <c>Tools/PianoTeacher3D/生成钢琴材质</c>，或命令行
    /// <c>-executeMethod PianoTeacher3D.EditorTools.PianoMaterialAssets.Generate</c>。可重复执行。
    /// </para>
    /// </summary>
    public static class PianoMaterialAssets
    {
        private const string Folder = "Assets/Resources/PianoMaterials/";

        [MenuItem("Tools/PianoTeacher3D/生成钢琴材质")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            ClearCoat("PianoKeyIvory", new Color(0.90f, 0.885f, 0.85f), 0.42f, 0.6f, 0.82f);
            // 黑键清漆偏弱：侧壁在掠射角下若反光太强，会把水下的青色环境映成灰青色
            ClearCoat("PianoKeyEbony", new Color(0.018f, 0.018f, 0.020f), 0.42f, 0.35f, 0.80f);

            // 竖琴钢琴（2026-11-18）：金属感深海蓝漆 + 一层厚清漆，掠射角能读出蓝青色的珠光高光
            ClearCoat("HarpLacquer", new Color(0.035f, 0.17f, 0.40f), 0.70f, 1.0f, 0.94f, 0.55f);
            // 弦钉 / 珠饰：珍珠银
            ClearCoat("HarpPearl", new Color(0.80f, 0.83f, 0.86f), 0.80f, 0.6f, 0.90f, 1.0f);
            // 琴凳软垫：雾蓝绒面
            ClearCoat("HarpCushion", new Color(0.30f, 0.42f, 0.52f), 0.18f, 0.0f, 0.5f, 0.0f);

            AssetDatabase.SaveAssets();
            Debug.Log("[PianoMaterials] 材质已生成：" + Folder);
        }

        private static void ClearCoat(string name, Color color, float baseSmoothness, float coatMask, float coatSmoothness,
            float metallic = 0f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Complex Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[PianoMaterials] 找不到 URP 着色器");
                return;
            }

            string path = Folder + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", baseSmoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_ClearCoatMask", coatMask);
            mat.SetFloat("_ClearCoatSmoothness", coatSmoothness);
            mat.EnableKeyword("_CLEARCOAT");
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", 2f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
        }
    }
}
#endif
