using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 资产门禁 <see cref="CharacterAssetValidator.ValidateMaterials"/> 的直接单元测试。
    ///
    /// <para>
    /// 为什么需要这一组：此前「缺贴图必须被拒绝」只由三条 PlayMode 用例间接覆盖
    /// （用真实的 Rocketbox 资产当被测对象）。贴图补齐后那三条的前提消失、必须改写成
    /// 「完整资产应通过」，于是「门禁还能不能拦住坏资产」就没人测了。
    /// 这里用**代码构造**的网格与材质直接把两个方向都钉住：
    /// 无主贴图 → 拒绝；有主贴图 → 通过；材质为空 → 拒绝；无蒙皮网格 → 拒绝。
    /// </para>
    /// </summary>
    public class CharacterAssetValidatorTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private SkinnedMeshRenderer CreateRenderer(params Material[] materials)
        {
            _go = new GameObject("ValidatorFixture");
            var smr = _go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMaterials = materials;
            return smr;
        }

        private static Material CreateLitMaterial()
        {
            // 与生产代码同一条回落链：URP 优先，内置 Standard 兜底
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")
                : Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader, "找不到可用 shader，测试环境异常");
            return new Material(shader);
        }

        [Test]
        public void ValidateMaterials_NoSkinnedRenderer_Fails()
        {
            _go = new GameObject("EmptyFixture");
            var result = CharacterAssetValidator.ValidateMaterials(_go);

            Assert.IsFalse(result.Ok, "没有任何蒙皮网格应被拒绝");
            StringAssert.Contains("蒙皮网格", result.Message);
        }

        [Test]
        public void ValidateMaterials_MaterialWithoutMainTexture_Fails()
        {
            var mat = CreateLitMaterial();
            CreateRenderer(mat);

            var result = CharacterAssetValidator.ValidateMaterials(_go);

            Assert.IsFalse(result.Ok, "没有主贴图的材质会被渲染成白模，必须拒绝");
            StringAssert.Contains("主贴图", result.Message);
        }

        [Test]
        public void ValidateMaterials_MaterialWithBaseMap_Passes()
        {
            // URP/Lit 走 _BaseMap（生产路径就是这条）
            var mat = CreateLitMaterial();
            var tex = new Texture2D(4, 4);
            mat.SetTexture(mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex", tex);
            CreateRenderer(mat);

            var result = CharacterAssetValidator.ValidateMaterials(_go);

            Assert.IsTrue(result.Ok, "带主贴图的材质应通过，当前：" + result.Message);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void ValidateMaterials_NullMaterial_Fails()
        {
            CreateRenderer(new Material[] { null });

            var result = CharacterAssetValidator.ValidateMaterials(_go);

            Assert.IsFalse(result.Ok, "材质槽为空（材质丢失）必须拒绝");
            StringAssert.Contains("为空", result.Message);
        }

        [Test]
        public void ValidateMaterials_NullRoot_Fails()
        {
            var result = CharacterAssetValidator.ValidateMaterials(null);
            Assert.IsFalse(result.Ok, "空对象必须拒绝而不是抛异常");
        }
    }
}
