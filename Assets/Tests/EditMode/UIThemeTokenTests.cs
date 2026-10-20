using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.UI;

/// <summary>
/// UITheme 设计令牌与 UI 状态机纯逻辑单测（EditMode）。
/// <para>
/// 对应方案 3.3（视觉设计系统：颜色 / 字号 / 间距 / 圆角 / 控件高度 / 按钮七态）、
/// 方案 3.4（状态可见性、快捷键、响应式折叠）、方案 8.3（错误文案三要素）、
/// 审查 2.2A / 2.2B / 2.2C / 2.3 与阶段 C 验收（当前值无需猜测、窄窗口不挤压、对比度可读）。
/// </para>
/// <para>
/// 本测试只访问纯数据（颜色 / 常量 / 枚举 / 纯静态函数），不创建任何 GameObject、纹理或字体，
/// 因此不依赖场景与渲染环境。
/// </para>
/// </summary>
public class UIThemeTokenTests
{
    // ==================== 颜色令牌 ====================

    [Test]
    public void ColorTokens_AreAllValidOpaqueOrTranslucentValues()
    {
        var tokens = new Dictionary<string, Color>
        {
            { nameof(UITheme.SceneBg), UITheme.SceneBg },
            { nameof(UITheme.PanelBg), UITheme.PanelBg },
            { nameof(UITheme.PanelBgRaised), UITheme.PanelBgRaised },
            { nameof(UITheme.PanelBgDeep), UITheme.PanelBgDeep },
            { nameof(UITheme.Control), UITheme.Control },
            { nameof(UITheme.ControlHover), UITheme.ControlHover },
            { nameof(UITheme.ControlPressed), UITheme.ControlPressed },
            { nameof(UITheme.Accent), UITheme.Accent },
            { nameof(UITheme.AccentBright), UITheme.AccentBright },
            { nameof(UITheme.AccentDim), UITheme.AccentDim },
            { nameof(UITheme.Selected), UITheme.Selected },
            { nameof(UITheme.Divider), UITheme.Divider },
            { nameof(UITheme.TextPrimary), UITheme.TextPrimary },
            { nameof(UITheme.TextSecondary), UITheme.TextSecondary },
            { nameof(UITheme.TextOnAccent), UITheme.TextOnAccent },
            { nameof(UITheme.TextDisabled), UITheme.TextDisabled },
            { nameof(UITheme.TextError), UITheme.TextError },
            { nameof(UITheme.TextWarning), UITheme.TextWarning },
            { nameof(UITheme.Error), UITheme.Error },
            { nameof(UITheme.Warning), UITheme.Warning },
            { nameof(UITheme.Success), UITheme.Success },
            { nameof(UITheme.Disabled), UITheme.Disabled },
            { nameof(UITheme.DangerBg), UITheme.DangerBg },
            { nameof(UITheme.LeftHandColor), UITheme.LeftHandColor },
            { nameof(UITheme.RightHandColor), UITheme.RightHandColor },
            { nameof(UITheme.UpcomingColor), UITheme.UpcomingColor }
        };

        foreach (var pair in tokens)
        {
            var c = pair.Value;
            Assert.GreaterOrEqual(c.r, 0f, pair.Key);
            Assert.LessOrEqual(c.r, 1f, pair.Key);
            Assert.GreaterOrEqual(c.g, 0f, pair.Key);
            Assert.LessOrEqual(c.g, 1f, pair.Key);
            Assert.GreaterOrEqual(c.b, 0f, pair.Key);
            Assert.LessOrEqual(c.b, 1f, pair.Key);
            Assert.Greater(c.a, 0f, $"{pair.Key} 不应完全透明");
        }
    }

    [Test]
    public void BackgroundLayers_KeepDarkToLightOrder()
    {
        // 层次：场景背景最深，其次顶栏/底栏，再次面板，最后控件
        Assert.Less(UITheme.RelativeLuminance(UITheme.SceneBg),
            UITheme.RelativeLuminance(UITheme.PanelBgDeep), "场景背景应比顶栏更深");
        Assert.LessOrEqual(UITheme.RelativeLuminance(UITheme.PanelBgDeep),
            UITheme.RelativeLuminance(UITheme.PanelBg), "顶栏不应比面板更亮");
        Assert.Less(UITheme.RelativeLuminance(UITheme.PanelBg),
            UITheme.RelativeLuminance(UITheme.Control), "面板应比控件底色更深");
        Assert.Less(UITheme.RelativeLuminance(UITheme.Control),
            UITheme.RelativeLuminance(UITheme.ControlHover), "悬停态应比常态更亮");
        Assert.Less(UITheme.RelativeLuminance(UITheme.ControlPressed),
            UITheme.RelativeLuminance(UITheme.Control), "按下态应比常态更暗");
    }

