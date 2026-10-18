using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Core;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 教学场景环境装配单测（EditMode，可在 <c>-nographics</c> 下运行）。
    /// <para>
    /// 对应方案 5.1 第 6 项（环境与后处理）、方案 5.4（钢琴材质与灯光）、
    /// 审查 1.3B（小而完整的教学场景）、审查 1.3C（为教学目标优化）、
    /// 方案 7.1 / 7.2（档位联动与性能预算）、方案 9.3（场景装配与测试）。
    /// </para>
    /// <para>
    /// <b>不调用 <c>PianoKeyMap.BuildKeys()</c></b>：该方法内部使用 <c>Object.Destroy</c>（仅运行期合法），
    /// EditMode 下会产生 "Destroy may not be called from edit mode" 错误日志。
    /// 因此这里用「与真实键盘同尺度的代理体块」提供等价包围范围
    /// （52 白键 × 24 mm = 1.248 m 居中、键面高 0.742 m），用于验证 <c>ContentBounds</c> 联合计算。
    /// </para>
    /// </summary>
    public class SceneEnvironmentTests
    {
        private GameObject _envRoot;
        private GameObject _pianoGo;
        private PianoKeyMap _keyMap;
        private PianoSceneEnvironment _env;
        private float _keyboardHalfWidth;
        private QualitySnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _snapshot = QualitySnapshot.Capture();

            _envRoot = new GameObject("EnvTestRoot");
            _env = _envRoot.AddComponent<PianoSceneEnvironment>();

            _pianoGo = new GameObject("Piano");
            _keyMap = _pianoGo.AddComponent<PianoKeyMap>();

            float total = _keyMap.TotalWidth;             // 52 × 0.024 = 1.248 m
            _keyboardHalfWidth = total * 0.5f;
            _pianoGo.transform.localPosition = new Vector3(-_keyboardHalfWidth, 0f, 0f);

            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "KeyboardProxy";
            proxy.transform.SetParent(_pianoGo.transform, false);
            proxy.transform.localPosition = new Vector3(total * 0.5f, _keyMap.KeybedHeight, 0f);
            proxy.transform.localScale = new Vector3(total, 0.045f, _keyMap.WhiteLength);
            Object.DestroyImmediate(proxy.GetComponent<Collider>());
        }

        [TearDown]
        public void TearDown()
        {
            if (_envRoot != null) Object.DestroyImmediate(_envRoot);
            if (_pianoGo != null) Object.DestroyImmediate(_pianoGo);
            _snapshot.Restore();
        }

        // ==================== 幂等与几何规模（方案 7.2） ====================

        [Test]
        public void Build_IsIdempotent_AndKeepsGeometryMinimal()
        {
            var high = QualityProfile.For(QualityTier.High);
            _env.Build(_keyMap, high);

            int childCount = _env.transform.childCount;
            int lightCount = _env.GetComponentsInChildren<Light>(true).Length;
            int rendererCount = _env.GetComponentsInChildren<Renderer>(true).Length;

            _env.Build(_keyMap, high); // 重复 Build 必须幂等

            Assert.AreEqual(childCount, _env.transform.childCount, "重复 Build 不得重复创建对象");
            Assert.AreEqual(lightCount, _env.GetComponentsInChildren<Light>(true).Length,
                "重复 Build 不得重复创建灯光");
            Assert.AreEqual(4, lightCount, "应恰好为 主光 + 补光 + 轮廓光 + 键盘点光");
            Assert.Greater(rendererCount, 0);
            Assert.Less(rendererCount, 20, "环境只应包含少量程序化几何，远低于 88 键规模（方案 7.2）");
        }

        // ==================== 三点光照 + 键盘点光（方案 5.4 / 审查 1.3B） ====================

        [Test]
        public void Build_CreatesFourManagedLights_AsChildren()
        {
            _env.Build(_keyMap, QualityProfile.For(QualityTier.Standard));

            Assert.IsNotNull(_env.MainLight, "缺主光");
            Assert.IsNotNull(_env.FillLight, "缺补光（手部暗部可读性）");
            Assert.IsNotNull(_env.RimLight, "缺轮廓光（人物/琴体与背景分离）");
            Assert.IsNotNull(_env.KeyLight, "缺键盘上方点光");

            Assert.AreEqual(_env.transform, _env.MainLight.transform.parent, "灯光必须由本组件管理");
            Assert.AreEqual(_env.transform, _env.FillLight.transform.parent);
            Assert.AreEqual(_env.transform, _env.RimLight.transform.parent);
            Assert.AreEqual(_env.transform, _env.KeyLight.transform.parent);

            Assert.AreEqual(LightType.Directional, _env.MainLight.type);
            Assert.AreEqual(LightType.Directional, _env.FillLight.type);
            Assert.AreEqual(LightType.Directional, _env.RimLight.type);
            Assert.AreEqual(LightType.Point, _env.KeyLight.type);

            // 方案 5.4：只有 1 个投影灯，避免多套阴影叠加与多个强光造成琴键反光刺眼
            Assert.AreEqual(LightShadows.None, _env.FillLight.shadows);
            Assert.AreEqual(LightShadows.None, _env.RimLight.shadows);
            Assert.AreEqual(LightShadows.None, _env.KeyLight.shadows);

            // 补光弱于主光（约 1/3 量级）
            Assert.Less(_env.FillLight.intensity, _env.MainLight.intensity);
            Assert.Less(_env.RimLight.intensity, _env.FillLight.intensity);
        }

        // ==================== 档位联动（方案 7.1：可降级但不移除） ====================

        [Test]
        public void ApplyQuality_TiersControlShadows_ButKeyLightStaysEnabled()
        {
            var high = QualityProfile.For(QualityTier.High);
            var standard = QualityProfile.For(QualityTier.Standard);

            _env.Build(_keyMap, high);

            Assert.AreEqual(QualityTier.High, _env.AppliedTier);
            Assert.AreEqual(LightShadows.Soft, _env.MainLight.shadows);
            Assert.AreEqual(high.ShadowStrength, _env.MainLight.shadowStrength, 0.0001f);
            Assert.AreEqual(high.ShadowResolution, _env.MainLight.shadowCustomResolution);
            Assert.IsTrue(_env.KeyLight.enabled);

            _env.ApplyQuality(standard); // 热切换档位

            Assert.AreEqual(QualityTier.Standard, _env.AppliedTier);
            Assert.AreEqual(LightShadows.Hard, _env.MainLight.shadows, "标准档降级为硬阴影");
            Assert.AreEqual(standard.ShadowStrength, _env.MainLight.shadowStrength, 0.0001f);
            Assert.AreEqual(standard.ShadowResolution, _env.MainLight.shadowCustomResolution);

            // 方案 7.1 / 5.4：低档位可降低阴影质量，但绝不"移除重要琴键高亮"或移除关键光照
            Assert.IsTrue(_env.KeyLight.enabled, "标准档也必须启用键盘点光");
            Assert.Greater(_env.KeyLight.intensity, 0f);
            Assert.IsTrue(_env.FillLight.enabled, "标准档不得移除补光（保证手部可读性）");
            Assert.Greater(_env.FillLight.intensity, 0f);
            Assert.IsTrue(_env.RimLight.enabled);
        }

        // ==================== 内容包围盒（供相机 / 诊断） ====================

        [Test]
        public void ContentBounds_CoversKeyboardExtent()
        {
            _env.Build(_keyMap, QualityProfile.For(QualityTier.High));

            Bounds b = _env.ContentBounds;
            Assert.Greater(b.size.x, 0f, "包围盒尺寸必须为正");
            Assert.Greater(b.size.y, 0f);
            Assert.Greater(b.size.z, 0f);

            // 覆盖 88 键两端（A0 与 C8 键附近）
            Assert.LessOrEqual(b.min.x, -_keyboardHalfWidth);
            Assert.GreaterOrEqual(b.max.x, _keyboardHalfWidth);

            // 地面顶面为 y = 0，故 max.y 必须由键盘 / 琴体贡献（证明琴体已被纳入联合包围盒）
            Assert.GreaterOrEqual(b.max.y, _keyMap.KeySurfaceY, "包围盒必须包含琴键键面高度");
        }

        // ==================== 环境可见性（调试对比） ====================

        [Test]
        public void SetEnvironmentVisible_TogglesEnvironmentRenderers()
        {
            _env.Build(_keyMap, QualityProfile.For(QualityTier.High));
            var renderers = _env.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "环境应包含可开关的几何");

            _env.SetEnvironmentVisible(false);
            foreach (var r in renderers) Assert.IsFalse(r.enabled, $"环境几何 {r.name} 应被关闭");

            _env.SetEnvironmentVisible(true);
            foreach (var r in renderers) Assert.IsTrue(r.enabled, $"环境几何 {r.name} 应被恢复");

            // 环境开关不得影响灯光（否则"有/无环境"对比会连带改变琴键可读性）
            Assert.IsTrue(_env.MainLight.enabled);
            Assert.IsTrue(_env.KeyLight.enabled);
            Assert.IsTrue(_env.FillLight.enabled);
        }

        // ==================== 容错（keyMap / quality 允许为 null） ====================

        [Test]
        public void Build_ToleratesNullArguments()
        {
            Assert.DoesNotThrow(() => _env.Build(null, null));
            Assert.IsNotNull(_env.MainLight, "即使无 keyMap 也必须建好灯光");
            Assert.Greater(_env.ContentBounds.size.x, 0f, "无 keyMap 时包围盒应退化为地面 / 键盘尺度兜底");
            Assert.Greater(_env.ContentBounds.size.y, 0f);
        }

        // ==================== 全局画质设置快照 ====================

        /// <summary>全局画质设置快照：EditMode 下写 QualitySettings 会改动项目设置，必须原样还原。</summary>
        private sealed class QualitySnapshot
        {
            private ShadowQuality _shadows;
            private float _shadowDistance;
            private UnityEngine.ShadowResolution _shadowResolution;
            private UnityEngine.ShadowProjection _shadowProjection;
            private int _shadowCascades;
            private int _antiAliasing;
            private int _vSyncCount;
            private int _targetFrameRate;

            public static QualitySnapshot Capture() => new QualitySnapshot
            {
                _shadows = QualitySettings.shadows,
                _shadowDistance = QualitySettings.shadowDistance,
                _shadowResolution = QualitySettings.shadowResolution,
                _shadowProjection = QualitySettings.shadowProjection,
                _shadowCascades = QualitySettings.shadowCascades,
                _antiAliasing = QualitySettings.antiAliasing,
                _vSyncCount = QualitySettings.vSyncCount,
                _targetFrameRate = Application.targetFrameRate
            };

            public void Restore()
            {
                QualitySettings.shadows = _shadows;
                QualitySettings.shadowDistance = _shadowDistance;
                QualitySettings.shadowResolution = _shadowResolution;
                QualitySettings.shadowProjection = _shadowProjection;
                QualitySettings.shadowCascades = _shadowCascades;
                QualitySettings.antiAliasing = _antiAliasing;
                QualitySettings.vSyncCount = _vSyncCount;
                Application.targetFrameRate = _targetFrameRate;
            }
        }
    }
}
