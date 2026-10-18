using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.Tests.PlayMode;

/// <summary>
/// 视觉快照测试（方案 11.2「视觉回归」+ 审查 6.1「画面基线组合 V01–V09」）。
/// <para>
/// 渲染课程场景的多个视角 / 角色 / 状态并保存 PNG，同时把每张图的元数据
/// （Unity 版本、项目版本锁定值、窗口尺寸、角色名与索引、曲目名、播放时间、相机状态、检查要点）
/// **自动写入** <c>Docs/screenshots/manifest.md</c>，保证清单与截图始终一致（不手写死）。
/// </para>
/// <para>
/// <b>产出覆盖</b>：
/// <list type="bullet">
/// <item>延续既有 6 张基线：<c>01_full.png</c> … <c>06_avatar_cartoon.png</c>（文件名与产出保持不变）；</item>
/// <item>新增审查 6.1 的 V01–V09 与方案 11.2 的 V10–V12，共 12 张。</item>
/// </list>
/// </para>
/// <para>
/// 无图形设备（例如 <c>-nographics</c>）时 <see cref="Assert.Ignore(string)"/>；带 GPU 运行时用
/// <c>Unity.exe -batchmode -runTests -testPlatform PlayMode -testFilter CaptureLessonViews</c>
/// 手动执行（**不要**加 <c>-nographics</c>）。
/// </para>
/// <para>
/// <b>像素差异只作辅助</b>：结果由人看图确认后才可复制进 <c>Docs/screenshots/baseline/</c>；
/// 自动比对见 <c>VisualBaselineCompareTests</c> 与 <c>Tools/visual_diff.py</c>。
/// </para>
/// </summary>
[Category("Visual")]
public class VisualSnapshotTests
{
    private const int W = 1280;
    private const int H = 720;

    /// <summary>读不到 ProjectSettings/ProjectVersion.txt 时的兜底版本锁定值。</summary>
    private const string ProjectVersionFallback = "2022.3.22f1c1";

