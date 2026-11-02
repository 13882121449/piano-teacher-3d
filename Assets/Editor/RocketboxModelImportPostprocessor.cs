using UnityEditor;

namespace PianoTeacher3D.EditorTools
{
    /// <summary>
    /// Rocketbox 角色一律按 Humanoid 导入。
    ///
    /// <para>
    /// 为什么需要这个脚本：FBX 默认按 Generic 导入，<c>Animator.GetBoneTransform(HumanBodyBones.RightFoot)</c>
    /// 会返回 null，于是 <c>PedalFootAnimator</c>、<c>HandIKController</c> 的骨骼查找全部落空，
    /// 手脚 IK 静默失效（不报错、只是不动）。<c>createFromThisModel</c> 则让 Avatar 直接由这份骨架生成，
    /// 不必再维护一个 Avatar 资产。
    /// </para>
    ///
    /// <para>
    /// 幂等：只在导入前设置，值没变时不会触发重导入。
    /// </para>
    /// </summary>
    public sealed class RocketboxModelImportPostprocessor : AssetPostprocessor
    {
        private const string Folder = "/Characters/Rocketbox/Resources/Characters/Rocketbox/";

        private void OnPreprocessModel()
        {
            var normalized = assetPath.Replace('\\', '/');
            if (normalized.IndexOf(Folder, System.StringComparison.Ordinal) < 0) return;
            if (!(assetImporter is ModelImporter importer)) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            // 保留骨骼 Transform：IK 需要直接读写每根骨骼
            importer.optimizeGameObjects = false;
        }
    }
}
