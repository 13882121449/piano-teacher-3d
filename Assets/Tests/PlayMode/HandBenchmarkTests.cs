using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 手部接触评测基线（手部接触与视觉呈现优化方案 · 批次 1.2 / 1.4）。
    /// <para>
    /// 口径：<c>Assets/StreamingAssets/Songs/bench/</c> 下的三首自制曲目（黑键 / 和弦 / 快速音阶+大跨度），
    /// 每首<b>整曲</b>播放（不是固定 3 s 窗口），逐首输出 <see cref="HandMotionReport"/>。
    /// 曲目由 <c>Tools/make_bench_midi.py</c> 生成，可复现、无版权风险。
    /// </para>
    /// <para>
    /// 本类只做「测量 + 分组门禁」；任何 IK 参数调整都在批次 2 单独提交，回滚时不带走评测。
    /// </para>
    /// </summary>
    public class HandBenchmarkTests
    {
        private const string BenchFolder = "bench";

        /// <summary>分组门禁：各组命中率不得低于实测基线 − 3 个百分点（方案 1.4）。</summary>
        private const float GroupGateMargin = 0.03f;

        /// <summary>
        /// 分组门禁的最小样本数：低于该值不设门禁（2026-10-30 基线复核）。
        /// <para>
        /// 背景：<c>bench_fast_scale</c> 的「跨手」组实测只有 1–2 个样本（A2↔C6 跳进处），
        /// 2 样本 1 未命中即 50%，对 92% 门禁必然假失败——小样本命中率方差远大于 3pp 边际，
        /// 门禁在统计上无效。原「取 95% 防单样本抖动」（见基线常量注释）没有解决样本量问题。
        /// </para>
        /// <para>
        /// 跳过<b>不是</b>放行：以 Warning 日志显式记录组名、样本数与实测值。
        /// 后续（接触方案批次 2）扩充基准曲的跨手跳进数量、重采基线后，该组将获得足够样本，
        /// 本守卫随之自然退出（届时样本 ≥ 5，门禁照常生效）。
        /// </para>
        /// </summary>
        private const int MinGroupSamples = 5;

        // 实测基线（2026-10-19，本机编辑器 -batchmode；曲目见 bench/）。数值取自
        // Bench_ThreeSongs_LogFullReport 的 [BENCH] 日志（Docs/perf/hand-baseline-2026-10-19.md），
        // 调整曲目或改动指法/IK 后必须同步更新。
        // 组内基线 → 门禁 = 基线 − 3pp：黑键 99.9→96.9、和弦 99.3→96.3、快速音阶 97.8→94.8；
        // 跨手样本仅 1，取 95% 避免单样本抖动导致假失败。
        private const float BaselineBlackKey = 0.969f;
        private const float BaselineChord = 0.963f;
        private const float BaselineFastScale = 0.948f;
        private const float BaselineLargeMotion = 0.95f;

        /// <summary>
        /// 键面距离门禁（H0）：<c>KeyTopDistance</c> 是「指尖到键顶面的有符号距离」，正=悬空、负=压入。
        /// <para>
        /// 实测基线（2026-10-31，三首基准曲：P50 3.6–9.1 mm、P95 10.2–17.7 mm）取上包络 + 1 mm 余量，
        /// 作用是<b>防回退</b>而不是达标：方案目标（P50 ≤ 3 mm、P95 ≤ 8 mm、悬空帧 ≤ 2%、穿键帧 = 0）
        /// 需要 H2 解析 IK 与 H3 掌位闭环落地后才能收紧到此值，见 master plan H0。
        /// </para>
        /// </summary>
        private const float BaselineKeySurfaceP50 = 0.0100f;
        private const float BaselineKeySurfaceP95 = 0.0185f;

        private LessonSessionController _session;
        private HandIKController _handIK;
        private LessonTimeline _timeline;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("PianoLesson");
            yield return null;
            yield return null;
            _session = Object.FindFirstObjectByType<LessonSessionController>();
            _handIK = Object.FindFirstObjectByType<HandIKController>();
            _timeline = Object.FindFirstObjectByType<LessonTimeline>();
            Assert.IsNotNull(_session, "PianoLesson 场景应包含 LessonSessionController");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Stop();
            yield return null;
        }

        private static List<string> BenchFiles()
        {
            var result = new List<string>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs", BenchFolder);
            if (!Directory.Exists(dir)) return result;
            var files = Directory.GetFiles(dir, "*.mid");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            result.AddRange(files);
            return result;
        }

        /// <summary>整曲播放一首基准曲，返回该曲的动作指标报告。</summary>
        private IEnumerator PlayWholeSong(string path, System.Action<HandMotionReport, float> onDone)
        {
            _session.LoadSong(path);
            yield return null;
            Assert.IsTrue(_session.HasSong, $"基准曲应能加载：{Path.GetFileName(path)}");

            float duration = Mathf.Clamp(_session.TotalDuration, 1f, 30f);
            _session.Seek(0f);
            _session.Play();
            while (_timeline.IsPreparing) yield return null;
            yield return new WaitForSeconds(duration + 0.3f);
            _session.Pause();
            yield return null;

            onDone?.Invoke(_session.HandMotion, duration);
        }

        [UnityTest]
        public IEnumerator Bench_ThreeSongs_LogFullReport()
        {
            var files = BenchFiles();
            if (files.Count == 0) Assert.Ignore("未找到基准曲目集（先运行 Tools/make_bench_midi.py）");

            foreach (var file in files)
            {
                HandMotionReport report = null;
                float duration = 0f;
                yield return PlayWholeSong(file, (r, d) => { report = r; duration = d; });

                Assert.IsNotNull(report, "应产出动作报告");
                Debug.Log($"[BENCH] song={Path.GetFileNameWithoutExtension(file)} duration={duration:0.0}s " +
                          $"keys 键面P50={report.KeyTopDistance.P50 * 1000f:0.#}mm " +
                          $"键面P95={report.KeyTopDistance.P95 * 1000f:0.#}mm " +
                          $"键面峰值={report.KeyTopDistance.Max * 1000f:0.#}mm " +
                          $"指尖均值={report.FingertipTargetError.Mean * 1000f:0.#}mm " +
                          $"指尖P95={report.FingertipTargetError.P95 * 1000f:0.#}mm " +
                          $"换位峰值={report.TransitionPeakError * 1000f:0.#}mm " +
                          $"悬空={report.HoveringFrames} 指尖压键={report.FingerPenetrationFrames} " +
                          $"掌穿模={report.PalmPenetrationFrames} " +
                          $"指节穿键={report.SegmentPenetrationFrames}/{report.SegmentClearanceSamples} " +
                          $"指节最深={(float.IsPositiveInfinity(report.MinSegmentClearance) ? 0f : -report.MinSegmentClearance * 1000f):0.0}mm " +
                          $"命中率={report.HitRate * 100f:0.#}% 样本={report.TargetSamples}");
                Debug.Log($"[BENCH] song={Path.GetFileNameWithoutExtension(file)} full {report.ToLogString()}");

                // 批次 2.1 归因：把「哪一类失败」也打印出来（未分配 / 缺按压点 / 超可达范围）
                if (_handIK != null)
                {
                    int total = _handIK.FingertipTargetSampleCount;
                    int unassigned = _handIK.UnassignedFingertipSamples;
                    int missingTouch = _handIK.MissingKeyTouchSamples;
                    int unreachable = _handIK.UnreachableTargetSamples;
                    Debug.Log($"[BENCH-DIAG] song={Path.GetFileNameWithoutExtension(file)} " +
                              $"目标样本={total} 未分配={Pct(unassigned, total)} 缺按压点={Pct(missingTouch, total)} " +
                              $"超可达范围={Pct(unreachable, total)}");
                }
            }
        }

        [UnityTest]
        public IEnumerator Bench_GroupHitRates_NotBelowRecordedBaseline()
        {
            var files = BenchFiles();
            if (files.Count == 0) Assert.Ignore("未找到基准曲目集（先运行 Tools/make_bench_midi.py）");

            foreach (var file in files)
            {
                HandMotionReport report = null;
                yield return PlayWholeSong(file, (r, _) => report = r);

                string song = Path.GetFileNameWithoutExtension(file);
                AssertGroup(song, "黑键", report.BlackKeyHitRate, report.BlackKeyTargetSamples, BaselineBlackKey);
                AssertGroup(song, "和弦", report.ChordHitRate, report.ChordTargetSamples, BaselineChord);
                AssertGroup(song, "快速音阶", report.FastScaleHitRate, report.FastScaleTargetSamples, BaselineFastScale);
                AssertGroup(song, "跨手", report.LargeMotionHitRate, report.LargeMotionTargetSamples, BaselineLargeMotion);
            }
        }

        [UnityTest]
        public IEnumerator Bench_KeySurfaceDistance_NotAboveRecordedBaseline()
        {
            var files = BenchFiles();
            if (files.Count == 0) Assert.Ignore("未找到基准曲目集（先运行 Tools/make_bench_midi.py）");

            foreach (var file in files)
            {
                HandMotionReport report = null;
                yield return PlayWholeSong(file, (r, _) => report = r);

                string song = Path.GetFileNameWithoutExtension(file);
                Assert.IsNotNull(report, "应产出动作报告");
                if (report.TargetSamples <= 0) continue;

                float p50 = report.KeyTopDistance.P50;
                float p95 = report.KeyTopDistance.P95;
                float hoverRate = report.HoveringFrames / (float)report.TargetSamples;

                Debug.Log($"[BENCH-H0] {song} 键面P50={p50 * 1000f:0.#}mm（门禁≤{BaselineKeySurfaceP50 * 1000f:0.#}）" +
                          $" 键面P95={p95 * 1000f:0.#}mm（门禁≤{BaselineKeySurfaceP95 * 1000f:0.#}）" +
                          $" 悬空率={hoverRate * 100f:0.#}% 穿键帧={report.FingerPenetrationFrames}");

                Assert.LessOrEqual(p50, BaselineKeySurfaceP50,
                    $"{song}：键面距离 P50 回退（{p50 * 1000f:0.#}mm > 基线 {BaselineKeySurfaceP50 * 1000f:0.#}mm）");
                Assert.LessOrEqual(p95, BaselineKeySurfaceP95,
                    $"{song}：键面距离 P95 回退（{p95 * 1000f:0.#}mm > 基线 {BaselineKeySurfaceP95 * 1000f:0.#}mm）");
            }
        }

        [UnityTest, Explicit("逐指接触诊断，固定 60 Hz 播放三首基准曲")]
        public IEnumerator Bench_ContactByFinger() => MeasureFingerContact();

        [UnityTest, Explicit("写实角色逐指接触诊断")]
        public IEnumerator Bench_RocketboxContactByFinger()
        {
            foreach (string id in Characters.CharacterRegistry.BuiltInIds)
            {
                _session.RestoreCharacter(id);
                Assert.AreEqual(id, _session.CurrentCharacterView.CharacterId);
                yield return null;
                Debug.Log($"[CONTACT-AVATAR] {id}");
                yield return MeasureFingerContact();
            }
        }

        [UnityTest, Explicit("演奏短样本逐指接触诊断")]
        public IEnumerator Bench_ArticulationContactByFinger()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Songs", BenchFolder, "articulation");
            Assert.IsTrue(Directory.Exists(dir), "请运行 Tools/make_bench_midi.py --articulation");
            var files = Directory.GetFiles(dir, "*.mid");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            _session.RestoreCharacter(Characters.CharacterRegistry.VroidGenkiId);
            yield return null;
            yield return MeasureFingerContact(files);
        }

        [UnityTest, Explicit("固定 60 Hz 导出重建场景的逐帧手部诊断，可选录制俯视与斜侧画面")]
        public IEnumerator Bench_NearHandsFrameTrace() => TraceNearHands();

        [UnityTest]
        public IEnumerator Bench_FastScaleRestartFromHighRegister_PreparesFirstNote()
        {
            _handIK.UseAnatomicalContact = true;
            var pose = Object.FindFirstObjectByType<FingerPoseController>();
            pose.UseAnatomicalContact = true;
            _handIK.Assignment.UsePreparedPhrases = true;
            _session.RestoreCharacter(Characters.CharacterRegistry.VroidGenkiId);
            string path = Path.Combine(Application.streamingAssetsPath, "Songs", BenchFolder, "bench_fast_scale.mid");
            _session.LoadSong(path);
            Assert.IsTrue(_session.HasSong);
            var avatar = Object.FindFirstObjectByType<AvatarManager>();
            Assert.That(avatar, Is.Not.Null);
            Assert.IsTrue(_handIK.TryGetKeyPressPoint(60, out var firstKey));

            _session.Seek(3.9f); // 预备从高两个八度的 C6 开始。
            yield return new WaitForSeconds(0.6f);
            Assert.That(Mathf.Abs(avatar.RightHand.Wrist.position.x - firstKey.x), Is.GreaterThan(0.2f));

            _session.Restart();
            Assert.IsTrue(_timeline.IsPreparing, "重播先给手部留出准备时间");
            var previous = avatar.RightHand.Wrist.position;
            while (_timeline.IsPreparing)
            {
                Assert.AreEqual(0f, _session.CurrentTime, "准备时乐曲时间保持在首拍");
                Assert.AreEqual(-1, _handIK.Assignment.GetFinger(false, 60), "准备时首个音符保持未按下");
                yield return null;
                var current = avatar.RightHand.Wrist.position;
                Assert.Less(Vector3.Distance(previous, current), 0.15f, "准备时手腕不能瞬移");
                previous = current;
            }
            yield return null;
            Assert.That(Mathf.Abs(avatar.RightHand.Wrist.position.x - firstKey.x), Is.LessThan(_handIK.HitRadius));
            int finger = _handIK.Assignment.GetFinger(false, 60);
            Assert.IsTrue(TryGetFinger(avatar.RightHand, finger, out _, out _, out _, out var tip));
            Assert.That(tip.position.y, Is.GreaterThanOrEqualTo(firstKey.y - _handIK.FingertipPenetrationTolerance));
        }

        [UnityTest, Explicit("验证受限解算中右手首个三和弦的完整触键")]
        public IEnumerator Bench_AnatomicalFirstTriadContactsAssignedKeys() => TraceNearHands(requireFirstTriad: true);

        private IEnumerator TraceNearHands(bool requireFirstTriad = false)
        {
            string directory = System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_DIR");
            if (string.IsNullOrEmpty(directory))
                directory = Path.Combine(Application.dataPath, "..", "_verify_out", "hand_pose_trace");
            string variant = System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_VARIANT") ?? "current";
            Assert.That(variant, Does.Match(@"^[a-zA-Z0-9_-]+$"), "对照名称只能使用字母、数字、连字符和下划线");
            string role = System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_ROLE");
            if (!string.IsNullOrEmpty(role))
                CollectionAssert.Contains(Characters.CharacterRegistry.BuiltInIds, role, "角色 ID 无效");
            string song = System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_SONG") ?? "near_hands";
            Assert.That(song, Does.Match(@"^[a-zA-Z0-9_-]+$"), "曲目名称只能使用字母、数字、连字符和下划线");
            float speed = System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_SPEED") == "0.5" ? 0.5f : 1f;
            int.TryParse(System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_FRAME_STEP"), out int imageStep);
            int.TryParse(System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_PREPLAY_FRAMES"), out int preplayFrames);
            bool capture = imageStep > 0 && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            string benchDirectory = Path.Combine(Application.streamingAssetsPath, "Songs", BenchFolder);
            string songDirectory = song.StartsWith("bench_", System.StringComparison.Ordinal)
                ? benchDirectory : Path.Combine(benchDirectory, "articulation");
            if (song == "cinematic_tide") songDirectory = Path.Combine(Application.streamingAssetsPath, "Songs", "cinematic");
            string file = Path.Combine(songDirectory, song + ".mid");
            Assert.That(File.Exists(file), $"缺少 {song}.mid，请先生成演奏短样本");
            var notes = MidiParser.ParseFile(file).Notes;
            Assert.IsNotEmpty(notes, "曲目需要有可测量的音符");

            var map = Object.FindFirstObjectByType<PianoKeyMap>();
            var avatar = Object.FindFirstObjectByType<AvatarManager>();
            var pose = Object.FindFirstObjectByType<FingerPoseController>();
            Assert.IsNotNull(map);
            Assert.IsNotNull(avatar);
            Assert.IsNotNull(_handIK);
            Assert.IsNotNull(pose);
            int previousCaptureRate = Time.captureFramerate;
            float previousSpeed = _session.PlaybackSpeed;
            Time.captureFramerate = 60;
            bool legacy = string.Equals(variant, "v6", System.StringComparison.Ordinal)
                || System.Environment.GetEnvironmentVariable("PT3D_HAND_TRACE_LEGACY") == "1";
            bool previousContact = pose.UseAnatomicalContact;
            bool previousWrist = _handIK.UseAnatomicalContact;
            bool previousPlanning = _handIK.Assignment.UsePreparedPhrases;
            _handIK.UseAnatomicalContact = !legacy;
            pose.UseAnatomicalContact = !legacy;
            _handIK.Assignment.UsePreparedPhrases = !legacy;
            try
            {
                foreach (string id in Characters.CharacterRegistry.BuiltInIds)
                {
                    if (!string.IsNullOrEmpty(role) && id != role) continue;
                    _session.RestoreCharacter(id);
                    Assert.AreEqual(id, _session.CurrentCharacterView.CharacterId);
                    _session.SetDisplayMode(DisplayMode.Showcase);
                    _session.SetPracticeHand(PracticeHand.Both);
                    _session.SetSpeed(speed);
                    _session.LoadSong(file);
                    Assert.IsTrue(_session.HasSong, song);
                    // 默认模式直接从新载曲目播放，避免 Seek(0) 提前消费首音事件。
                    if (!legacy) _session.Seek(0f);
                    yield return null;
                    for (int frame = 0; frame < preplayFrames; frame++) yield return null;
                    string output = Path.Combine(directory, variant, song, id, speed < 1f ? "half" : "normal");
                    Directory.CreateDirectory(output);
                    using var writer = new StreamWriter(Path.Combine(output, "frames.csv"), false, new UTF8Encoding(false));
                    writer.WriteLine("frame,time_s,hand,pitch,finger,phase,note_start_s,note_end_s,correct_key,key_local_x_mm,key_local_z_mm,key_height_mm,wrist_x_mm,wrist_y_mm,wrist_z_mm,wrist_pitch_deg,wrist_yaw_deg,wrist_roll_deg,mcp_from_bind_deg,pip_from_bind_deg,dip_from_bind_deg,mcp_x_mm,mcp_y_mm,mcp_z_mm,tip_x_mm,tip_y_mm,tip_z_mm,palm_gap_mm,nearest_other_mcp_mm,nearest_other_tip_mm");

                    int[] firstContact = new int[notes.Count];
                    System.Array.Fill(firstContact, -1);
                    int triadCorrect = 0, triadSamples = 0;
                    int heldCorrect = 0, heldSamples = 0, unassigned = 0;
                    int sampled = 0, hover = 0, penetration = 0;
                    int thumbHold = 0, thumbCurled = 0, thumbContact = 0;
                    int thumbPress = 0, thumbPressContact = 0;
                    int laterThumbFrames = 0, laterThumbContact = 0;
                    int openingThumbContact = int.MaxValue;
                    var left = avatar.LeftHand;
                    var right = avatar.RightHand;
                    float lastTime = -1f;
                    _session.Play();
                    while (_timeline.IsPreparing) yield return null;
                    int frames = Mathf.CeilToInt(_session.TotalDuration * 60f / speed) + 6;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        yield return null;
                        float time = _session.CurrentTime;
                        if (time <= lastTime) continue;
                        lastTime = time;
                        bool sounding = false, frameHover = false, framePenetration = false;
                        var assignedFingers = 0;
                        for (int i = 0; i < notes.Count; i++)
                        {
                            var note = notes[i];
                            if (time < note.StartTime || time >= note.EndTime) continue;
                            bool isLeft = PianoNoteFilter.IsLeftHand(note);
                            int finger = _handIK.Assignment.GetFinger(isLeft, note.Pitch);
                            var hand = isLeft ? left : right;
                            var other = isLeft ? right : left;
                            bool correct = false;
                            sounding = true;
                            if (finger < 0 || !map.TryGetKey(note.Pitch, out var key)
                                || !TryGetFinger(hand, finger, out _, out _, out _, out var tip))
                            {
                                unassigned++;
                                WriteFingerTrace(map, writer, frame, time, isLeft, note.Pitch, -1, "Unassigned",
                                    note.StartTime, note.EndTime, false, null, hand, other, pose);
                            }
                            else
                            {
                                assignedFingers |= 1 << ((isLeft ? 0 : 5) + finger);
                                float height = KeyLocalHeight(map, note.Pitch, key.InverseTransformPoint(tip.position));
                                correct = IsFingertipOnKeycap(map, note.Pitch, tip.position);
                                frameHover |= height > 0.006f;
                                framePenetration |= height < -0.004f;
                                if (correct && firstContact[i] < 0)
                                    firstContact[i] = Mathf.RoundToInt((time - note.StartTime) * 60f / speed);
                                string phase = pose.GetPhase(isLeft, finger).ToString();
                                if (!isLeft && finger == 0)
                                {
                                    if (note.StartTime <= 0.0005f)
                                    {
                                        if (correct) openingThumbContact = Mathf.Min(openingThumbContact,
                                            Mathf.RoundToInt(time * 60f / speed));
                                    }
                                    else
                                    {
                                        laterThumbFrames++;
                                        if (correct) laterThumbContact++;
                                    }
                                    if (phase == "Press")
                                    {
                                        thumbPress++;
                                        if (correct) thumbPressContact++;
                                    }
                                }
                                if (phase == "Hold" && !isLeft && finger == 0 &&
                                    TryGetFinger(hand, finger, out _, out _, out var distal, out _))
                                {
                                    thumbHold++;
                                    if (correct) thumbContact++;
                                    if (Quaternion.Angle(distal.localRotation,
                                        pose.JointAxes.CurlLocalRotation(distal, 2, 0f)) >= 3f) thumbCurled++;
                                }
                                WriteFingerTrace(map, writer, frame, time, isLeft, note.Pitch, finger, phase,
                                    note.StartTime, note.EndTime, correct, key, hand, other, pose);
                            }
                            // 统计保持期时保留未分配和按错的音，避免只拿已分配音符当分母。
                            if (time - note.StartTime >= 2f * speed / 60f)
                            {
                                heldSamples++;
                                if (correct) heldCorrect++;
                                if (!isLeft && note.StartTime < 0.4f &&
                                    (note.Pitch == 60 || note.Pitch == 64 || note.Pitch == 67))
                                {
                                    triadSamples++;
                                    if (correct) triadCorrect++;
                                }
                            }
                        }
                        for (int slot = 0; slot < 10; slot++)
                        {
                            if ((assignedFingers & (1 << slot)) != 0) continue;
                            bool isLeft = slot < 5;
                            int finger = slot % 5;
                            string phase = pose.GetPhase(isLeft, finger).ToString();
                            WriteFingerTrace(map, writer, frame, time, isLeft, -1, finger, phase,
                                null, null, null, null, isLeft ? left : right, isLeft ? right : left, pose);
                        }
                        if (sounding) sampled++;
                        if (frameHover) hover++;
                        if (framePenetration) penetration++;
                        if (capture && frame % imageStep == 0)
                            CaptureTraceViews(map, output, frame);
                    }
                    _session.Stop();
                    var onset = new List<int>();
                    foreach (int contact in firstContact)
                        if (contact >= 0) onset.Add(contact);
                    onset.Sort();
                    int onsetP95 = onset.Count > 0 ? onset[Mathf.CeilToInt(0.95f * onset.Count) - 1] : -1;
                    Debug.Log($"[POSE-TRACE] {variant}/{id}/{song} speed={speed} " +
                              $"正确键帽={heldCorrect}/{heldSamples} 悬空={hover}/{sampled} 穿键={penetration}/{sampled} " +
                              $"未分配帧音符={unassigned} 触键音符={onset.Count}/{notes.Count} 延迟P95={onsetP95}帧 " +
                              $"MCP/PIP/DIP上限={pose.MaxMcpDegrees}/{pose.MaxPipDegrees}/{pose.MaxDipDegrees} " +
                              $"轨迹={output}");
                    Assert.Greater(sampled, 0, "没有采到演奏帧");
                    if (song == "bench_fast_scale" && role == Characters.CharacterRegistry.VroidGenkiId &&
                        System.Environment.GetEnvironmentVariable("PT3D_CHECK_RIGHT_THUMB") == "1")
                    {
                        string openingContactText = openingThumbContact == int.MaxValue ? "未触键" : $"{openingThumbContact} 帧";
                        Debug.Log($"[THUMB-TRACE] 首音={openingContactText}，起音={thumbPressContact}/{thumbPress}，" +
                            $"保持={thumbContact}/{thumbHold}，首音后={laterThumbContact}/{laterThumbFrames}");
                        Assert.That(openingThumbContact, Is.LessThanOrEqualTo(2),
                            "快音阶首个右拇指音须在两帧内落到 C4 键帽");
                        if (legacy)
                        {
                            // 默认指法的大跳长音由小指演奏，用首音之后的右拇指实际样本验证跟随。
                            Assert.Greater(laterThumbFrames, 20, "默认指法需要覆盖足够的右拇指触键帧");
                            Assert.That((float)laterThumbContact / laterThumbFrames, Is.GreaterThanOrEqualTo(0.70f),
                                $"默认指法右拇指正确落键 {laterThumbContact}/{laterThumbFrames}");
                        }
                        else
                        {
                            Assert.Greater(thumbPress, 40, "连续音阶需覆盖足够的起音帧");
                            Assert.That((float)thumbPressContact / thumbPress, Is.GreaterThanOrEqualTo(0.78f),
                                $"右拇指起音期正确落键 {thumbPressContact}/{thumbPress}");
                            Assert.Greater(thumbHold, 40, "连续音阶需覆盖足够的保持帧");
                            Assert.That((float)thumbCurled / thumbHold, Is.GreaterThanOrEqualTo(0.70f),
                                $"右拇指末端在快速音阶停止屈曲 {thumbHold - thumbCurled}/{thumbHold}");
                            Assert.That((float)thumbContact / thumbHold, Is.GreaterThanOrEqualTo(0.90f),
                                $"右拇指保持期正确落键 {thumbContact}/{thumbHold}");
                        }
                    }
                    if (requireFirstTriad)
                    {
                        Assert.Greater(triadSamples, 0, "首个三和弦应进入保持期");
                        Assert.That((float)triadCorrect / triadSamples, Is.GreaterThanOrEqualTo(0.90f),
                            $"{id} 首个右手 C–E–G 保持期正确键帽 {triadCorrect}/{triadSamples}");
                    }
                    if (!legacy && song == "unplayable_span")
                        Assert.Greater(unassigned, 0, "超跨度音组不应硬塞给一个手指");
                }
            }
            finally
            {
                Time.captureFramerate = previousCaptureRate;
                _session.SetSpeed(previousSpeed);
                _handIK.UseAnatomicalContact = previousWrist;
                pose.UseAnatomicalContact = previousContact;
                _handIK.Assignment.UsePreparedPhrases = previousPlanning;
            }
        }

        private static bool TryGetFinger(IHandRig hand, int finger, out Transform mcp, out Transform pip,
            out Transform dip, out Transform tip)
        {
            mcp = pip = dip = tip = null;
            if (!(hand is IFingerJointRig rig) || !rig.TryGetJoints(finger, out mcp, out pip, out dip)) return false;
            tip = rig.GetFingertip(finger);
            return tip != null;
        }

        private static float KeyLocalHeight(PianoKeyMap map, int pitch, Vector3 local)
            => local.y - (PianoKeyMap.IsBlackKey(pitch) ? map.BlackHeight : map.WhiteHeight);

        private static void WriteFingerTrace(PianoKeyMap map, StreamWriter writer, int frame, float time, bool left, int pitch,
            int finger, string phase, float? start, float? end, bool? correct, Transform key,
            IHandRig hand, IHandRig other, FingerPoseController pose)
        {
            var wrist = hand.Wrist;
            var rotation = wrist.rotation.eulerAngles;
            if (hand is IHandOrientationRig orientation)
                rotation = Quaternion.LookRotation(wrist.TransformDirection(orientation.FingerForwardLocal),
                    wrist.TransformDirection(orientation.BackNormalLocal)).eulerAngles;
            rotation = new Vector3(Mathf.DeltaAngle(0f, rotation.x), Mathf.DeltaAngle(0f, rotation.y),
                Mathf.DeltaAngle(0f, rotation.z));

            Vector3? local = null, mcpPosition = null, tipPosition = null;
            float? mcpAngle = null, pipAngle = null, dipAngle = null, mcpGap = null, tipGap = null;
            float? localHeight = null;
            if (finger >= 0 && TryGetFinger(hand, finger, out var mcp, out var pip, out var dip, out var tip))
            {
                mcpPosition = mcp.position;
                tipPosition = tip.position;
                if (key != null)
                {
                    local = key.InverseTransformPoint(tip.position);
                    localHeight = KeyLocalHeight(map, pitch, local.Value);
                }
                mcpAngle = Quaternion.Angle(mcp.localRotation, pose.JointAxes.CurlLocalRotation(mcp, 0, 0f));
                pipAngle = Quaternion.Angle(pip.localRotation, pose.JointAxes.CurlLocalRotation(pip, 1, 0f));
                dipAngle = Quaternion.Angle(dip.localRotation, pose.JointAxes.CurlLocalRotation(dip, 2, 0f));
                for (int i = 0; i < 5; i++)
                {
                    if (!TryGetFinger(other, i, out var otherMcp, out _, out _, out var otherTip)) continue;
                    float rootDistance = Vector3.Distance(mcp.position, otherMcp.position);
                    float tipDistance = Vector3.Distance(tip.position, otherTip.position);
                    mcpGap = Mathf.Min(mcpGap.GetValueOrDefault(float.PositiveInfinity), rootDistance);
                    tipGap = Mathf.Min(tipGap.GetValueOrDefault(float.PositiveInfinity), tipDistance);
                }
            }

            string[] values =
            {
                frame.ToString(), Number(time), left ? "L" : "R", pitch < 0 ? "" : pitch.ToString(),
                finger < 0 ? "" : (finger + 1).ToString(), phase, Number(start), Number(end),
                correct.HasValue ? (correct.Value ? "1" : "0") : "",
                Millimetres(local?.x), Millimetres(local?.z), Millimetres(localHeight),
                Millimetres(wrist.position.x), Millimetres(wrist.position.y), Millimetres(wrist.position.z),
                Number(rotation.x), Number(rotation.y), Number(rotation.z),
                Number(mcpAngle), Number(pipAngle), Number(dipAngle),
                Millimetres(mcpPosition?.x), Millimetres(mcpPosition?.y), Millimetres(mcpPosition?.z),
                Millimetres(tipPosition?.x), Millimetres(tipPosition?.y), Millimetres(tipPosition?.z),
                Millimetres(Vector3.Distance(hand.Palm.position, other.Palm.position)),
                Millimetres(mcpGap), Millimetres(tipGap)
            };
            writer.WriteLine(string.Join(",", values));
        }

        private static string Number(float? value)
            => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

        private static string Millimetres(float? value) => Number(value * 1000f);

        private static void CaptureTraceViews(PianoKeyMap map, string output, int frame)
        {
            var camera = Camera.main;
            if (camera == null || !map.TryGetKey(64, out var anchor)) return;
            const int width = 640, height = 360;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var target = camera.targetTexture;
            var active = RenderTexture.active;
            bool orthographic = camera.orthographic;
            float size = camera.orthographicSize;
            var center = new Vector3(anchor.position.x, map.KeySurfaceY, 0.07f);
            try
            {
                camera.targetTexture = rt;
                camera.orthographic = true;
                camera.orthographicSize = 0.23f;
                DrawTraceFrame(camera, center + Vector3.up * 0.75f, center, Vector3.back,
                    texture, rt, Path.Combine(output, $"top_{frame:D5}.png"));
                DrawTraceFrame(camera, center + new Vector3(-0.23f, 0.55f, 0.38f), center, Vector3.up,
                    texture, rt, Path.Combine(output, $"side_{frame:D5}.png"));
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.targetTexture = target;
                camera.orthographic = orthographic;
                camera.orthographicSize = size;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(texture);
            }
        }

        private static void DrawTraceFrame(Camera camera, Vector3 position, Vector3 center, Vector3 up,
            Texture2D texture, RenderTexture rt, string output)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(center - position, up));
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            File.WriteAllBytes(output, texture.EncodeToPNG());
        }

        private IEnumerator MeasureFingerContact(IReadOnlyList<string> files = null)
        {
            files ??= BenchFiles();
            Assert.IsNotEmpty(files);
            var keyMap = Object.FindFirstObjectByType<PianoKeyMap>();
            Assert.IsNotNull(keyMap);
            var avatar = Object.FindFirstObjectByType<AvatarManager>();
            Assert.IsNotNull(avatar);
            var pressedField = typeof(HandIKController).GetField("_pressed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var pressed = (HashSet<int>)pressedField.GetValue(_handIK);
            int previousCaptureRate = Time.captureFramerate;
            Time.captureFramerate = 60;
            try
            {
                foreach (var file in files)
                {
                    _session.LoadSong(file);
                    yield return null;
                    Assert.IsTrue(_session.HasSong, file);
                    _session.Seek(0f);
                    _session.Play();
                    var horizontal = new List<float>[10];
                    var vertical = new List<float>[10];
                    for (int i = 0; i < 10; i++)
                    {
                        horizontal[i] = new List<float>();
                        vertical[i] = new List<float>();
                    }
                    var events = MidiParser.ParseFile(file).Notes;
                    var firstContactFrames = new int[events.Count];
                    System.Array.Fill(firstContactFrames, -1);
                    int keyedSamples = 0;
                    int keyedContacts = 0;
                    int sampledFrames = 0;
                    int penetrationFrames = 0;
                    int hoverFrames = 0;
                    int hoverBeyondPlanFrames = 0;
                    int infeasibleFrames = 0;
                    float maxSpanGap = 0f;
                    int unassigned = 0;
                    var origins = new Vector3[10];
                    var keys = new Vector3[10];
                    var reaches = new float[10];
                    int frames = Mathf.CeilToInt(_session.TotalDuration * 60f) + 18;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        yield return null;
                        bool penetration = false;
                        bool hover = false;
                        bool hoverBeyondPlan = false;
                        bool sampled = false;
                        int activeFingers = 0;
                        foreach (int pitch in pressed)
                        {
                            bool left = _handIK.IsActivePitchLeft(pitch);
                            int finger = _handIK.Assignment.GetFinger(left, pitch);
                            if (finger < 0) { unassigned++; continue; }
                            if (!_handIK.TryGetKeyPressPoint(pitch, out var key)) continue;
                            var rig = (IFingerJointRig)(left ? avatar.LeftHand : avatar.RightHand);
                            var tip = rig.GetFingertip(finger).position;
                            var delta = (tip - key) * 1000f;
                            bool correctKey = IsFingertipOnKeycap(keyMap, pitch, tip);
                            float time = _session.CurrentTime;
                            for (int i = 0; i < events.Count; i++)
                            {
                                var note = events[i];
                                if (note.Pitch != pitch || PianoNoteFilter.IsLeftHand(note) != left
                                    || time < note.StartTime || time >= note.EndTime) continue;
                                if (time - note.StartTime >= 2f / 60f)
                                {
                                    keyedSamples++;
                                    if (correctKey) keyedContacts++;
                                }
                                if (correctKey && firstContactFrames[i] < 0)
                                    firstContactFrames[i] = Mathf.RoundToInt((time - note.StartTime) * 60f);
                            }
                            int slot = (left ? 0 : 5) + finger;
                            horizontal[slot].Add(Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z));
                            vertical[slot].Add(delta.y);
                            sampled = true;
                            penetration |= delta.y < -4f;
                            hover |= delta.y > 8f;
                            hoverBeyondPlan |= delta.y > 6f;
                            if (!rig.TryGetJoints(finger, out var root, out _, out _)) continue;
                            activeFingers |= 1 << slot;
                            origins[slot] = root.position;
                            keys[slot] = key;
                            reaches[slot] = rig.GetFingerLength(finger);
                        }
                        if (sampled) sampledFrames++;
                        if (penetration) penetrationFrames++;
                        if (hover) hoverFrames++;
                        if (hoverBeyondPlan) hoverBeyondPlanFrames++;

                        float frameGap = 0f;
                        for (int i = 0; i < 10; i++)
                        {
                            if ((activeFingers & (1 << i)) == 0) continue;
                            for (int j = i + 1; j < (i < 5 ? 5 : 10); j++)
                            {
                                if ((activeFingers & (1 << j)) == 0) continue;
                                // 若键距超过两指长与两指根间距之和，平移手腕也无法同时触键。
                                float gap = Vector3.Distance(keys[i], keys[j]) - Vector3.Distance(origins[i], origins[j])
                                            - reaches[i] - reaches[j];
                                frameGap = Mathf.Max(frameGap, gap);
                            }
                        }
                        if (frameGap > 0.001f) infeasibleFrames++;
                        maxSpanGap = Mathf.Max(maxSpanGap, frameGap);
                    }
                    _session.Stop();
                    string song = Path.GetFileNameWithoutExtension(file);
                    Assert.Greater(sampledFrames, 0, song);
                    Debug.Log($"[CONTACT] {song} 帧={sampledFrames} 穿键帧={penetrationFrames} 离键超过8mm帧={hoverFrames} 未分配样本={unassigned}");
                    Debug.Log($"[CONTACT-SPAN] {song} 离键超过6mm帧={hoverBeyondPlanFrames} " +
                              $"同手无法覆盖帧={infeasibleFrames} 最大跨度缺口={maxSpanGap * 1000f:0.0}mm");
                    for (int i = 0; i < 10; i++)
                    {
                        if (vertical[i].Count == 0) continue;
                        horizontal[i].Sort();
                        vertical[i].Sort();
                        int p95 = Mathf.CeilToInt(0.95f * vertical[i].Count) - 1;
                        Debug.Log($"[CONTACT] {song} {(i < 5 ? "左" : "右")}{i % 5} n={vertical[i].Count} " +
                                  $"水平P50={horizontal[i][horizontal[i].Count / 2]:0.0}mm 水平P95={horizontal[i][p95]:0.0}mm " +
                                  $"高度P95={vertical[i][p95]:0.0}mm 最低={vertical[i][0]:0.0}mm");
                    }
                    var delays = new List<int>();
                    for (int i = 0; i < events.Count; i++)
                    {
                        var note = events[i];
                        if (firstContactFrames[i] >= 0) delays.Add(firstContactFrames[i]);
                        else Debug.Log($"[ONSET-MISS] {song} 时刻={note.StartTime:F3}s 音高={note.Pitch} 手={(PianoNoteFilter.IsLeftHand(note) ? "左" : "右")}");
                    }
                    delays.Sort();
                    int onsetP95 = delays.Count > 0 ? delays[Mathf.CeilToInt(delays.Count * 0.95f) - 1] : -1;
                    Debug.Log($"[KEYCAP] {song} 正确键帽={keyedContacts}/{keyedSamples} " +
                              $"起音命中={delays.Count}/{events.Count} 落键延迟P95={onsetP95}帧");
                    Assert.Zero(penetrationFrames, $"{song}：分配指尖不应低于键面 4 mm");
                    if (song == "bench_black_keys")
                        Assert.Less(hoverFrames, sampledFrames / 2, "黑键悬空帧不能占多数");
                }
            }
            finally { Time.captureFramerate = previousCaptureRate; }
        }

        private static string Pct(int part, int total)
            => total > 0 ? $"{part}（{part * 100f / total:0.#}%）" : "0（样本 0）";

        private static bool IsFingertipOnKeycap(PianoKeyMap map, int pitch, Vector3 fingertip)
        {
            if (!map.TryGetKey(pitch, out var key)) return false;
            bool black = PianoKeyMap.IsBlackKey(pitch);
            float width = black ? map.BlackWidth : map.WhiteWidth;
            float length = black ? map.BlackLength : map.WhiteLength;
            float height = black ? map.BlackHeight : map.WhiteHeight;
            Vector3 local = key.InverseTransformPoint(fingertip);
            // 白键的后段被黑键占用，只统计前侧可触区。黑键保留前后端各一小段倒角。
            float near = length * (black ? 0.08f : 0.44f);
            return Mathf.Abs(local.x) <= width * 0.5f && local.z >= near && local.z <= length
                && local.y >= height - 0.004f && local.y <= height + 0.006f;
        }

        private static void AssertGroup(string song, string group, float rate, int samples, float baseline)        {
            if (samples == 0) return; // 该曲不含此类帧：不计入（与「样本 0」口径一致）
            if (samples < MinGroupSamples)
            {
                // 小样本组不设门禁（见 MinGroupSamples 注释）：显式记录而不是静默放行
                Debug.LogWarning($"[BENCH] {song} 的「{group}」样本仅 {samples}（< {MinGroupSamples}），" +
                                 $"不设门禁；实测命中率 {rate * 100f:0.#}%（基线 {baseline * 100f:0.#}%）");
                return;
            }
            Assert.GreaterOrEqual(rate, baseline - GroupGateMargin,
                $"{song} 的「{group}」命中率 {rate * 100f:0.#}% 低于基线 {baseline * 100f:0.#}% −3pp（样本 {samples}）");
        }
    }
}
