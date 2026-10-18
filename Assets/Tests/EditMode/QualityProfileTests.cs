using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 画质档位纯逻辑单测（EditMode，可在 <c>-nographics</c> 下运行）。
    /// <para>
    /// 对应方案 7.1（设备档位与性能目标）、方案 7.2（视觉质量使用质量档位配置，不把设备判断散落到各脚本）、
    /// 方案 5.4（低端设备可降低阴影分辨率/距离，而不是移除重要琴键高亮）、方案 9.2（配置与内容分离）。
    /// </para>
    /// <para>
    /// 本测试只访问纯数据与 <see cref="QualitySettings"/> / <c>Application.targetFrameRate</c>，
    /// 不创建任何 GameObject / 材质 / 贴图；<see cref="SetUp"/> 会快照全局画质设置，<see cref="TearDown"/> 原样还原，
    /// 避免污染项目设置与其它测试。
    /// </para>
    /// </summary>
    public class QualityProfileTests
    {
        private QualitySnapshot _snapshot;

        [SetUp]
        public void SetUp() => _snapshot = QualitySnapshot.Capture();

        [TearDown]
        public void TearDown() => _snapshot.Restore();

        // ==================== 缓存与非法输入（方案 7.2：不要每帧 new） ====================

        [Test]
        public void For_ReturnsSameCachedInstancePerTier()
        {
            Assert.AreSame(QualityProfile.For(QualityTier.Standard), QualityProfile.For(QualityTier.Standard),
                "同档位必须返回同一静态缓存实例（方案 7.2：不每帧 new）");
            Assert.AreSame(QualityProfile.For(QualityTier.High), QualityProfile.For(QualityTier.High));

            Assert.AreNotSame(QualityProfile.For(QualityTier.Standard), QualityProfile.For(QualityTier.High));
            Assert.AreEqual(QualityTier.Standard, QualityProfile.For(QualityTier.Standard).Tier);
            Assert.AreEqual(QualityTier.High, QualityProfile.For(QualityTier.High).Tier);
        }

        [Test]
        public void For_InvalidEnumValue_FallsBackToStandardAndNeverThrows()
        {
            QualityProfile invalid = null;
            Assert.DoesNotThrow(() => invalid = QualityProfile.For((QualityTier)99));

            Assert.IsNotNull(invalid);
            Assert.AreEqual(QualityTier.Standard, invalid.Tier, "非法枚举必须保守回落到标准档（方案 7.1）");
            Assert.AreSame(QualityProfile.For(QualityTier.Standard), invalid, "回落应复用标准档缓存实例");
        }

        // ==================== 档位单调性（方案 7.1 档位表） ====================

        [Test]
        public void High_ShadowBudgetIsAtLeastStandard()
        {
            var standard = QualityProfile.For(QualityTier.Standard);
            var high = QualityProfile.For(QualityTier.High);

            Assert.GreaterOrEqual(high.ShadowResolution, standard.ShadowResolution);
            Assert.GreaterOrEqual(high.ShadowDistance, standard.ShadowDistance);
            Assert.GreaterOrEqual(high.ShadowStrength, standard.ShadowStrength);
            Assert.IsTrue(high.SoftShadows, "高画质档应使用软阴影");
            Assert.IsFalse(standard.SoftShadows, "标准档可用硬阴影换取性能");
        }

        [Test]
        public void TierFields_AreWithinSaneTeachingSafeRanges()
        {
            foreach (var tier in new[] { QualityTier.Standard, QualityTier.High })
            {
                var p = QualityProfile.For(tier);

                Assert.IsFalse(string.IsNullOrEmpty(p.DisplayName), "档位必须有中文显示名");
                Assert.GreaterOrEqual(p.ShadowResolution, 512, "阴影分辨率不得低到失去接触感");
                Assert.Greater(p.ShadowDistance, 0f);
                Assert.GreaterOrEqual(p.ShadowStrength, 0f);
                Assert.LessOrEqual(p.ShadowStrength, 1f);
                Assert.GreaterOrEqual(p.TargetFrameRate, 30);

                // 方案 5.4 / 7.1：接触阴影属于"可降级但不能移除"的教学可读性下限
                Assert.IsTrue(p.EnableContactShadow, "任何档位都必须保留接触阴影近似（保证物体不悬浮）");
            }
        }

        // ==================== 描述文本 ====================

        [Test]
        public void Describe_IsNonEmptyAndContainsTierName()
        {
            foreach (var tier in new[] { QualityTier.Standard, QualityTier.High })
            {
                var p = QualityProfile.For(tier);
                string text = p.Describe();

                Assert.IsNotEmpty(text);
                StringAssert.Contains(p.DisplayName, text, "描述必须包含档位名，供诊断面板区分");
                StringAssert.Contains("FPS", text);
            }

            StringAssert.Contains(QualityProfile.For(QualityTier.High).ShadowResolution.ToString(),
                QualityProfile.For(QualityTier.High).Describe(), "高画质档描述应带出阴影分辨率");
            StringAssert.Contains(QualityProfile.For(QualityTier.High).SoftShadows ? "软阴影" : "硬阴影",
                QualityProfile.For(QualityTier.High).Describe());
        }

        // ==================== 设备推荐（方案 7.1 / 7.2） ====================

        [Test]
        public void DetectRecommended_ReturnsDefinedTierAndNeverThrows()
        {
            QualityTier recommended = QualityTier.Standard;
            Assert.DoesNotThrow(() => recommended = QualityProfile.DetectRecommended());

            Assert.IsTrue(recommended == QualityTier.Standard || recommended == QualityTier.High,
                "推荐值必须在枚举范围内");
            Assert.IsTrue(System.Enum.IsDefined(typeof(QualityTier), recommended));

            // 连续调用结果稳定（纯读取，不依赖帧 / 时序；信息不足时保守返回 Standard）
            Assert.AreEqual(recommended, QualityProfile.DetectRecommended());
        }

        // ==================== 落地到 QualitySettings / targetFrameRate ====================

        [Test]
        public void Apply_SetsTargetFrameRateAndDoesNotThrow()
        {
            foreach (var tier in new[] { QualityTier.Standard, QualityTier.High })
            {
                var p = QualityProfile.For(tier);

                Assert.DoesNotThrow(() => p.Apply());
                Assert.AreEqual(p.TargetFrameRate, Application.targetFrameRate,
                    "Apply() 必须让 Application.targetFrameRate 与档位一致");
                Assert.AreEqual(0, QualitySettings.vSyncCount, "为使 targetFrameRate 生效必须关闭 VSync");

                // 方案 7.1 降级边界：阴影只降质量，绝不关闭
                Assert.AreNotEqual(ShadowQuality.Disable, QualitySettings.shadows,
                    "任何档位都不得关闭阴影（降级内容仅限分辨率 / 距离）");
                Assert.AreEqual(p.ShadowDistance, QualitySettings.shadowDistance, 0.001f);
            }
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
