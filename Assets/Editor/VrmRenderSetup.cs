#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VRM10.MToon10;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// 给项目的 URP 渲染器加上 MToon 描边渲染功能（UniVRM 文档：URP 下 MToon10 的描边 Pass
    /// 「MToonOutline」需要 <see cref="MToonOutlineRenderFeature"/> 才会被绘制）。可重复执行，已有时不重复添加。
    /// <para>命令行：<c>-executeMethod PianoTeacher3D.EditorTools.VrmRenderSetup.AddOutlineFeature</c></para>
    /// </summary>
    public static class VrmRenderSetup
    {
        private const string RendererPath = "Assets/Settings/PianoTeacher3D-URP-Renderer.asset";

        [MenuItem("Tools/PianoTeacher3D/添加 MToon 描边渲染功能")]
        public static void AddOutlineFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                Debug.LogError("[EXEC] 找不到渲染器资产 " + RendererPath);
                return;
            }
            if (data.rendererFeatures.Any(f => f is MToonOutlineRenderFeature))
            {
                Debug.Log("[EXEC] MToon 描边功能已存在");
                return;
            }

            // 与 URP 渲染器 Inspector 的「Add Renderer Feature」同一做法：子资产 + 序列化列表 + 本地 ID 映射表
            var feature = ScriptableObject.CreateInstance<MToonOutlineRenderFeature>();
            feature.name = "MToonOutline";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[EXEC] 已添加 MToon 描边功能");
        }
    }
}
#endif
