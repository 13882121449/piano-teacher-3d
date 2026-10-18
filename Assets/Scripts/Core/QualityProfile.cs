using UnityEngine;
using ShadowRes = UnityEngine.ShadowResolution;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 画质档位（对应方案 7.1「先定设备档位与性能目标」）。
    /// <para>
    /// 与方案 7.1 档位表的映射：<see cref="QualityTier.Standard"/> = Desktop Standard（集显 / 旧款笔记本），
    /// <see cref="QualityTier.High"/> = Desktop High（中档以上 Windows PC）。移动端 / Web 属于后续能力，此处不承诺。
    /// </para>
    /// </summary>
    public enum QualityTier
    {
        /// <summary>标准档：稳定 30–60 FPS；可降级内容 = 阴影分辨率 / 阴影距离 / 抗锯齿（方案 7.1）。</summary>
        Standard = 0,

        /// <summary>高画质档：1080p 目标 60 FPS（方案 7.1）。</summary>
        High = 1
    }

    /// <summary>
    /// 画质档位配置（对应方案 7.1 / 7.2、方案 9.2「配置与内容分离」）。
    /// <para>
    /// <b>设计目的</b>（方案 7.2 明确要求）：「视觉质量使用质量档位配置，<b>不把设备判断散落到各个脚本中</b>」。
    /// 因此本类是设备 / 画质判断的<b>唯一</b>判断点：
    /// 其它脚本只接收一个 <see cref="QualityProfile"/>，不自行读 <c>SystemInfo</c>、不自行改 <c>QualitySettings</c>。
    /// </para>
    /// <para>
    /// <b>可降级内容边界</b>（方案 7.1 / 5.4，务必遵守）：
    /// <c>Apply()</c> 只触碰 <see cref="QualitySettings"/> 与 <c>Application.targetFrameRate</c>。
    /// 降档只允许降低<b>阴影分辨率 / 阴影距离 / 抗锯齿 / 软硬阴影</b>；
    /// <b>不得</b>关闭琴键高亮（<c>PianoKeyHighlighter</c> 的暖金 / 冷蓝 / 暖橙教学语义色）、
    /// <b>不得</b>移除补光或「键盘上方点光」（手部与键面可读性属于教学下限）、
    /// <b>不得</b>改变任何 MIDI / 时间轴 / 教学状态。
    /// </para>
    /// <para>
    /// <b>性能</b>（方案 7.2）：<see cref="For"/> 返回<b>静态缓存实例</b>，每帧调用也不产生分配；
    /// <see cref="DetectRecommended"/> 为纯读取、不阻塞、不抛异常。
    /// </para>
    /// </summary>
    public sealed class QualityProfile
    {
        // ==================== 档位字段（冻结 API） ====================

        /// <summary>档位标识。</summary>
        public QualityTier Tier;

        /// <summary>中文显示名（"标准" / "高画质"），供诊断面板显示。</summary>
        public string DisplayName;

        /// <summary>阴影贴图分辨率（像素）。</summary>
        public int ShadowResolution;

        /// <summary>阴影距离（米）：决定实时阴影的有效范围。</summary>
        public float ShadowDistance;

        /// <summary>阴影强度 0..1（越大接触处越暗）。</summary>
        public float ShadowStrength;

        /// <summary>是否使用软阴影（落实到 <c>Light.shadows = LightShadows.Soft</c>）。</summary>
        public bool SoftShadows;

        /// <summary>
        /// 是否启用「环境遮蔽」观感。
        /// <para>
        /// <b>诚实性说明</b>（方案 0 节 / 审查 0.1）：内置渲染管线没有后处理栈，<b>无法实现真实 SSAO</b>。
        /// 本字段只表达「希望得到物体接触处变暗的观感」，实际由
        /// <see cref="EnableContactShadow"/> 的「贴地极淡径向渐变薄片」<b>近似</b>，
        /// 与「已实现后处理环境遮蔽」是两回事，不得混称。
        /// </para>
        /// </summary>
        public bool EnableAmbientOcclusion;

        /// <summary>目标帧率（方案 7.1：桌面两个档位均为 60）。</summary>
        public int TargetFrameRate;

        /// <summary>是否启用接触阴影近似（主光软阴影 + 贴地暗色渐变薄片）。</summary>
        public bool EnableContactShadow;

        // ==================== 静态缓存实例（方案 7.2：不每帧 new） ====================

        /// <summary>标准档（Desktop Standard，方案 7.1）。</summary>
        private static readonly QualityProfile StandardProfile = new QualityProfile
        {
            Tier = QualityTier.Standard,
            DisplayName = "标准",
            ShadowResolution = 1024,
            ShadowDistance = 8f,
            ShadowStrength = 0.55f,
            SoftShadows = false,
            EnableAmbientOcclusion = false,
            TargetFrameRate = 60,
            EnableContactShadow = true
        };

        /// <summary>高画质档（Desktop High，方案 7.1）。</summary>
        private static readonly QualityProfile HighProfile = new QualityProfile
        {
            Tier = QualityTier.High,
            DisplayName = "高画质",
            ShadowResolution = 2048,
            ShadowDistance = 18f,
            ShadowStrength = 0.72f,
            SoftShadows = true,
            EnableAmbientOcclusion = true,
            TargetFrameRate = 60,
            EnableContactShadow = true
        };

        /// <summary>
        /// 取指定档位的<b>静态缓存实例</b>（方案 7.2：不要在每帧 new 配置对象）。
        /// <para>非法枚举值（例如 <c>(QualityTier)99</c>）保守回落 <see cref="QualityTier.Standard"/>，绝不抛异常。</para>
        /// </summary>
        public static QualityProfile For(QualityTier tier) => tier switch
        {
            QualityTier.High => HighProfile,
            _ => StandardProfile // Standard 与任何非法值都落到标准档（保守优先，方案 7.1）
        };

        // ==================== 设备推荐（方案 7.1 / 7.2） ====================

        /// <summary>
        /// 依 <see cref="SystemInfo"/> 做<b>保守</b>推荐：只在信息明确充足时给高画质。
        /// <para>
        /// 本方法是设备判断的集中点（方案 7.2「不把设备判断散落到各个脚本中」）：
        /// 纯读取、不分配、不阻塞、不抛异常；信息不足（值缺失 / 无图形设备）时一律返回
        /// <see cref="QualityTier.Standard"/>，宁可画质保守也不牺牲帧率与稳定性。
        /// </para>
        /// </summary>
        public static QualityTier DetectRecommended()
        {
            // 无图形设备（如 -nographics / 无法初始化 GPU）：按最低档处理
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return QualityTier.Standard;

            // 着色器能力低于 SM4.5：视为老旧 GPU / 部分集显
            if (SystemInfo.graphicsShaderLevel < 45) return QualityTier.Standard;

            // 显存不足 2GB（含读取失败返回 0 的情况）：视为低端设备
            if (SystemInfo.graphicsMemorySize < 2048) return QualityTier.Standard;

            return QualityTier.High;
        }

        // ==================== 展示与落地 ====================

        /// <summary>中文可读描述，供诊断面板 / 日志显示。</summary>
        public string Describe()
        {
            string shadowKind = SoftShadows ? "软阴影" : "硬阴影";
            string ao = EnableContactShadow ? " / 接触阴影(近似，非后处理 AO)" : "";
            return $"{DisplayName} · 阴影 {ShadowResolution} / 距离 {ShadowDistance:0.#}m / {shadowKind} / {TargetFrameRate} FPS{ao}";
        }

        /// <summary>
        /// 把档位落地到 <see cref="QualitySettings"/> 与 <c>Application.targetFrameRate</c>。
        /// <para>
        /// <b>只改渲染质量，不改教学语义</b>（方案 7.1 的「可降级内容」边界，同时满足方案 5.4
        /// 「开启阴影与环境遮蔽前要测性能；低端设备可降低阴影分辨率/距离，而不是移除重要琴键高亮」）：
        /// </para>
        /// <list type="bullet">
        /// <item>这里<b>不</b>触碰琴键高亮（<c>PianoKeyHighlighter</c> 的暖金 / 冷蓝 / 暖橙语义色）；</item>
        /// <item>这里<b>不</b>关闭任何灯光（补光 / 键盘点光由环境组件保证始终存在）；</item>
        /// <item>阴影只降质量（分辨率 / 距离 / 软硬 / 级联），<b>不</b>置为 <c>ShadowQuality.Disable</c>。</item>
        /// </list>
        /// </summary>
        public void Apply()
        {
            // 目标帧率：必须先关闭 VSync，否则 targetFrameRate 不生效（方案 7.1「目标 60 FPS」）
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate > 0 ? TargetFrameRate : 60;

            // 阴影质量：永远保留阴影（All），只调分辨率 / 距离 / 级联 / 投影方式
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = ShadowDistance;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            // 级联数：高画质 4（远距离更细），标准 2（更省）。两者都是 Unity 明确支持的取值，避免 0/1 的兼容歧义
            QualitySettings.shadowCascades = Tier == QualityTier.High ? 4 : 2;

            // 注意：字段名 ShadowResolution 会遮蔽 UnityEngine.ShadowResolution 类型名，故用别名 ShadowRes 引用
            QualitySettings.shadowResolution = ShadowResolution >= 2048 ? ShadowRes.High : ShadowRes.Medium;

            QualitySettings.antiAliasing = Tier == QualityTier.High ? 4 : 2;
        }
    }
}