    [UnityTest]
    public IEnumerator CaptureLessonViews()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Assert.Ignore("视觉快照需要图形设备（请去掉 -nographics 运行）");
            yield break;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "screenshots"));
        Directory.CreateDirectory(dir);

        // 方案 11.2 / 审查 6.1：固定窗口尺寸，并把「实际值」写进 manifest（不假设一定是 1280×720）
        Screen.SetResolution(W, H, false);
        yield return null;

        SceneManager.LoadScene("PianoLesson");
        yield return null;
        yield return new WaitForSeconds(4f);

        LessonSessionController session = UnityEngine.Object.FindFirstObjectByType<LessonSessionController>();
        Camera cam = Camera.main;
        CameraModeController cameraCtrl = UnityEngine.Object.FindFirstObjectByType<CameraModeController>();
        AvatarManager avatar = UnityEngine.Object.FindFirstObjectByType<AvatarManager>();
        PianoKeyMap keyMap = UnityEngine.Object.FindFirstObjectByType<PianoKeyMap>();
        MidiEventScheduler scheduler = UnityEngine.Object.FindFirstObjectByType<MidiEventScheduler>();
        LessonTimeline timeline = UnityEngine.Object.FindFirstObjectByType<LessonTimeline>();

        Assert.IsNotNull(session, "LessonSessionController 未装配");
        Assert.IsNotNull(cam, "主摄像机缺失");
        Assert.IsNotNull(cameraCtrl, "CameraModeController 未装配");
        Assert.IsNotNull(avatar, "AvatarManager 未装配");
        Assert.IsNotNull(timeline, "LessonTimeline 未装配");
        Assert.IsNotNull(keyMap, "PianoKeyMap 未装配");
        Assert.IsTrue(keyMap.TryGetKey(PianoKeyMap.MinPitch, out Transform lowestKey) && lowestKey != null,
            "琴键映射不完整：缺少最低音 A0(21) 的键根，视觉回归无法判断「全景是否完整展示 88 键」");
        Assert.IsTrue(keyMap.TryGetKey(PianoKeyMap.MaxPitch, out Transform highestKey) && highestKey != null,
            "琴键映射不完整：缺少最高音 C8(108) 的键根，视觉回归无法判断「全景是否完整展示 88 键」");

        var records = new List<ShotRecord>();
        var skipped = new List<string>();

        // ==================================================================
        // 一、既有 6 张基线（文件名与产出保持不变，不破坏既有基线）
        // ==================================================================

        yield return Capture(cam, Path.Combine(dir, "01_full.png"));
        records.Add(Snapshot("01_full.png", "方案 11.2-2", "标准全景：键盘完整、低音端无遮挡、人物完整入画",
            null, session, cameraCtrl, avatar, timeline));

        session.SetCameraMode(CameraMode.Closeup);
        yield return new WaitForSeconds(2.5f);
        yield return Capture(cam, Path.Combine(dir, "02_closeup.png"));
        records.Add(Snapshot("02_closeup.png", "方案 11.2-3", "手部特写（白键旋律）：掌心、指尖与相应琴键同时可见",
            "高亮默认关闭，特写按活动手范围取景", session, cameraCtrl, avatar, timeline));

        session.SetCameraMode(CameraMode.Full);
        session.Play();
        yield return new WaitForSeconds(3f);
        yield return Capture(cam, Path.Combine(dir, "03_playing.png"));
        records.Add(Snapshot("03_playing.png", "方案 11.2-2", "播放中全景：琴键动作与高亮同步、人物无穿模",
            null, session, cameraCtrl, avatar, timeline));

        session.CyclePracticeHand(); // 左手练习
        yield return new WaitForSeconds(1.5f);
        yield return Capture(cam, Path.Combine(dir, "04_practice_left.png"));
        records.Add(Snapshot("04_practice_left.png", "方案 11.2-6", "分手练习开启：相机聚焦活动手",
            null, session, cameraCtrl, avatar, timeline));

        session.CycleAvatar();       // 写实男性
        session.CyclePracticeHand(); // 回到右手/双手循环
        yield return new WaitForSeconds(2f);
        yield return Capture(cam, Path.Combine(dir, "05_avatar_man.png"));
        records.Add(Snapshot("05_avatar_man.png", "方案 11.2-8", "不同人物切换后的同一机位：构图规律一致",
            null, session, cameraCtrl, avatar, timeline));

        session.CycleAvatar();       // 卡通
        yield return new WaitForSeconds(2f);
        yield return Capture(cam, Path.Combine(dir, "06_avatar_cartoon.png"));
        records.Add(Snapshot("06_avatar_cartoon.png", "方案 11.2-8", "不同人物切换后的同一机位：构图规律一致",
            null, session, cameraCtrl, avatar, timeline));

        // ==================================================================
        // 二、V01–V12：方案 11.2 的固定用例 + 审查 6.1 的画面基线组合
        // ==================================================================

        // 归位到确定性的初始状态，避免上一段用例的残留影响
        session.Stop();
        session.ClearLoop();
        session.SetHighlightMode(HighlightMode.Off);
        session.SwitchAvatar(0);
        session.SetPracticeHand(PracticeHand.Both);
        yield return new WaitForSeconds(1.0f);

        float total = timeline.TotalDuration;
        var notes = NoteScan.CollectAllNotes(scheduler, total);
        string contentNote = notes.Count == 0
            ? "曲内未扫描到音符（曲目未加载？），本图改用固定时间比例（曲目内容限制）"
            : null;

        // ---- V01 手部特写 + 双手中音区和弦（审查 6.1 V01 / 方案 11.2-4）----
        session.SetCameraMode(CameraMode.Closeup);
        string v01Note = contentNote;
        float v01Time = total * 0.25f;
        if (notes.Count > 0 && NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float chordTime, out int chordSize))
        {
            v01Time = chordTime;
            v01Note = string.Format("曲内同时发声 {0} 音的中音区和弦（t={1:F2}s）", chordSize, chordTime);
        }
        else if (v01Note == null)
        {
            v01Note = "曲内未找到同时发声 ≥2 音的中音区和弦，退化为曲目 25% 处（曲目内容限制）";
        }
        session.Seek(v01Time);
        session.Play();
        yield return new WaitForSeconds(0.9f);
        session.Pause();                              // 暂停使手型固定，便于逐次回归对比
        yield return new WaitForSeconds(1.6f);        // 等相机收敛
        yield return Capture(cam, Path.Combine(dir, "V01_closeup_chord.png"));
        records.Add(Snapshot("V01_closeup_chord.png", "审查 6.1 V01 / 方案 11.2-4",
            "掌心、指尖、琴键可见；指法自然", v01Note, session, cameraCtrl, avatar, timeline));

        // ---- V02 手部特写 + 低音到高音大跨度跳跃（审查 6.1 V02 / 3.2-1）----
        string v02Note = contentNote;
        float v02Time = total * 0.5f;
        if (notes.Count > 0 && NoteScan.TryFindWidestSpanTime(notes, 0.5f, out float spanTime, out int span, out int spanLow, out int spanHigh))
        {
            v02Time = spanTime;
            v02Note = string.Format(
                "曲内 0.5s 窗口最大音域跨度 {0} 半音（pitch {1}–{2}，t={3:F2}s）；内置示例曲音域有限，跨度不足属曲目内容限制",
                span, spanLow, spanHigh, spanTime);
        }
        else if (v02Note == null)
        {
            v02Note = "曲内未找到可判定跨度的片段，退化为曲目 50% 处（曲目内容限制）";
        }
        session.SetCameraMode(CameraMode.Closeup);
        session.Seek(v02Time);
        session.Play();
        yield return new WaitForSeconds(0.9f);
        session.Pause();
        yield return new WaitForSeconds(1.8f);
        yield return Capture(cam, Path.Combine(dir, "V02_closeup_wide_leap.png"));
        records.Add(Snapshot("V02_closeup_wide_leap.png", "审查 6.1 V02 / 3.2-1",
            "动态取景范围与阻尼：两手分开时画面不突然缩放", v02Note, session, cameraCtrl, avatar, timeline));

        // ---- V03 全景 + 手臂明显运动的片段（审查 6.1 V03）----
        session.SwitchAvatar(1); // 写实男性
        string v03Note = contentNote;
        float v03Time = total * 0.6f;
        if (notes.Count > 0 && NoteScan.TryFindLargestLeapTime(notes, out float leapTime, out int leap))
        {
            v03Time = leapTime;
            v03Note = string.Format("曲内最大音程跳进 {0} 半音（t={1:F2}s），手臂运动在该片段最明显", leap, leapTime);
        }
        else if (v03Note == null)
        {
            v03Note = "曲内未找到跳进片段，退化为曲目 60% 处（曲目内容限制）";
        }
        session.SetCameraMode(CameraMode.Full);
        session.Seek(v03Time);
        session.Play();
        yield return new WaitForSeconds(2.5f);
        yield return Capture(cam, Path.Combine(dir, "V03_full_arm_motion.png"));
        records.Add(Snapshot("V03_full_arm_motion.png", "审查 6.1 V03",
            "坐姿、肩臂、完整键盘", v03Note, session, cameraCtrl, avatar, timeline));

        // ---- V04 全景 + 卡通角色（审查 6.1 V04）----
        session.SwitchAvatar(2); // 卡通角色
        session.SetCameraMode(CameraMode.Full);
        session.Seek(total * 0.4f);
        session.Play();
        yield return new WaitForSeconds(2.5f);
        yield return Capture(cam, Path.Combine(dir, "V04_full_cartoon.png"));
        records.Add(Snapshot("V04_full_cartoon.png", "审查 6.1 V04",
            "风格一致与取景适配", "中等速度片段（曲目 40% 处）", session, cameraCtrl, avatar, timeline));

        // ---- V05 左手练习（特写）（审查 6.1 V05 / 3.2-3）----
        session.SwitchAvatar(0);
        session.SetCameraMode(CameraMode.Closeup);
        bool hasLeftHand = session.Analysis != null && session.Analysis.LeftHandCount > 0;
        if (hasLeftHand)
        {
            float v05Time = notes.Count > 0
                ? NoteScan.FirstStartTimeByHand(notes, leftHand: true, fallback: total * 0.3f)
                : total * 0.3f;
            session.SetPracticeHand(PracticeHand.LeftOnly);
            session.Seek(v05Time);
            session.Play();
            yield return new WaitForSeconds(0.9f);
            session.Pause();
            yield return new WaitForSeconds(1.6f);
            yield return Capture(cam, Path.Combine(dir, "V05_left_hand_only.png"));
            records.Add(Snapshot("V05_left_hand_only.png", "审查 6.1 V05 / 3.2-3",
                "相机聚焦活动手", string.Format("左手独立音型（t={0:F2}s）", v05Time),
                session, cameraCtrl, avatar, timeline));
        }
        else
        {
            const string reason = "V05 左手练习（特写）：当前曲目左手音符数为 0，无法生成该用例";
            skipped.Add(reason);
            records.Add(Snapshot("V05_left_hand_only.png", "审查 6.1 V05 / 3.2-3",
                "相机聚焦活动手", "未生成：曲目左手音符数为 0", session, cameraCtrl, avatar, timeline)
                .WithStatus("跳过：" + reason));
        }

        // ---- V06 右手练习（特写）（审查 6.1 V06 / 3.2-4）----
        session.SwitchAvatar(0);
        session.SetCameraMode(CameraMode.Closeup);
        bool hasRightHand = session.Analysis != null && session.Analysis.RightHandCount > 0;
        if (hasRightHand)
        {
            float v06Time = notes.Count > 0
                ? NoteScan.FirstStartTimeByHand(notes, leftHand: false, fallback: total * 0.3f)
                : total * 0.3f;
            session.SetPracticeHand(PracticeHand.RightOnly);
            session.Seek(v06Time);
            session.Play();
            yield return new WaitForSeconds(0.9f);
            session.Pause();
            yield return new WaitForSeconds(1.6f);
            yield return Capture(cam, Path.Combine(dir, "V06_right_hand_only.png"));
            records.Add(Snapshot("V06_right_hand_only.png", "审查 6.1 V06 / 3.2-4",
                "相机聚焦活动手", string.Format("右手独立音型（t={0:F2}s）", v06Time),
                session, cameraCtrl, avatar, timeline));
        }
        else
        {
            const string reason = "V06 右手练习（特写）：当前曲目右手音符数为 0，无法生成该用例";
            skipped.Add(reason);
            records.Add(Snapshot("V06_right_hand_only.png", "审查 6.1 V06 / 3.2-4",
                "相机聚焦活动手", "未生成：曲目右手音符数为 0", session, cameraCtrl, avatar, timeline)
                .WithStatus("跳过：" + reason));
        }

        // ---- V07 自由观察（暂停态）（审查 6.1 V07）----
        session.SetPracticeHand(PracticeHand.Both);
        session.SetCameraMode(CameraMode.Free);
        session.Pause();
        yield return new WaitForSeconds(1.5f);
        yield return Capture(cam, Path.Combine(dir, "V07_free_orbit_paused.png"));
        records.Add(Snapshot("V07_free_orbit_paused.png", "审查 6.1 V07",
            "旋转/缩放、UI 鼠标操作边界",
            "测试环境无法注入真实鼠标右键拖动：本图记录「进入自由观察 → 暂停」后的初始构图（进入时不跳变）",
            session, cameraCtrl, avatar, timeline));

        // ---- V08 播放中连续切换视角后的稳定构图（审查 6.1 V08）----
        session.SetCameraMode(CameraMode.Full);
        session.Seek(total * 0.35f);
        session.Play();
        yield return new WaitForSeconds(1.5f);
        CameraMode[] switchSequence =
        {
            CameraMode.Closeup, CameraMode.Full, CameraMode.Free, CameraMode.Closeup, CameraMode.Full
        };
        for (int i = 0; i < switchSequence.Length; i++)
        {
            session.SetCameraMode(switchSequence[i]);
            yield return null;
            yield return null;
        }
        float timeBeforeSwitchWait = timeline.CurrentTime;
        yield return new WaitForSeconds(3f);
        bool settled = !cameraCtrl.IsTransitioning;
        string v08Note = string.Format(
            "播放中连续切换视角 {0} 次后等待 3s：IsTransitioning={1}（期望 False）、时间从 {2:F2}s 推进到 {3:F2}s（时间轴连续）",
            switchSequence.Length, settled, timeBeforeSwitchWait, timeline.CurrentTime);
        yield return Capture(cam, Path.Combine(dir, "V08_switch_settled.png"));
        records.Add(Snapshot("V08_switch_settled.png", "审查 6.1 V08",
            "最终构图稳定、时间轴连续", v08Note, session, cameraCtrl, avatar, timeline));

        // ---- V09 手部特写 + A-B 循环（跳转后构图）（审查 6.1 V09）----
        session.SetCameraMode(CameraMode.Closeup);
        session.SetPracticeHand(PracticeHand.Both);
        float loopA = Mathf.Clamp(total * 0.35f, 0f, Mathf.Max(0f, total - 0.6f));
        float loopB = loopA + 0.6f;
        session.SetLoop(loopA, loopB);
        session.Seek(loopA + 0.2f);
        session.Play();
        yield return new WaitForSeconds(0.7f);   // 越过 B 点，触发循环跳回 A
        yield return new WaitForSeconds(1.6f);   // 跳转后等相机收敛（不追旧目标）
        string v09Note = string.Format(
            "A-B 循环 [{0:F2}s, {1:F2}s]，IsLooping={2}；等待跨越 B 点后再等 1.6s，此刻时间 {3:F2}s",
            loopA, loopB, session.IsLooping, timeline.CurrentTime);
        yield return Capture(cam, Path.Combine(dir, "V09_closeup_loop.png"));
        records.Add(Snapshot("V09_closeup_loop.png", "审查 6.1 V09",
            "循环跳转后手位和相机不追旧目标", v09Note, session, cameraCtrl, avatar, timeline));
        session.ClearLoop();

        // ---- V11 高亮提示开启（暖金预提示 + 蓝/橙发声）（方案 11.2-5）----
        session.SetCameraMode(CameraMode.Full);
        session.SetPracticeHand(PracticeHand.Both);
        float previousLookAhead = session.HighlightLookAhead;
        session.HighlightLookAhead = 1.2f;   // 临时放大提前量，使同一帧里既有「发声」也有「预提示」
        session.SetHighlightMode(HighlightMode.Both);
        float v11Time = notes.Count > 0 && NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float hq, out _)
            ? Mathf.Max(0f, hq - 0.1f)
            : total * 0.3f;
        session.Seek(v11Time);
        session.Play();
        yield return new WaitForSeconds(0.35f);
        string v11Note = string.Format(
            "高亮模式 Both（暖金预提示 + 蓝/橙发声）；为同时呈现两种提示，临时把预提示提前量设为 {0:F1}s（原 {1:F1}s，截图后还原）",
            session.HighlightLookAhead, previousLookAhead);
        yield return Capture(cam, Path.Combine(dir, "V11_highlight_on.png"));
        records.Add(Snapshot("V11_highlight_on.png", "方案 11.2-5",
            "颜色语义正确（暖金=预提示、左蓝/右橙=发声），且高亮不遮挡手部", v11Note,
            session, cameraCtrl, avatar, timeline));
        session.HighlightLookAhead = previousLookAhead;
        session.SetHighlightMode(HighlightMode.Off);

        // ---- V12 曲目分析面板展开 + 开发诊断展开（方案 11.2-7）----
        session.SetCameraMode(CameraMode.Full);   // 手部特写会折叠练习工具栏，「曲目分析」按钮不可见
        session.SetPracticeHand(PracticeHand.Both);
        session.Seek(total * 0.3f);
        session.Play();
        yield return new WaitForSeconds(1.5f);
        bool analysisOpened = ClickButton("Btn_曲目分析");
        yield return new WaitForSeconds(1.2f);
        bool diagnosticsOpened = ClickButton("Btn_开发诊断");
        yield return new WaitForSeconds(1.2f);
        if (analysisOpened)
        {
            string v12Note = string.Format(
                "曲目分析面板展开={0}、开发诊断展开={1}（通过按钮 onClick 触发，未注入鼠标事件）；" +
                "展开诊断后主画面仍未被过度覆盖（本次仅截图，遮挡断言见 CameraVisibilityTests 3.2-2）",
                analysisOpened, diagnosticsOpened);
            yield return Capture(cam, Path.Combine(dir, "V12_analysis_expanded.png"));
            records.Add(Snapshot("V12_analysis_expanded.png", "方案 11.2-7",
                "分析面板展开；动作质量默认折叠在「开发诊断」区", v12Note,
                session, cameraCtrl, avatar, timeline));
        }
        else
        {
            const string reason = "V12 曲目分析面板展开：未找到可见且可点击的「曲目分析」按钮（Btn_曲目分析），无法生成该用例";
            skipped.Add(reason);
            records.Add(Snapshot("V12_analysis_expanded.png", "方案 11.2-7",
                "分析面板展开；动作质量默认折叠在「开发诊断」区", "未生成：找不到「曲目分析」按钮",
                session, cameraCtrl, avatar, timeline).WithStatus("跳过：" + reason));
        }

        // ---- V13 教学模式：隐藏手部，只高亮被按下的琴键（左手冷蓝 / 右手暖橙）----
        // 先收起分析面板（V12 展开过），避免遮挡键盘区域
        ClickButton("Btn_曲目分析");
        yield return new WaitForSeconds(0.6f);

        session.SetDisplayMode(DisplayMode.Teaching);
        session.SetCameraMode(CameraMode.Closeup);
        session.SetPracticeHand(PracticeHand.Both);
        float v13Time = notes.Count > 0 && NoteScan.TryFindChordTime(notes, 2, 48, 76, 0.06f, out float chord13, out int size13)
            ? Mathf.Max(0f, chord13 + 0.05f)
            : total * 0.25f;
        session.Stop();
        session.Seek(v13Time);
        session.Play();
        yield return new WaitForSeconds(0.4f);
        // 等到"左右手同时有键被按下"，这样一张图里能同时看到左手冷蓝与右手暖橙
        yield return WaitBothHandsPressed(session, 120);
        yield return Capture(cam, Path.Combine(dir, "V13_teaching_mode.png"));
        records.Add(Snapshot("V13_teaching_mode.png", "新增：教学模式 / 展示模式",
            "教学模式：手部动画隐藏，只有被按下的琴键高亮（左手冷蓝 · 右手暖橙）",
            string.Format("显示模式=教学，手部可见={0}，当前按下 {1} 个琴键、高亮 {2} 个琴键",
                avatar != null && avatar.HandsAndArmsVisible, session.PressedPitchCount, HighlightedKeyCount()),
            session, cameraCtrl, avatar, timeline));

        // ---- V14 同一时刻的展示模式（与 V13 对照：手部动画可见）----
        session.SetDisplayMode(DisplayMode.Showcase);
        session.Stop();
        session.Seek(v13Time);
        session.Play();
        yield return new WaitForSeconds(0.4f);
        yield return WaitBothHandsPressed(session, 120);
        yield return Capture(cam, Path.Combine(dir, "V14_showcase_mode.png"));
        records.Add(Snapshot("V14_showcase_mode.png", "新增：教学模式 / 展示模式",
            "展示模式：完整手部动画（与 V13 同机位、同曲目时刻，便于逐帧对照）",
            string.Format("显示模式=展示，手部可见={0}，当前按下 {1} 个琴键",
                avatar != null && avatar.HandsAndArmsVisible, session.PressedPitchCount),
            session, cameraCtrl, avatar, timeline));
        session.Pause();

        // ---- V10 主菜单 / 空曲库（方案 11.2-1）----
        // 放在最后：它会切换场景，避免影响上面所有课程场景截图
        session.Pause();
        session.Stop();
        SceneManager.LoadScene("MainMenu");
        yield return null;
        yield return new WaitForSeconds(2f);
        Camera menuCam = Camera.main;
        if (menuCam == null)
        {
            const string reason = "V10 主菜单：MainMenu 场景没有主摄像机，无法截图";
            skipped.Add(reason);
            records.Add(Snapshot("V10_mainmenu.png", "方案 11.2-1",
                "主菜单空状态可操作：导入入口可见、无曲目时不显示可播放按钮", "未生成：MainMenu 场景缺主摄像机",
                null, null, null, null).WithStatus("跳过：" + reason));
        }
        else
        {
            string songDir = Path.Combine(Application.streamingAssetsPath, "Songs");
            int songCount = CountSongFiles(songDir);
            string v10Note;
            if (songCount < 0)
                v10Note = "曲库目录缺失 → 主菜单显示「曲库不可用」错误态并提供导入入口";
            else if (songCount == 0)
                v10Note = "曲库为空 → 主菜单显示「曲库为空」空状态并提供导入入口";
            else
                v10Note = string.Format(
                    "曲库有 {0} 首示例曲 → 主菜单显示曲目卡片；空曲库态需清空 Assets/StreamingAssets/Songs 后重跑才能复现（本次未生成空状态图）",
                    songCount);

            yield return Capture(menuCam, Path.Combine(dir, "V10_mainmenu.png"));
            ShotRecord v10Record = Snapshot("V10_mainmenu.png", "方案 11.2-1",
                "主菜单空状态可操作：导入入口可见、无曲目时不显示可播放按钮", v10Note,
                null, null, null, null);
            v10Record.Avatar = "不适用（主菜单场景）";
            v10Record.AvatarIndex = -1;
            v10Record.Song = "不适用（未进入课程）";
            v10Record.Time = -1f;
            v10Record.CameraState = "不适用（主菜单场景无 CameraModeController）";
            records.Add(v10Record);
        }

        // ==================================================================
        // 三、自动生成 manifest.md（与本次实际截图严格一致）
        // ==================================================================
        WriteManifest(dir, records, skipped);

        if (skipped.Count > 0)
        {
            Assert.Ignore("以下用例未生成（原因已写入 Docs/screenshots/manifest.md 的「未生成 / 降级的用例」）：\n - "
                + string.Join("\n - ", skipped.ToArray()));
        }
    }

    // ==================================================================
    // 截图辅助（行为与改造前一致：Overlay Canvas 临时转 Screen Space Camera 后渲染）
    // ==================================================================

    /// <summary>同步渲染当前相机视图（含 Overlay UI）到 PNG。</summary>
    private static IEnumerator Capture(Camera cam, string path)
    {
        // Overlay Canvas 临时转为 Screen Space Camera，使其纳入相机渲染
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var saved = new List<(Canvas c, RenderMode mode, Camera wcam, float pd)>();
        foreach (var c in canvases)
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                saved.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 0.5f;
            }
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        Canvas.ForceUpdateCanvases();

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());

        cam.targetTexture = prev;
        RenderTexture.active = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);

        foreach (var s in saved)
        {
            s.c.renderMode = s.mode;
            s.c.worldCamera = s.wcam;
            s.c.planeDistance = s.pd;
        }
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// 点一下名字为 <paramref name="buttonName"/> 的按钮（直接触发 onClick，不注入鼠标事件）。
    /// <para>
    /// 注意：窄窗口（如批处理下的 640×480）会触发「练习工具栏自动折叠」，
    /// 被折叠面板里的按钮处于 <c>activeInHierarchy == false</c>，<see cref="GameObject.Find"/> 找不到它。
    /// 截图用例需要这些控件可见，因此这里会先精确查找（含未激活对象），再把父级链激活后再点击。
    /// </para>
    /// </summary>
    private static bool ClickButton(string buttonName)
    {
        GameObject go = GameObject.Find(buttonName);
        if (go == null) go = FindIncludingInactive(buttonName);
        if (go == null) return false;

        if (!go.activeInHierarchy) ActivateAncestors(go);

        Button button = go.GetComponent<Button>();
        if (button == null || !button.interactable) return false;

        Canvas.ForceUpdateCanvases();
        button.onClick.Invoke();
        return true;
    }

    /// <summary>按名字查找对象，**包含未激活对象**（排除资源对象）。</summary>
    private static GameObject FindIncludingInactive(string name)
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null || go.name != name) continue;
            if (!go.scene.IsValid()) continue;   // 排除 Asset / 预制体资源
            return go;
        }
        return null;
    }

    /// <summary>逐级激活父对象（不越过 Canvas 根），使被折叠面板内的控件可见。</summary>
    private static void ActivateAncestors(GameObject go)
    {
        Transform t = go.transform.parent;
        while (t != null)
        {
            if (t.GetComponent<Canvas>() != null) break;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    /// <summary>
    /// 等到「左右手同时有琴键被按下」的帧（最多 <paramref name="maxFrames"/> 帧）。
    /// 用于教学模式对照截图：同一张图里能同时看到左手冷蓝与右手暖橙。
    /// 超时则按当前状态返回（不失败——这是截图用例，不是断言用例）。
    /// </summary>
    private static IEnumerator WaitBothHandsPressed(LessonSessionController session, int maxFrames)
    {
        if (session == null) yield break;
        for (int i = 0; i < maxFrames; i++)
        {
            if (session.TryGetPressedPitchSpan(out int min, out int max)
                && PianoNoteFilter.IsLeftHand(min) && !PianoNoteFilter.IsLeftHand(max))
            {
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>当前被着色的琴键数量（高亮组件缺失时返回 -1，用于清单备注）。</summary>
    private static int HighlightedKeyCount()
    {
        var highlighter = UnityEngine.Object.FindFirstObjectByType<PianoKeyHighlighter>();
        return highlighter != null ? highlighter.HighlightedKeyCount : -1;
    }

    private static int CountSongFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return -1;
            string[] mid = Directory.GetFiles(directory, "*.mid");
            string[] midi = Directory.GetFiles(directory, "*.midi");
            return mid.Length + midi.Length;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    // ==================================================================
    // manifest.md 生成
    // ==================================================================

    /// <summary>单张截图的元数据行（方案 11.2 / 审查 6.1：每张图都要记录版本、尺寸、角色、曲目、时间与相机状态）。</summary>
    private sealed class ShotRecord
    {
        public string File = "—";
        public string Doc = "—";
        public string Check = "—";
        public string Note = "—";
        public string Avatar = "—";
        public int AvatarIndex = -1;
        public string Song = "—";
        public float Time = -1f;
        public string CameraState = "—";
        public string Window = "—";
        public string UnityVersion = "—";
        public string ProjectVersion = "—";
        public string Status = "已生成";

        public ShotRecord WithStatus(string status)
        {
            Status = status;
            return this;
        }
    }

    private static ShotRecord Snapshot(string file, string doc, string check, string note,
        LessonSessionController session, CameraModeController camera, AvatarManager avatar, LessonTimeline timeline)
    {
        var record = new ShotRecord
        {
            File = file,
            Doc = doc,
            Check = check,
            Note = string.IsNullOrEmpty(note) ? "—" : note,
            UnityVersion = Application.unityVersion,
            ProjectVersion = ReadProjectVersion(),
            Window = string.Format("{0}×{1}（请求 {2}×{3}）", Screen.width, Screen.height, W, H)
        };

        if (avatar != null)
        {
            record.Avatar = string.IsNullOrEmpty(avatar.CurrentName) ? "（未命名）" : avatar.CurrentName;
            record.AvatarIndex = avatar.CurrentIndex;
        }
        if (session != null)
        {
            record.Song = string.IsNullOrEmpty(session.CurrentSongName) ? "（无曲目）" : session.CurrentSongName;
        }
        if (timeline != null)
        {
            record.Time = timeline.CurrentTime;
        }
        if (camera != null)
        {
            record.CameraState = string.Format("{0}（Mode={1}, ModeLabel={2}）自由观察={3}{4}",
                camera.ModeLabel, camera.Mode, camera.ModeLabel,
                camera.Mode == CameraMode.Free,
                camera.IsUserOrbiting ? "，正在鼠标拖动旋转" : string.Empty);
        }
        return record;
    }

    private static string DescribeAvatarCell(ShotRecord record)
    {
        if (record.AvatarIndex < 0) return record.Avatar;
        if (record.Avatar.StartsWith("不适用", StringComparison.Ordinal)) return record.Avatar;
        return string.Format("{0}（索引 {1}）", record.Avatar, record.AvatarIndex);
    }

    private static string DescribeTimeCell(ShotRecord record)
        => record.Time < 0f ? "—" : record.Time.ToString("F2");

    private static string ReadProjectVersion()
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectVersion.txt");
            if (File.Exists(path))
            {
                const string key = "m_EditorVersion:";
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith(key, StringComparison.Ordinal))
                        return lines[i].Substring(key.Length).Trim();
                }
            }
        }
        catch (Exception)
        {
            // 读不到就退化为已知锁定值，不影响截图产出
        }
        return ProjectVersionFallback;
    }

    private static void WriteManifest(string dir, List<ShotRecord> records, List<string> skipped)
    {
        records.Sort((a, b) => SortKey(a.File).CompareTo(SortKey(b.File)));

        var sb = new StringBuilder();
        sb.AppendLine("# 视觉回归截图清单（Docs/screenshots/manifest.md）");
        sb.AppendLine();
        sb.AppendLine("> 本文件由 `Assets/Tests/PlayMode/VisualSnapshotTests.CaptureLessonViews` 在运行时**自动生成/覆盖**，");
        sb.AppendLine("> 请勿手工编辑 —— 手工内容会在下一次运行时被覆盖。");
        sb.AppendLine("> 对应文档：方案 **11.2 视觉回归**（10 个固定截图/场景用例）、**11.3/11.4 验收与自动化**、");
        sb.AppendLine("> **15 发布前检查清单**；审查 **3.2 视角可用性**、**6.1 画面基线组合 V01–V09**、**6.2 相机验收清单**。");
        sb.AppendLine();
        sb.AppendLine("- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("- 生成主机 Unity 版本：" + Application.unityVersion);
        sb.AppendLine("- 项目版本锁定值：" + ReadProjectVersion() + "（来源 ProjectSettings/ProjectVersion.txt）");
        sb.AppendLine("- 运行平台 / 图形设备：" + Application.platform + " / " + SystemInfo.graphicsDeviceType);
        sb.AppendLine("- 请求窗口尺寸：1280×720（实际尺寸见下表「窗口尺寸」列）");
        sb.AppendLine();
        sb.AppendLine("## 说明");
        sb.AppendLine();
        sb.AppendLine("像素差异**只作辅助**，不能替代人工看图（方案 11.2 末段 / 审查 6.1）：");
        sb.AppendLine("光照、材质、抗锯齿与显卡驱动的细微变化都会让同一构图的像素值改变，");
        sb.AppendLine("因此自动比对（`Tools/visual_diff.py` + `VisualBaselineCompareTests`，Explicit）");
        sb.AppendLine("必须配合 `--tolerance` / `--roi` 使用，并且**每次视觉改动都要人工核对**下列要点：");
        sb.AppendLine("关键 UI 不遮挡演奏区域、手部没有明显穿模、角色未被裁切、颜色语义正确。");
        sb.AppendLine();
        sb.AppendLine("## 截图清单");
        sb.AppendLine();
        sb.AppendLine("| 文件名 | 对应文档 | 角色 | 曲目 | 播放时间(s) | 相机状态 | 窗口尺寸 | Unity 版本 | 项目版本锁定 | 检查要点 | 备注 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        for (int i = 0; i < records.Count; i++)
        {
            ShotRecord r = records[i];
            sb.Append("| ").Append(Cell(r.File))
              .Append(" | ").Append(Cell(r.Doc))
              .Append(" | ").Append(Cell(DescribeAvatarCell(r)))
              .Append(" | ").Append(Cell(r.Song))
              .Append(" | ").Append(Cell(DescribeTimeCell(r)))
              .Append(" | ").Append(Cell(r.CameraState))
              .Append(" | ").Append(Cell(r.Window))
              .Append(" | ").Append(Cell(r.UnityVersion))
              .Append(" | ").Append(Cell(r.ProjectVersion))
              .Append(" | ").Append(Cell(r.Check))
              .Append(" | ").Append(Cell(r.Note))
              .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## 如何新增 / 更新基线");
        sb.AppendLine();
        sb.AppendLine("1. 在有 GPU 的机器上生成截图：跑 `python Tools/verify.py --visual`（该阶段**不加** `-nographics`，需要真实 GPU 渲染），");
        sb.AppendLine("   或直跑 `Unity.exe -batchmode -runTests -testPlatform PlayMode -testFilter CaptureLessonViews`（**不要**加 `-nographics`）。");
        sb.AppendLine("2. **人工看图**确认构图、遮挡、穿模与颜色语义无误（方案 11.2 末段 / 审查 6.1）—— 这一步不可省略。");
        sb.AppendLine("3. 把确认无误的图复制到 `Docs/screenshots/baseline/`（同名覆盖），再提交 `baseline/` 与本文件。");
        sb.AppendLine("4. 之后可用 `python Tools/verify.py --compare-visual`（等价于只跑本用例的重型比较）或直接跑");
        sb.AppendLine("   `python Tools/visual_diff.py Docs/screenshots/baseline Docs/screenshots` 做自动像素对比辅助。");
        sb.AppendLine();
        sb.AppendLine("## 未生成 / 降级的用例");
        sb.AppendLine();

        if (skipped.Count == 0)
        {
            sb.AppendLine("本次运行全部用例均已生成（「备注」列中的退化说明仅为曲目内容限制，不影响图片产出）。");
        }
        else
        {
            sb.AppendLine("以下用例本次**未生成**（不是通过，原因如下；后续需要重新跑一次或调整测试曲目）：");
            sb.AppendLine();
            for (int i = 0; i < skipped.Count; i++)
                sb.Append("- ").AppendLine(Cell(skipped[i]));
        }
        sb.AppendLine();

        File.WriteAllText(Path.Combine(dir, "manifest.md"), sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Markdown 单元格转义：竖线与换行会破坏表格。</summary>
    private static string Cell(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        return value.Replace("|", "／").Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>
    /// 表格排序键：<c>01_…06_…</c> 按数字升序排在前，<c>V01…V12</c> 按编号排在后，
    /// 与截图的执行顺序无关，保证 manifest 每次的表格顺序稳定。
    /// </summary>
    private static int SortKey(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return int.MaxValue;

        if (fileName[0] == 'V' || fileName[0] == 'v')
        {
            int number = 0;
            for (int i = 1; i < fileName.Length && char.IsDigit(fileName[i]); i++)
                number = number * 10 + (fileName[i] - '0');
            return 1000 + number;
        }

        int leading = 0;
        for (int i = 0; i < fileName.Length && char.IsDigit(fileName[i]); i++)
            leading = leading * 10 + (fileName[i] - '0');
        return leading;
    }
}
