using NUnit.Framework;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 「手指 ↔ 音高」共用分配服务（方案 5.3 第二阶段 / 审查 1.2B）：
    /// 手指姿态控制器与手部 IK 必须读同一份结果，否则会出现
    /// 「弯下去的手指并不是落在键上的手指」这类错配。
    /// </summary>
    public class FingerAssignmentServiceTests
    {
        private FingerAssignmentService NewService()
        {
            var service = new FingerAssignmentService();
            service.Provider = new HeuristicFingeringProvider();
            return service;
        }

        [Test]
        public void PreparedMajorScale_KeepsThumbCrossingAndFingeringAfterSeek()
        {
            var service = NewService();
            int[] pitches = { 60, 62, 64, 65, 67, 69, 71, 72 };
            int[] expected = { 0, 1, 2, 0, 1, 2, 3, 4 };
            var notes = new MidiNoteEvent[pitches.Length];
            for (int i = 0; i < notes.Length; i++)
                notes[i] = new MidiNoteEvent(pitches[i], i * 0.125f, 0.11f, 80, 0, 0);

            service.PrepareSong(notes);
            for (int i = 0; i < pitches.Length; i++)
            {
                service.PushMelody(false, notes[i]);
                service.Refresh(false, new[] { pitches[i] }, notes[i].StartTime, i);
                Assert.AreEqual(expected[i], service.GetFinger(false, pitches[i]), "音符 " + pitches[i]);
            }

            service.Clear();
            service.PushMelody(false, notes[3]);
            service.Refresh(false, new[] { 65 }, notes[3].StartTime, 10);
            Assert.AreEqual(0, service.GetFinger(false, 65), "跳转到穿指位置后仍应用同一份曲目计划");
        }

        [Test]
        public void Refresh_AssignsEveryPitchToExactlyOneFinger()
        {
            var service = NewService();
            var pitches = new[] { 48, 52, 55 };

            service.Refresh(true, pitches, 0f, 1);

            int assigned = 0;
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int pitch = service.GetPitch(true, f);
                if (pitch < 0) continue;
                assigned++;

                // 反向查询必须得到同一根手指（一一对应，不允许"一个音挂多指"）
                Assert.AreEqual(f, service.GetFinger(true, pitch),
                    "同一音高被分配给多根手指（" + pitch + "）");
            }
            Assert.AreEqual(pitches.Length, assigned, "每个按下的音高都应被分到一根手指");
        }

        [Test]
        public void Refresh_SameFrameAndSamePitches_IsIdempotentAndConsistentAcrossCallers()
        {
            var service = NewService();
            var pitches = new[] { 60, 64 };

            service.Refresh(true, pitches, 0f, 100);
            int first = service.GetPitch(true, 0);

            // 第二个调用者（同一帧、同一集合）不应重算出不同答案
            service.Refresh(true, pitches, 0f, 100);
            Assert.AreEqual(first, service.GetPitch(true, 0), "同帧重复刷新产生了不同结果");

            // 集合变化（同一帧内）必须重算
            service.Refresh(true, new[] { 60 }, 0f, 100);
            Assert.AreEqual(1, CountAssigned(service, true), "音高集合变化后应重新求解");
        }

        [Test]
        public void Refresh_LeftAndRightAreIsolated()
        {
            var service = NewService();
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            service.Refresh(false, new[] { 72, 76 }, 0f, 1);

            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int l = service.GetPitch(true, f);
                if (l >= 0) Assert.Less(l, PianoNoteFilter.SplitPitch, "左手分配到了右手音区");
                int r = service.GetPitch(false, f);
                if (r >= 0) Assert.GreaterOrEqual(r, PianoNoteFilter.SplitPitch, "右手分配到了左手音区");
            }
        }

        [Test]
        public void Refresh_EmptyInput_ClearsAssignmentAndNeverThrows()
        {
            var service = NewService();
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            Assert.AreEqual(2, CountAssigned(service, true));

            service.Refresh(true, new int[0], 0f, 2);
            Assert.AreEqual(0, CountAssigned(service, true), "空输入应清空分配");

            Assert.DoesNotThrow(() => service.Refresh(true, null, 0f, 3), "null 输入不得抛异常");
            Assert.IsFalse(service.HasAssignment(true));
        }

        [Test]
        public void Clear_RemovesAssignmentAndMelodyHistory()
        {
            var service = NewService();
            service.PushMelody(true, 48);
            service.Refresh(true, new[] { 48, 52 }, 0f, 1);
            Assert.IsTrue(service.HasAssignment(true));

            service.Clear();

            Assert.IsFalse(service.HasAssignment(true));
            Assert.AreEqual(-1, service.GetPitch(true, 0));
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
                Assert.AreEqual(-1, service.GetFinger(true, 48 + f));
        }

        [Test]
        public void Provider_NullFallsBackToHeuristicInsteadOfThrowing()
        {
            var service = new FingerAssignmentService();
            service.Provider = null;

            Assert.DoesNotThrow(() => service.Refresh(true, new[] { 48, 52, 55 }, 0f, 1));
            Assert.IsTrue(service.HasAssignment(true), "未设置提供器时应回退启发式而不是产生空分配");
            Assert.IsNotEmpty(service.ProviderName);
        }

        [Test]
        public void Refresh_MorePitchesThanFingers_NeverWritesOutOfRangeIndex()
        {
            var service = NewService();
            var many = new[] { 40, 43, 45, 47, 48, 50, 52, 55, 57, 59 };

            Assert.DoesNotThrow(() => service.Refresh(true, many, 0f, 1));

            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                int pitch = service.GetPitch(true, f);
                if (pitch >= 0) Assert.Contains(pitch, many, "分配出的音高必须来自输入集合");
            }
        }

        [Test]
        public void PreparedLegato_KeepsHeldFingerAndRestoresTheSameChordAfterSeek()
        {
            var service = NewService();
            var held = new MidiNoteEvent(64, 0f, 0.5f, 80, 0, 0);
            var added = new MidiNoteEvent(67, 0.15f, 0.4f, 80, 0, 0);
            service.PrepareSong(new[] { held, added });

            service.PushMelody(false, held);
            service.Refresh(false, new[] { 64 }, 0f, 1);
            int heldFinger = service.GetFinger(false, 64);
            service.PushMelody(false, added);
            service.Refresh(false, new[] { 64, 67 }, 0.2f, 2);

            Assert.AreEqual(heldFinger, service.GetFinger(false, 64), "保持中的音符不能在加入新音时换指");
            int addedFinger = service.GetFinger(false, 67);
            Assert.Greater(addedFinger, heldFinger, "自然右手形要维持由低向高的指序");

            service.Clear();
            service.PushMelody(false, added);
            service.PushMelody(false, held);
            service.Refresh(false, new[] { 64, 67 }, 0.2f, 3);
            Assert.AreEqual(heldFinger, service.GetFinger(false, 64));
            Assert.AreEqual(addedFinger, service.GetFinger(false, 67));
        }

        [Test]
        public void PreparedRepeatedNote_KeepsItsFingeringAfterRestAndSeek()
        {
            var service = NewService();
            var approach = new MidiNoteEvent(60, 0f, 0.2f, 80, 0, 0);
            var first = new MidiNoteEvent(60, 0.3f, 0.2f, 80, 0, 0);
            var second = new MidiNoteEvent(60, 0.7f, 0.2f, 80, 0, 0);
            var chord = new MidiNoteEvent(67, 0f, 0.2f, 80, 0, 0);
            service.PrepareSong(new[] { approach, chord, first, second });
            service.PushMelody(false, approach);
            service.PushMelody(false, chord);
            service.Refresh(false, new[] { 60, 67 }, 0f, 1);
            int finger = service.GetFinger(false, 60);

            foreach (var note in new[] { first, second })
            {
                Assert.AreEqual(finger, service.PreviewFinger(false, note.Pitch, note.StartTime));
                service.Clear();
                service.PushMelody(false, note);
                service.Refresh(false, new[] { 60 }, note.StartTime, 2);
                Assert.AreEqual(finger, service.GetFinger(false, 60));
            }
        }

        [Test]
        public void PreparedOverstretch_DoesNotAssignBothKeysToOneHand()
        {
            var service = NewService();
            var low = new MidiNoteEvent(60, 0f, 0.5f, 80, 0, 0);
            var high = new MidiNoteEvent(84, 0f, 0.5f, 80, 0, 0);
            service.PrepareSong(new[] { low, high });
            service.PushMelody(false, low);
            service.PushMelody(false, high);
            service.Refresh(false, new[] { 60, 84 }, 0.1f, 1);
            Assert.Less(CountAssigned(service, false), 2, "一只手不能同时跨两个八度");
        }

        [Test]
        public void CinematicTide_RightAndLeftHandFingering_IsRealisticAndUsesThumb()
        {
            string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath,
                "Songs", "cinematic", "cinematic_tide.mid");
            Assert.IsTrue(System.IO.File.Exists(path), $"找不到测试曲目: {path}");

            var parsed = MidiParser.ParseFile(path);
            Assert.IsTrue(parsed.Success, $"MIDI 解析失败: {parsed.Error}");
            Assert.Greater(parsed.Notes.Count, 20);

            var service = NewService();
            service.PrepareSong(parsed.Notes);

            CheckHandRealism("cinematic_tide RH", service, parsed.Notes, false, 4);
            CheckHandRealism("cinematic_tide LH", service, parsed.Notes, true, 3);
        }

        [Test]
        public void BlackKeyMelody_AvoidsThumbAndPinkyPecking()
        {
            string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath,
                "Songs", "bench", "bench_black_keys.mid");
            Assert.IsTrue(System.IO.File.Exists(path), $"找不到基准曲目: {path}");

            var parsed = MidiParser.ParseFile(path);
            Assert.IsTrue(parsed.Success, $"MIDI 解析失败: {parsed.Error}");

            var service = NewService();
            service.PrepareSong(parsed.Notes);

            int blackTotal = 0;
            int blackLongFingers = 0;
            int blackThumb = 0;
            foreach (var note in parsed.Notes)
            {
                if (!PianoNoteFilter.IsBlackKey(note.Pitch)) continue;
                bool leftHand = PianoNoteFilter.IsLeftHand(in note);
                int finger = service.PreviewFinger(leftHand, note.Pitch, note.StartTime);
                if (finger < 0) continue;
                blackTotal++;
                if (finger >= 1 && finger <= 3) blackLongFingers++;
                if (finger == 0) blackThumb++;
            }

            Assert.Greater(blackTotal, 5);
            float longRatio = (float)blackLongFingers / blackTotal;
            float thumbRatio = (float)blackThumb / blackTotal;
            UnityEngine.Debug.Log($"[FINGERING] bench_black_keys total={blackTotal} longRatio={longRatio:F3} thumbRatio={thumbRatio:F3}");
            Assert.GreaterOrEqual(longRatio, 0.70f, "黑键应主要由 2/3/4 长指演奏");
            Assert.LessOrEqual(thumbRatio, 0.15f, "黑键应尽量避开拇指连击");
        }

        [Test]
        public void FiveFingerAndThumbUnderRun_ProducesTextbookFingering()
        {
            // 1) 先弹 C4 再上高八度 C5-G5，验证五指均参与而非全程卡在小指 (4)
            var service = NewService();
            var phrase = new System.Collections.Generic.List<MidiNoteEvent>
            {
                new MidiNoteEvent(60, 0f, 0.25f, 80, 0, 0),
                new MidiNoteEvent(72, 0.8f, 0.25f, 80, 0, 0),
                new MidiNoteEvent(74, 1.1f, 0.25f, 80, 0, 0),
                new MidiNoteEvent(76, 1.4f, 0.25f, 80, 0, 0),
                new MidiNoteEvent(77, 1.7f, 0.25f, 80, 0, 0),
                new MidiNoteEvent(79, 2.0f, 0.25f, 80, 0, 0)
            };
            service.PrepareSong(phrase);
            var highFingers = new System.Collections.Generic.HashSet<int>();
            for (int i = 1; i < phrase.Count; i++)
                highFingers.Add(service.PreviewFinger(false, phrase[i].Pitch, phrase[i].StartTime));
            Assert.AreEqual(5, highFingers.Count, "高音区 C5-G5 五指进行必须自然用满 5 根手指");

            // 2) 右手下行 C 大调音阶：C5..C4 必须有拇指参与且无连续同指异音
            int[] descPitches = { 72, 71, 69, 67, 65, 64, 62, 60 };
            var descNotes = new System.Collections.Generic.List<MidiNoteEvent>(descPitches.Length);
            for (int i = 0; i < descPitches.Length; i++)
                descNotes.Add(new MidiNoteEvent(descPitches[i], i * 0.25f, 0.20f, 80, 0, 0));
            service.PrepareSong(descNotes);
            var descDistinct = new System.Collections.Generic.HashSet<int>();
            int prevDescFinger = -1;
            bool descUsedThumb = false;
            for (int i = 0; i < descNotes.Count; i++)
            {
                int f = service.PreviewFinger(false, descNotes[i].Pitch, descNotes[i].StartTime);
                Assert.GreaterOrEqual(f, 0);
                descDistinct.Add(f);
                if (f == 0) descUsedThumb = true;
                Assert.AreNotEqual(prevDescFinger, f, $"下行音阶第 {i} 音不应同指啄键");
                prevDescFinger = f;
            }
            Assert.IsTrue(descUsedThumb, "右手下行音阶必须经过拇指穿/跨指");
            Assert.GreaterOrEqual(descDistinct.Count, 4);

            // 3) 左手低音区五指进行 C2-G2：必须用满 5 指且最高音 G2 由拇指 (0) 演奏
            int[] lhPitches = { 36, 38, 40, 41, 43 };
            var lhNotes = new System.Collections.Generic.List<MidiNoteEvent>(lhPitches.Length);
            for (int i = 0; i < lhPitches.Length; i++)
                lhNotes.Add(new MidiNoteEvent(lhPitches[i], i * 0.3f, 0.25f, 80, 0, 0));
            var lhFirst = lhNotes[0];
            Assert.IsTrue(PianoNoteFilter.IsLeftHand(in lhFirst));
            service.PrepareSong(lhNotes);
            var lhDistinct = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < lhNotes.Count; i++)
            {
                int f = service.PreviewFinger(true, lhNotes[i].Pitch, lhNotes[i].StartTime);
                Assert.GreaterOrEqual(f, 0);
                lhDistinct.Add(f);
            }
            Assert.AreEqual(5, lhDistinct.Count, "左手五指 C2-G2 应使用全部 5 根手指");
            Assert.AreEqual(0, service.PreviewFinger(true, 43, 1.2f), "左手 C2-G2 最高音 G2 应由左手拇指 (0) 演奏");
        }

        private static void CheckHandRealism(string tag, FingerAssignmentService service,
            System.Collections.Generic.IReadOnlyList<MidiNoteEvent> allNotes, bool leftHand,
            int minDistinctFingers)
        {
            var handNotes = new System.Collections.Generic.List<MidiNoteEvent>();
            for (int i = 0; i < allNotes.Count; i++)
            {
                var n = allNotes[i];
                if (PianoNoteFilter.IsLeftHand(in n) == leftHand) handNotes.Add(n);
            }
            Assert.Greater(handNotes.Count, 20, $"{tag} 音符数过少");

            handNotes.Sort((a, b) =>
            {
                int cmp = a.StartTime.CompareTo(b.StartTime);
                return cmp != 0 ? cmp : a.Pitch.CompareTo(b.Pitch);
            });

            var counts = new int[FingerAssignmentService.MaxFingers];
            var singleFlags = new bool[handNotes.Count];
            for (int i = 0; i < handNotes.Count; i++)
            {
                bool inChord = (i > 0 && System.Math.Abs(handNotes[i].StartTime - handNotes[i - 1].StartTime) < 0.02f) ||
                               (i + 1 < handNotes.Count && System.Math.Abs(handNotes[i + 1].StartTime - handNotes[i].StartTime) < 0.02f);
                singleFlags[i] = !inChord;
            }

            int assigned = 0;
            int stepTransitions = 0;
            int stepSameFingerPecks = 0;
            int prevSingleFinger = -1;
            int prevSingleIndex = -1;

            for (int i = 0; i < handNotes.Count; i++)
            {
                var n = handNotes[i];
                int f = service.PreviewFinger(leftHand, n.Pitch, n.StartTime);
                if (f < 0 || f >= FingerAssignmentService.MaxFingers) continue;
                counts[f]++;
                assigned++;

                if (singleFlags[i])
                {
                    if (prevSingleIndex >= 0)
                    {
                        var prev = handNotes[prevSingleIndex];
                        float dt = n.StartTime - prev.StartTime;
                        int dp = System.Math.Abs(n.Pitch - prev.Pitch);
                        if (dt >= 0.03f && dt < 0.65f && dp >= 1 && dp <= 4 &&
                            PianoNoteFilter.IsBlackKey(n.Pitch) == PianoNoteFilter.IsBlackKey(prev.Pitch))
                        {
                            stepTransitions++;
                            if (f == prevSingleFinger) stepSameFingerPecks++;
                        }
                    }
                    prevSingleFinger = f;
                    prevSingleIndex = i;
                }
                else
                {
                    prevSingleIndex = -1;
                    prevSingleFinger = -1;
                }
            }

            Assert.Greater(assigned, 20, $"{tag} 未分配足够指法");
            Assert.Greater(counts[0], 0, $"{tag} 拇指不得全程闲置 ({counts[0]})");
            int distinct = 0;
            int maxSingle = 0;
            for (int f = 0; f < counts.Length; f++)
            {
                if (counts[f] > 0) distinct++;
                if (counts[f] > maxSingle) maxSingle = counts[f];
            }
            float dominance = (float)maxSingle / assigned;
            float peckRatio = stepTransitions > 0 ? (float)stepSameFingerPecks / stepTransitions : 0f;
            UnityEngine.Debug.Log(
                $"[FINGERING] {tag} assigned={assigned} counts=[{counts[0]},{counts[1]},{counts[2]},{counts[3]},{counts[4]}] maxShare={dominance:F3} stepPeck={stepSameFingerPecks}/{stepTransitions}");
            Assert.GreaterOrEqual(distinct, minDistinctFingers, $"{tag} 应合理动用至少 {minDistinctFingers} 根手指");
            Assert.LessOrEqual(dominance, 0.55f, $"{tag} 不应长期由单一手指主控 ({dominance:F2})");
            if (stepTransitions >= 8)
                Assert.Less(peckRatio, 0.05f, $"{tag} 连贯级进乐句不应出现异音同指啄键 ({stepSameFingerPecks}/{stepTransitions})");
        }

        private static int CountAssigned(FingerAssignmentService service, bool leftHand)
        {
            int n = 0;
            for (int f = 0; f < FingerAssignmentService.MaxFingers; f++)
            {
                if (service.GetPitch(leftHand, f) >= 0) n++;
            }
            return n;
        }
    }
}
