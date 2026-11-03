using System.Text;
using PianoTeacher3D.Characters;
using UnityEditor;
using UnityEngine;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// 一次性诊断：角色库人像缩略图在运行时到底能不能被 <c>Resources.Load</c> 拿到。
    ///
    /// <para>
    /// 背景：角色库三张竖版卡在截图里是纯色占位（<c>AvatarSelectionUI.LoadPortrait</c> 返回 null）。
    /// 磁盘上 PNG 真实存在、路径拼接与 registry 一致、GUID 无重复，剩下的可能只有两种：
    /// ① 资源根本没进 AssetDatabase（导入失败）；② 导入成功但 <c>Resources</c> 合并/查找失败。
    /// 本探针同时走 <c>AssetDatabase</c> 与 <c>Resources</c> 两条路，把两者对照打印出来。
    /// </para>
    ///
    /// <para>
    /// 用法：<c>Unity.exe -batchmode -projectPath . -executeMethod PianoTeacher3D.EditorTools.PortraitProbe.Run -logFile probe.log</c>
    /// </para>
    /// </summary>
    public static class PortraitProbe
    {
        private const string AssetDir = "Assets/Characters/Rocketbox/Resources/Characters/Rocketbox";

        [MenuItem("Tools/PianoTeacher3D/诊断/人像缩略图加载")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[probe] ==== 人像缩略图加载诊断 ====");

            sb.AppendLine($"[probe] registry.BuiltInIds = {string.Join(", ", CharacterRegistry.BuiltInIds)}");
            foreach (var id in CharacterRegistry.BuiltInIds)
            {
                string path = CharacterRegistry.PortraitResourcesPath(id);
                sb.AppendLine($"[probe] id={id} resourcesPath='{path}'");
            }

            // ① AssetDatabase：资源是否被 Unity 导入
            foreach (var name in new[] { "Female_Adult_01_portrait", "Male_Adult_01_portrait", "Female_Child_01_portrait" })
            {
                string assetPath = $"{AssetDir}/{name}.png";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                sb.AppendLine(tex != null
                    ? $"[probe] AD  OK   {name} {tex.width}x{tex.height} readable={tex.isReadable} format={tex.format}"
                    : $"[probe] AD  NULL {name} (path={assetPath}, exists={System.IO.File.Exists(assetPath)})");
            }

            // ② Resources：运行时那条路（Resources 根 = Assets/Characters/Rocketbox/Resources）
            foreach (var rel in new[]
                     {
                         "Characters/Rocketbox/Female_Adult_01_portrait",
                         "Characters/Rocketbox/Male_Adult_01_portrait",
                         "Characters/Rocketbox/Female_Child_01_portrait",
                     })
            {
                var tex = Resources.Load<Texture2D>(rel);
                sb.AppendLine(tex != null
                    ? $"[probe] RES OK   {rel} {tex.width}x{tex.height}"
                    : $"[probe] RES NULL {rel}");
            }

            // ③ Resources 目录下所有贴图（看清 Resources 到底收录了哪些）
            var all = Resources.LoadAll<Texture2D>("Characters/Rocketbox");
            sb.AppendLine($"[probe] Resources.LoadAll<Texture2D>(\"Characters/Rocketbox\") => {all.Length} 张");
            foreach (var tex in all) sb.AppendLine($"[probe]   - {tex.name} {tex.width}x{tex.height}");

            sb.AppendLine($"[probe] AssetDatabase.FindAssets(t:Texture2D, {AssetDir}) => " +
                          $"{AssetDatabase.FindAssets("t:Texture2D", new[] { AssetDir }).Length} 张");

            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
