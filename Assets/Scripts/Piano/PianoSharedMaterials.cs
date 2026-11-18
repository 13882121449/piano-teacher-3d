using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 钢琴的共享材质资产（<c>Resources/PianoMaterials</c>，由编辑器脚本 <c>PianoMaterialAssets</c> 生成）。
    /// <para>
    /// 为什么用资产：清漆效果依赖 URP Complex Lit 的 Clear Coat 变体，只有被材质资产引用时才会进入
    /// 独立运行版；运行时临时 new 出来的材质在构建里可能拿不到这个变体。
    /// </para>
    /// <para>
    /// 返回的是资产本身：多个物体共享，<b>调用方不得销毁也不得修改参数</b>
    /// （逐物体的高亮等效果用 MaterialPropertyBlock）。资产缺失时返回 null，调用方退回各自的程序化材质。
    /// </para>
    /// </summary>
    public static class PianoSharedMaterials
    {
        private const string Folder = "PianoMaterials/";
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static Material Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = Resources.Load<Material>(Folder + name);
            if (material != null) Cache[name] = material;
            return material;
        }
    }
}