    [Test]
    public void AccentScale_IsMonotonic()
    {
        Assert.Less(UITheme.RelativeLuminance(UITheme.AccentDim),
            UITheme.RelativeLuminance(UITheme.Accent), "AccentDim 应比 Accent 暗");
        Assert.Less(UITheme.RelativeLuminance(UITheme.Accent),
            UITheme.RelativeLuminance(UITheme.AccentBright), "AccentBright 应比 Accent 亮");
    }

    // ==================== 对比度（方案 3.3「保证文本对比度」） ====================

    [Test]
    public void TextPrimary_MeetsWcagAaOnAllPanelBackgrounds()
    {
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextPrimary, UITheme.PanelBg), 4.5f);
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextPrimary, UITheme.PanelBgDeep), 4.5f);
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextPrimary, UITheme.PanelBgRaised), 4.5f);
    }

    [Test]
    public void SecondaryAndStatusText_MeetMinimumContrast()
    {
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextSecondary, UITheme.PanelBg), 3f,
            "辅助文字至少 3:1");
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextDisabled, UITheme.PanelBg), 2f,
            "禁用文字也应可辨识");
    }

    [Test]
    public void TextOnAccent_IsReadableOnAccentAndSelectedStates()
    {
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextOnAccent, UITheme.Accent), 4.5f,
            "主按钮文字必须在强调色底上清晰");
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextOnAccent, UITheme.AccentBright), 4.5f);
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextPrimary, UITheme.Selected), 4.5f);
    }

    [Test]
    public void ErrorAndWarningText_AreReadableOnTheirBanners()
    {
        float errorContrast = UITheme.ContrastRatio(UITheme.TextError,
            UITheme.ErrorBannerColor(ErrorSeverity.Recoverable));
        float warningContrast = UITheme.ContrastRatio(UITheme.TextWarning,
            UITheme.ErrorBannerColor(ErrorSeverity.Warning));
        Assert.GreaterOrEqual(errorContrast, 4.5f);
        Assert.GreaterOrEqual(warningContrast, 4.5f);
    }

    [Test]
    public void ContrastRatio_ExtremesAreSane()
    {
        Assert.AreEqual(1f, UITheme.ContrastRatio(UITheme.Accent, UITheme.Accent), 0.001f);
        Assert.AreEqual(21f, UITheme.ContrastRatio(Color.white, Color.black), 0.1f);
    }

    [Test]
    public void TeachingSemanticColors_KeepDocumentedMeaning()
    {
        // 审查 2.3：金色 = 预提示，蓝色 = 左手发声，橙色 = 右手发声，语义不得互换
        Assert.AreEqual(UITheme.Accent.r, UITheme.UpcomingColor.r, 0.001f);
        Assert.Greater(UITheme.LeftHandColor.b, UITheme.LeftHandColor.r, "左手应为冷色（偏蓝）");
        Assert.Greater(UITheme.RightHandColor.r, UITheme.RightHandColor.b, "右手应为暖色（偏橙）");
        Assert.AreNotEqual(UITheme.LeftHandColor.r, UITheme.RightHandColor.r);
    }

    // ==================== 字号 / 间距 / 圆角 / 控件高度 ====================

    [Test]
    public void FontScale_IsStrictlyOrdered()
    {
        Assert.Greater(UITheme.FontPageTitle, UITheme.FontTitle);
        Assert.GreaterOrEqual(UITheme.FontTitle, UITheme.FontSongTitle);
        Assert.Greater(UITheme.FontSongTitle, UITheme.FontButton);
        Assert.GreaterOrEqual(UITheme.FontButton, UITheme.FontBody);
        Assert.Greater(UITheme.FontBody, UITheme.FontSmall);
        Assert.GreaterOrEqual(UITheme.FontSmall, UITheme.FontCaption);
        Assert.GreaterOrEqual(UITheme.FontSmall, UITheme.FontStatusLabel);
    }

    [Test]
    public void SpacingScale_UsesDocumentedSteps()
    {
        Assert.AreEqual(4f, UITheme.Space4);
        Assert.AreEqual(8f, UITheme.Space8);
        Assert.AreEqual(12f, UITheme.Space12);
        Assert.AreEqual(16f, UITheme.Space16);
        Assert.AreEqual(24f, UITheme.Space24);
        Assert.Less(UITheme.Space4, UITheme.Space8);
        Assert.Less(UITheme.Space8, UITheme.Space12);
        Assert.Less(UITheme.Space12, UITheme.Space16);
        Assert.Less(UITheme.Space16, UITheme.Space24);
    }

    [Test]
    public void RadiusScale_IsSmallToLarge()
    {
        Assert.Less(UITheme.RadiusSmall, UITheme.RadiusMedium);
        Assert.Less(UITheme.RadiusMedium, UITheme.RadiusLarge);
        Assert.Greater(UITheme.RadiusSmall, 0);
    }

    [Test]
    public void ControlHeights_KeepPrimaryLargest()
    {
        Assert.Greater(UITheme.HeightPrimary, UITheme.HeightButton, "主按钮必须比普通按钮更大");
        Assert.Greater(UITheme.HeightButton, UITheme.HeightSmall);
        Assert.GreaterOrEqual(UITheme.HeightInput, UITheme.HeightSmall);
        Assert.GreaterOrEqual(UITheme.HeightPrimary, 44f, "主按钮不应小到难以点击");
    }

    // ==================== 按钮七态（方案 3.3 / 审查 2.2A） ====================

    [Test]
    public void ButtonState_HasExactlySevenDocumentedStates()
    {
        var values = System.Enum.GetValues(typeof(ButtonState));
        Assert.AreEqual(7, values.Length);
        foreach (var expected in new[]
                 {
                     ButtonState.Default, ButtonState.Hover, ButtonState.Pressed, ButtonState.Selected,
                     ButtonState.Disabled, ButtonState.Loading, ButtonState.Error
                 })
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(ButtonState), expected), expected.ToString());
        }
    }

    [Test]
    public void ButtonStyle_CoversVisualHierarchy()
    {
        var values = System.Enum.GetValues(typeof(ButtonStyle));
        Assert.AreEqual(5, values.Length);
        Assert.IsTrue(System.Enum.IsDefined(typeof(ButtonStyle), ButtonStyle.Primary));
        Assert.IsTrue(System.Enum.IsDefined(typeof(ButtonStyle), ButtonStyle.Danger));
        Assert.IsTrue(System.Enum.IsDefined(typeof(ButtonStyle), ButtonStyle.Toggle));
    }

    [Test]
    public void PrimaryStyle_IsTheOnlyStrongAccentStyle()
    {
        Assert.AreEqual(UITheme.Accent, UITheme.BaseColor(ButtonStyle.Primary));
        Assert.AreEqual(UITheme.TextOnAccent, UITheme.LabelColor(ButtonStyle.Primary));
        Assert.AreEqual(UITheme.AccentBright, UITheme.SelectedColor(ButtonStyle.Primary));
        Assert.IsTrue(UITheme.IsStrongStyle(ButtonStyle.Primary));

        // 次操作/开关/弱化样式都不能与主操作同色
        Assert.AreNotEqual(UITheme.BaseColor(ButtonStyle.Primary), UITheme.BaseColor(ButtonStyle.Secondary));
        Assert.AreNotEqual(UITheme.BaseColor(ButtonStyle.Primary), UITheme.BaseColor(ButtonStyle.Toggle));
        Assert.AreNotEqual(UITheme.BaseColor(ButtonStyle.Primary), UITheme.BaseColor(ButtonStyle.Ghost));
        Assert.IsFalse(UITheme.IsStrongStyle(ButtonStyle.Secondary));
    }

    [Test]
    public void DangerAndToggleStyles_UseDedicatedColors()
    {
        Assert.AreEqual(UITheme.DangerBg, UITheme.BaseColor(ButtonStyle.Danger));
        Assert.AreEqual(UITheme.Selected, UITheme.SelectedColor(ButtonStyle.Toggle));
        Assert.AreNotEqual(UITheme.BaseColor(ButtonStyle.Danger), UITheme.BaseColor(ButtonStyle.Secondary));
    }

    [Test]
    public void ColorBlocks_UseThemeFadeAndDimmedDisabledState()
    {
        var toggle = UITheme.BlockFor(ButtonStyle.Toggle);
        Assert.AreEqual(UITheme.ButtonFadeSeconds, toggle.fadeDuration, 0.0001f);
        Assert.Less(toggle.disabledColor.a, 1f, "禁用态应明显弱化");
        Assert.Greater(toggle.highlightedColor.r, toggle.normalColor.r, "悬停应比常态更亮");
        Assert.Less(toggle.pressedColor.r, toggle.normalColor.r, "按下应比常态更暗");
    }

    [Test]
    public void ErrorBannerColors_IncreaseInSeverity()
    {
        var info = UITheme.RelativeLuminance(UITheme.ErrorBannerColor(ErrorSeverity.Info));
        var warning = UITheme.RelativeLuminance(UITheme.ErrorBannerColor(ErrorSeverity.Warning));
        var error = UITheme.RelativeLuminance(UITheme.ErrorBannerColor(ErrorSeverity.Recoverable));
        Assert.AreNotEqual(info, warning);
        Assert.AreNotEqual(warning, error);
    }

    // ==================== 布局令牌与响应式折叠（方案 3.4 末条 / 15 章） ====================

    [Test]
    public void LayoutTokens_KeepMainBarAboveMinimumUsableHeight()
    {
        Assert.Greater(UITheme.Layout.MainBarHeightNormal, UITheme.Layout.MainBarHeightCompact);
        Assert.GreaterOrEqual(UITheme.Layout.MainBarHeightCompact, UITheme.HeightPrimary);
        Assert.Greater(UITheme.Layout.ToolbarHeightExpanded, UITheme.Layout.ToolbarHeightCollapsed);
        Assert.Greater(UITheme.Layout.AnalysisPanelWidth, 0f);
    }

    [Test]
    public void LayoutTokens_FitAtAllRequiredResolutions()
    {
        // 1280×720 / 1366×768 / 1920×1080 均为 16:9：参考分辨率下宽度都是 1280
        const float refWidth = 1280f;
        Assert.IsFalse(UITheme.IsNarrowReferenceWidth(refWidth), "16:9 下不应判定为窄窗口");

        float bottomStack = UITheme.Layout.ScreenMargin + UITheme.Layout.MainBarHeightNormal +
                            UITheme.Space8 + UITheme.Layout.ToolbarHeightExpanded + UITheme.Space8;
        float topStack = UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space8;
        float free = UITheme.Layout.ReferenceResolution.y - bottomStack - topStack;
        Assert.Greater(free, 300f, "3D 区域至少应保留 300 参考像素，不被控制栏过度遮盖");
    }

    [Test]
    public void ShouldFoldPracticeTools_FoldsOnNarrowOrCloseup()
    {
        Assert.IsFalse(UITheme.ShouldFoldPracticeTools(1280f, false), "宽窗口 + 非特写应展开");
        Assert.IsTrue(UITheme.ShouldFoldPracticeTools(1280f, true), "手部特写应自动折叠练习工具");
        Assert.IsTrue(UITheme.ShouldFoldPracticeTools(960f, false), "窄窗口应自动折叠");
        Assert.IsFalse(UITheme.ShouldFoldPracticeTools(1920f, false));
    }

    [Test]
    public void NarrowWidthThreshold_SitsBetweenCommonWindowSizes()
    {
        Assert.IsTrue(UITheme.IsNarrowReferenceWidth(960f), "4:3 窗口（1024×768）应判为窄");
        Assert.IsFalse(UITheme.IsNarrowReferenceWidth(1180f));
        Assert.IsFalse(UITheme.IsNarrowReferenceWidth(1280f));
        Assert.Greater(UITheme.Layout.NarrowWidthThreshold, 900f);
        Assert.Less(UITheme.Layout.NarrowWidthThreshold, 1280f);
    }

    // ==================== 状态机助手（方案 3.4 / 6.2 / 审查 2.2C） ====================

    [Test]
    public void PlayButtonLabel_ReflectsStateAndLoading()
    {
        Assert.AreEqual("暂停", UITheme.PlayButtonLabel(LessonState.Playing, PlaybackState.Playing));
        Assert.AreEqual("播放", UITheme.PlayButtonLabel(LessonState.Ready, PlaybackState.Stopped));
        Assert.AreEqual("播放", UITheme.PlayButtonLabel(LessonState.Paused, PlaybackState.Paused));
        StringAssert.Contains("加载", UITheme.PlayButtonLabel(LessonState.Loading, PlaybackState.Stopped));
    }

    [Test]
    public void CanTriggerSongActions_BlocksWhenNoSongLoadingOrError()
    {
        Assert.IsTrue(UITheme.CanTriggerSongActions(LessonState.Ready, true));
        Assert.IsTrue(UITheme.CanTriggerSongActions(LessonState.Playing, true));
        Assert.IsTrue(UITheme.CanTriggerSongActions(LessonState.Paused, true));

        Assert.IsFalse(UITheme.CanTriggerSongActions(LessonState.NoSong, false));
        Assert.IsFalse(UITheme.CanTriggerSongActions(LessonState.Loading, true), "加载中必须防重复点击");
        Assert.IsFalse(UITheme.CanTriggerSongActions(LessonState.Error, true));
        Assert.IsFalse(UITheme.CanTriggerSongActions(LessonState.Ready, false), "无曲目时即使状态就绪也不能播放");
    }

    [Test]
    public void Describers_ReturnNonEmptyReadableText()
    {
        Assert.AreEqual("双手", UITheme.DescribePracticeHand(PracticeHand.Both));
        Assert.AreEqual("左手", UITheme.DescribePracticeHand(PracticeHand.LeftOnly));
        Assert.AreEqual("右手", UITheme.DescribePracticeHand(PracticeHand.RightOnly));

        Assert.AreEqual("手部特写", UITheme.DescribeCameraMode(CameraMode.Closeup));
        Assert.AreEqual("全景", UITheme.DescribeCameraMode(CameraMode.Full));
        Assert.AreEqual("自由观察", UITheme.DescribeCameraMode(CameraMode.Free));

        Assert.AreEqual("关闭", UITheme.DescribeHighlight(HighlightMode.Off));
        Assert.AreEqual("全部", UITheme.DescribeHighlight(HighlightMode.Both));

        Assert.AreEqual("播放中", UITheme.DescribePlayback(PlaybackState.Playing));
        Assert.AreEqual("尚无曲目", UITheme.DescribeLessonState(LessonState.NoSong));
    }

    [Test]
    public void StateGlyph_DistinguishesStatesWithoutColor()
    {
        Assert.AreEqual("▶", UITheme.StateGlyph(PlaybackState.Playing));
        Assert.AreEqual("❚❚", UITheme.StateGlyph(PlaybackState.Paused));
        Assert.AreEqual("■", UITheme.StateGlyph(PlaybackState.Stopped));
        Assert.AreNotEqual(UITheme.StateGlyph(PlaybackState.Playing), UITheme.StateGlyph(PlaybackState.Paused));
        Assert.AreNotEqual(UITheme.StateGlyph(LessonState.Loading), UITheme.StateGlyph(LessonState.Error));
    }

    [Test]
    public void HandEmptyHint_TellsUserWhatToDoNext()
    {
        // 只有右手音符的曲目（音高 >= 60 属右手）
        var notes = new List<MidiNoteEvent>
        {
            new MidiNoteEvent(60, 0f, 1f, 80, 0, 0),
            new MidiNoteEvent(64, 1f, 1f, 80, 0, 0)
        };
        var analysis = SongAnalyzer.Analyze(notes, null, 2f);
        Assert.AreEqual(0, analysis.LeftHandCount);
        Assert.Greater(analysis.RightHandCount, 0);

        string leftHint = UITheme.HandEmptyHint(PracticeHand.LeftOnly, analysis);
        Assert.IsNotEmpty(leftHint);
        StringAssert.Contains("左手", leftHint);
        Assert.IsEmpty(UITheme.HandEmptyHint(PracticeHand.RightOnly, analysis));
        Assert.IsEmpty(UITheme.HandEmptyHint(PracticeHand.Both, analysis));
        Assert.IsEmpty(UITheme.HandEmptyHint(PracticeHand.LeftOnly, null));
    }

    // ==================== 文案（方案 8.3 三要素 / 快捷键提示） ====================

    [Test]
    public void ComposeTooltip_AppendsShortcutAfterPurpose()
    {
        Assert.AreEqual("节拍器 · M", UITheme.ComposeTooltip("节拍器", "M"));
        Assert.AreEqual("重播", UITheme.ComposeTooltip("重播", null));
        Assert.AreEqual("重播", UITheme.ComposeTooltip("重播", ""));
        Assert.AreEqual("", UITheme.ComposeTooltip(null, "M"));
    }

    [Test]
    public void ComposeErrorBody_AlwaysCarriesNextStep()
    {
        var withNextStep = AppError.Recoverable(ErrorCode.MidiHeaderInvalid,
            "无法读取该 MIDI 文件。文件可能损坏或不是标准 MIDI 文件。请重新选择文件。",
            RecoveryAction.ChooseFile);
        Assert.AreEqual(withNextStep.UserMessage, UITheme.ComposeErrorBody(withNextStep));

        var withoutNextStep = AppError.Recoverable(ErrorCode.MidiHeaderInvalid,
            "MIDI 解析失败：文件头无效。", RecoveryAction.ChooseFile);
        string body = UITheme.ComposeErrorBody(withoutNextStep);
        StringAssert.Contains(withoutNextStep.UserMessage, body);
        StringAssert.Contains("下一步", body);
        StringAssert.Contains("重新选择文件", body, "应使用会话层给出的 RecoveryLabel");
    }

    [Test]
    public void ComposeErrorBody_HandlesEmptyMessageAndNoRecovery()
    {
        var noRecovery = AppError.Create(ErrorCode.Unknown, ErrorSeverity.Info, "已切回默认人物。");
        Assert.AreEqual("已切回默认人物。", UITheme.ComposeErrorBody(noRecovery));
        Assert.AreEqual("知道了", UITheme.ComposeErrorActionLabel(noRecovery));

        Assert.IsNotEmpty(UITheme.ComposeErrorBody(default(AppError)));
        Assert.IsTrue(UITheme.MessageContainsNextStep("请重新选择文件"));
        Assert.IsFalse(UITheme.MessageContainsNextStep("解析失败"));
    }

    [Test]
    public void ErrorSeverityLabels_AreDistinguishableByTextNotOnlyColor()
    {
        Assert.AreEqual("提示", UITheme.ErrorSeverityLabel(ErrorSeverity.Info));
        Assert.AreEqual("警告", UITheme.ErrorSeverityLabel(ErrorSeverity.Warning));
        Assert.AreEqual("错误", UITheme.ErrorSeverityLabel(ErrorSeverity.Recoverable));
        Assert.AreEqual("严重错误", UITheme.ErrorSeverityLabel(ErrorSeverity.Fatal));
        Assert.AreNotEqual(UITheme.ErrorSeverityGlyph(ErrorSeverity.Warning),
            UITheme.ErrorSeverityGlyph(ErrorSeverity.Recoverable));
    }

    // ==================== 格式化助手 ====================

    [Test]
    public void FormatTime_HandlesZeroNegativeAndMinutes()
    {
        Assert.AreEqual("00:00", UITheme.FormatTime(0f));
        Assert.AreEqual("01:05", UITheme.FormatTime(65f));
        Assert.AreEqual("10:00", UITheme.FormatTime(600f));
        Assert.AreEqual("00:00", UITheme.FormatTime(-3f), "负值按 0 处理，不显示异常字符");
        Assert.AreEqual("00:00", UITheme.FormatTime(float.NaN));
    }

    [Test]
    public void Truncate_KeepsLabelWithinBudget()
    {
        Assert.AreEqual("abc", UITheme.Truncate("abc", 5));
        Assert.AreEqual("", UITheme.Truncate(null, 5));
        string long1 = UITheme.Truncate("一二三四五六七八九十", 5);
        Assert.AreEqual(5, long1.Length);
        StringAssert.EndsWith("…", long1);
    }

    [Test]
    public void Percent_ClampsAndRounds()
    {
        Assert.AreEqual(83, UITheme.Percent(0.829f));
        Assert.AreEqual(0, UITheme.Percent(-1f));
        Assert.AreEqual(100, UITheme.Percent(2f));
        Assert.AreEqual(50, UITheme.Percent(0.5f));
    }

    // ==================== 深色工作台重设计：栅格 / 圆角 / 版面占比 ====================

    [Test]
    public void SpacingTokens_SnapToFourPixelGrid()
    {
        var values = new[]
        {
            UITheme.Space4, UITheme.Space8, UITheme.Space12, UITheme.Space16, UITheme.Space24,
            UITheme.Space32,
            UITheme.Layout.ScreenMargin, UITheme.Layout.TopBarPadding,
            UITheme.Layout.PlayerPadding, UITheme.Layout.PlayerRowGap,
            UITheme.Layout.ChipGapInner, UITheme.Layout.ChipGapGroup,
            UITheme.Layout.HintCardWidth, UITheme.Layout.HintCardHeight,
            UITheme.Layout.SongCardWidth, UITheme.Layout.SongCardHeight, UITheme.Layout.SongCardGap,
            UITheme.Layout.TimeLabelWidth, UITheme.Layout.PrimaryActionWidth,
            UITheme.Layout.ProgressHandleSize, UITheme.Layout.TooltipPadX, UITheme.Layout.TooltipPadY
        };
        foreach (float v in values)
            Assert.AreEqual(0f, Mathf.Repeat(v, 4f), 0.001f, $"间距 {v} 必须是 4 的倍数");
    }

    [Test]
    public void ControlHeights_SnapToFourPixelGrid()
    {
        var heights = new[]
        {
            UITheme.HeightPrimary, UITheme.HeightButton, UITheme.HeightInput, UITheme.HeightSmall,
            UITheme.SizeIconButton,
            UITheme.Layout.StatusBarHeight, UITheme.Layout.MainBarHeightNormal,
            UITheme.Layout.MainBarHeightCompact, UITheme.Layout.PlayerProgressRowHeight,
            UITheme.Layout.PlayerControlRowHeight, UITheme.Layout.ChipHeight
        };
        foreach (float h in heights)
            Assert.AreEqual(0f, Mathf.Repeat(h, 4f), 0.001f, $"控件高度 {h} 必须是 4 的倍数");
    }

    [Test]
    public void RadiusScale_MatchesChipButtonPanelHierarchy()
    {
        // 进度条 3 < chip 8 < 按钮 10 < 面板 12：一套圆角只表达一套层级
        Assert.Less(UITheme.RadiusBar, UITheme.RadiusSmall);
        Assert.Less(UITheme.RadiusSmall, UITheme.RadiusMedium);
        Assert.Less(UITheme.RadiusMedium, UITheme.RadiusLarge);
        Assert.AreEqual(8, UITheme.RadiusSmall);
        Assert.AreEqual(10, UITheme.RadiusMedium);
        Assert.AreEqual(12, UITheme.RadiusLarge);
        Assert.LessOrEqual(UITheme.RadiusLarge, 12, "面板圆角不应做成大圆角（工作台而非卡片风）");
    }

    [Test]
    public void FontScale_UsesDocumentedSixSteps()
    {
        Assert.AreEqual(28, UITheme.FontPageTitle, "hero");
        Assert.AreEqual(20, UITheme.FontTitle, "title");
        Assert.AreEqual(15, UITheme.FontSubtitle, "subtitle");
        Assert.AreEqual(13, UITheme.FontBody, "body");
        Assert.AreEqual(12, UITheme.FontBodySmall, "body-small");
        Assert.AreEqual(10, UITheme.FontCaption, "caption");

        // 行高 = 字号 × 1.35（取整到整数像素）
        foreach (int size in new[] { 10, 12, 13, 15, 20, 28 })
        {
            float line = UITheme.LineHeight(size);
            Assert.GreaterOrEqual(line, size * 1.3f, $"{size} 号字的行高过小");
            Assert.LessOrEqual(line, size * 1.4f, $"{size} 号字的行高过大");
        }
    }

    [Test]
    public void PlayerBar_OccupiesAtMostTwentyTwoPercentOfScreenHeight()
    {
        float refH = UITheme.Layout.ReferenceResolution.y;
        float occupied = UITheme.Layout.ScreenMargin + UITheme.Layout.MainBarHeightNormal;
        Assert.LessOrEqual(occupied / refH, 0.22f, "底栏（含外边距）不得超过屏高 22%");
        Assert.LessOrEqual(occupied, 158f, "720 高参考分辨率下的硬上限");

        // 左右各留 ≥16，播放区不会被两侧面板侵入
        Assert.GreaterOrEqual(UITheme.Layout.ScreenMargin, 16f);
    }

    [Test]
    public void PlayerBar_KeepsPlayAreaUncovered()
    {
        float refH = UITheme.Layout.ReferenceResolution.y;
        float top = UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight;
        float bottom = UITheme.Layout.ScreenMargin + UITheme.Layout.MainBarHeightNormal;
        float stage = refH - top - bottom;
        Assert.GreaterOrEqual(stage, 480f, "顶栏 + 底栏之外必须留出 ≥480px 的演奏区");
        Assert.GreaterOrEqual(stage / refH, 0.65f, "演奏区至少占屏高 65%");
    }

    [Test]
    public void PlayerBar_InternalRowsAddUpExactly()
    {
        // 132 = 16 上内边距 + 44 进度行 + 16 行距 + 40 控制行 + 16 下内边距
        float sum = UITheme.Layout.PlayerPadding + UITheme.Layout.PlayerProgressRowHeight +
                    UITheme.Layout.PlayerRowGap + UITheme.Layout.PlayerControlRowHeight +
                    UITheme.Layout.PlayerPadding;
        Assert.AreEqual(UITheme.Layout.MainBarHeightNormal, sum, 0.001f,
            "底栏高度必须由内部行高精确相加得出，不允许出现无法解释的留白");

        // 紧凑态：隐藏进度行，只留控制行 + 上下内边距
        float compact = UITheme.Layout.PlayerPadding * 2f + UITheme.Layout.PlayerControlRowHeight;
        Assert.AreEqual(UITheme.Layout.MainBarHeightCompact, compact, 0.001f);
    }

    [Test]
    public void TopBar_IsSingleRowAndVerticallyCentered()
    {
        Assert.AreEqual(56f, UITheme.Layout.StatusBarHeight, "顶栏固定 56 高（单行）");
        float leftover = UITheme.Layout.StatusBarHeight - UITheme.SizeIconButton;
        Assert.Greater(leftover, 0f);
        Assert.AreEqual(0f, Mathf.Repeat(leftover * 0.5f, 4f), 0.001f, "32 高控件应能整像素垂直居中");

        // 同高、同内边距：图标按钮与 chip 同高（32），间距 8
        Assert.AreEqual(UITheme.SizeIconButton, UITheme.Layout.ChipHeight);
        Assert.AreEqual(UITheme.HeightSmall, UITheme.Layout.ChipHeight);
        Assert.AreEqual(8f, UITheme.Layout.ChipGapInner);
    }

    [Test]
    public void HintCard_StaysSmallAndSemiTransparent()
    {
        Assert.LessOrEqual(UITheme.Layout.HintCardWidth, 240f, "提示卡只占左上角，不进入演奏区中心");
        Assert.LessOrEqual(UITheme.Layout.HintCardHeight, 96f);
        Assert.GreaterOrEqual(UITheme.Layout.HintCardFadeDelay, 2f, "移出后至少停留 2s 再淡出");
        Assert.Greater(UITheme.Layout.HintCardIdleAlpha, 0.4f, "淡出后仍必须可读（不完全隐藏）");
        Assert.Less(UITheme.Layout.HintCardIdleAlpha, 1f);

        // 玻璃卡必须比面板更透，且比「淡出后的自己」更实
        Assert.Less(UITheme.PanelBgGlass.a, UITheme.PanelBg.a);
        Assert.Greater(UITheme.PanelBgGlass.a, UITheme.Layout.HintCardIdleAlpha);
    }

    [Test]
    public void SongGrid_FitsAtLeastThreeColumnsAtReferenceWidth()
    {
        float avail = UITheme.Layout.ReferenceResolution.x - UITheme.Layout.ScreenMargin * 2f;
        float gap = UITheme.Layout.SongCardGap;
        int columns = Mathf.FloorToInt((avail + gap) / (UITheme.Layout.SongCardWidth + gap));
        Assert.GreaterOrEqual(columns, 3, "1280 宽下曲库至少 3 列");
        Assert.GreaterOrEqual(columns, UITheme.Layout.SongCardMinColumns);

        // 卡片高度能容纳标题（15）+ 元信息（12）+ 上下 16 内边距
        float content = UITheme.LineHeight(UITheme.FontSubtitle) + UITheme.LineHeight(UITheme.FontBodySmall) +
                        UITheme.Space16 * 2f;
        Assert.LessOrEqual(content, UITheme.Layout.SongCardHeight, "卡片高度必须容纳两行文字与内边距");
    }

    [Test]
    public void ProgressAndChips_UseDocumentedGeometry()
    {
        Assert.AreEqual(6f, UITheme.Layout.ProgressBarHeight);
        Assert.AreEqual(3, UITheme.RadiusBar);
        Assert.AreEqual(12f, UITheme.Layout.ProgressHandleSize);
        Assert.AreEqual(44f, UITheme.Layout.TimeLabelWidth);
        Assert.AreEqual(96f, UITheme.Layout.PrimaryActionWidth);
        Assert.AreEqual(UITheme.HeightButton, UITheme.Layout.PlayerControlRowHeight,
            "主按钮与控制行同高（40），保证行内基线一致");
    }

    [Test]
    public void GlassPanel_KeepsSecondaryTextReadable()
    {
        // 玻璃卡上仍有 12 号快捷说明，必须保持可读（≥3:1）
        Assert.GreaterOrEqual(UITheme.ContrastRatio(UITheme.TextSecondary, UITheme.PanelBg), 3f);
        Assert.Greater(UITheme.ContrastRatio(UITheme.TextPrimary, UITheme.PanelBgGlass), 4.5f);
        Assert.Greater(UITheme.ContrastRatio(UITheme.TextSecondary, UITheme.PanelBgGlass), 3f);
    }
}
