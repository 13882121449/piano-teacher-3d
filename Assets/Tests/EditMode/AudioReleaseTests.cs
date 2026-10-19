using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.EditMode
{
    /// <summary>
    /// 音频正确释放测试（对应<b>方案 7.3 音频优先级</b>：Note On/Off、暂停、拖动、切曲、循环边界都必须正确释放）。
    /// <para>
    /// <b>不依赖真实音频设备</b>：EditMode 下 <see cref="PianoAudioSynth.Initialize"/> 必然降级（<c>Application.isPlaying == false</c>），
    /// 正好用于验证"初始化失败不抛异常 + 上层可静音继续"；声部池的容量/淡出/抢占等核心逻辑
    /// 由同文件的纯逻辑类 <see cref="PianoVoiceAllocator"/> 直接覆盖，与设备无关、结果确定。
    /// </para>
    /// </summary>
    public class AudioReleaseTests
    {
        private const float FadeSeconds = PianoVoiceAllocator.DefaultFadeSeconds;

        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            }
            _objects.Clear();
        }

        private PianoAudioSynth CreateSynth()
        {
            var go = new GameObject("AudioSynthTest");
            _objects.Add(go);
            return go.AddComponent<PianoAudioSynth>();
        }

        private static MidiNoteEvent Note(int pitch, int velocity = 100)
            => new MidiNoteEvent(pitch, 0f, 1f, velocity, 0, 0);

        // ==================== 初始化失败防护 ====================

        [Test]
        public void Initialize_WithoutAudioAvailability_ReportsChineseErrorAndStaysSafe()
        {
            var synth = CreateSynth();

            Assert.DoesNotThrow(() => synth.Initialize(), "初始化失败不得抛异常");

            Assert.IsFalse(synth.Initialized, "EditMode（非播放模式）下音频应不可用");
            Assert.IsFalse(string.IsNullOrEmpty(synth.LastInitError), "必须写入中文失败原因供上层上报");
            Assert.AreEqual(0, synth.ActiveVoiceCount, "未初始化时不应有在发声部");

            Assert.DoesNotThrow(() => synth.OnNoteOn(Note(60)), "音频不可用时 OnNoteOn 必须静默返回");
            Assert.DoesNotThrow(() => synth.OnNoteOff(Note(60)), "音频不可用时 OnNoteOff 必须静默返回");
            Assert.DoesNotThrow(() => synth.ReleaseAll());
            Assert.DoesNotThrow(() => synth.Initialize(), "重复 Initialize 必须幂等且安全");

            Assert.AreEqual(0, synth.ActiveVoiceCount);
        }

        [Test]
        public void NoteEvents_BeforeInitialize_AreSafeNoOps()
        {
            var synth = CreateSynth();

            Assert.DoesNotThrow(() => synth.OnNoteOn(Note(60)));
            Assert.DoesNotThrow(() => synth.OnNoteOff(Note(60)));
            Assert.DoesNotThrow(() => synth.ReleaseAll());

            Assert.AreEqual(0, synth.ActiveVoiceCount);
            Assert.IsNull(synth.LastInitError, "尚未初始化时不应凭空产生错误信息");
        }

        [Test]
        public void Muted_NoteOnDoesNotOccupyAnyVoice()
        {
            var synth = CreateSynth();
            synth.Initialize();

            synth.Muted = true;
            Assert.IsTrue(synth.Muted, "Muted 必须可读写");

            Assert.DoesNotThrow(() => synth.OnNoteOn(Note(60)));
            Assert.AreEqual(0, synth.ActiveVoiceCount, "静音期间 NoteOn 不得占用声部");

            synth.Muted = false;
            Assert.IsFalse(synth.Muted);
            Assert.AreEqual(0, synth.ActiveVoiceCount);
        }

        [Test]
        public void SetMasterVolume_ClampsToUnitRange()
        {
            var synth = CreateSynth();

            synth.SetMasterVolume(5f);
            Assert.AreEqual(1f, synth.MasterVolume, 1e-6f, "超过 1 必须钳制为 1");

            synth.SetMasterVolume(-3f);
            Assert.AreEqual(0f, synth.MasterVolume, 1e-6f, "低于 0 必须钳制为 0");

            synth.SetMasterVolume(0.4f);
            Assert.AreEqual(0.4f, synth.MasterVolume, 1e-6f, "合法值应原样保留");
        }

        [Test]
        public void OnNoteOff_ForSilentPitch_DoesNotThrow()
        {
            var synth = CreateSynth();
            synth.Initialize();

            Assert.DoesNotThrow(() => synth.OnNoteOff(Note(PianoKeyMap.MinPitch)));
            Assert.DoesNotThrow(() => synth.OnNoteOff(Note(PianoKeyMap.MaxPitch)));
            Assert.DoesNotThrow(() => synth.OnNoteOff(Note(127)));   // 超出 88 键范围
            Assert.AreEqual(0, synth.ActiveVoiceCount);
        }

        // ==================== 声部池纯逻辑（PianoVoiceAllocator） ====================

        [Test]
        public void Allocator_ReleaseAll_BringsActiveVoiceCountToZero()
        {
            var allocator = new PianoVoiceAllocator(8, FadeSeconds);

            for (int i = 0; i < 5; i++) allocator.NoteOn(60 + i, 100);
            Assert.AreEqual(5, allocator.ActiveCount);

            allocator.ReleaseAll();

            Assert.AreEqual(0, allocator.ActiveCount, "ReleaseAll 后不得再有在发声部");
            Assert.Greater(allocator.OccupiedCount, 0, "槽位应仍在淡出中占用（随后归还池）");

            allocator.AdvanceReleases(FadeSeconds * 1.5f);
            Assert.AreEqual(0, allocator.OccupiedCount, "淡出完成后所有槽位必须归还池");
        }

        [Test]
        public void Allocator_NoteOnNeverExceedsPoolCapacity()
        {
            var allocator = new PianoVoiceAllocator(PianoAudioSynth.MaxVoices, FadeSeconds);

            for (int i = 0; i < 200; i++)
            {
                int pitch = PianoKeyMap.MinPitch + (i % PianoKeyMap.KeyCount);
                allocator.NoteOn(pitch, 100);

                Assert.LessOrEqual(allocator.ActiveCount, allocator.Capacity,
                    "连续触发 200 个 NoteOn 后声部数不得超过池上限");
            }

            Assert.AreEqual(PianoAudioSynth.MaxVoices, allocator.ActiveCount, "池应被填满但不再增长");
            Assert.AreEqual(allocator.Capacity, allocator.OccupiedCount);
        }

        [Test]
        public void Allocator_NoteOffForUnknownPitch_LeavesOtherVoicesIntact()
        {
            var allocator = new PianoVoiceAllocator(8, FadeSeconds);

            int a = allocator.NoteOn(60, 100);
            int b = allocator.NoteOn(64, 80);
            Assert.AreEqual(2, allocator.ActiveCount);

            int result = allocator.NoteOff(70);   // 从未发声

            Assert.AreEqual(-1, result, "未发声的音高应返回 -1（安全空操作）");
            Assert.AreEqual(2, allocator.ActiveCount, "不得改变其他声部");
            Assert.AreEqual(60, allocator.GetPitch(a));
            Assert.AreEqual(64, allocator.GetPitch(b));
            Assert.IsFalse(allocator.IsReleasing(a));
            Assert.IsFalse(allocator.IsReleasing(b));
        }

        [Test]
        public void Allocator_VoiceStealCount_CountsOnlyPoolFullTakeovers()
        {
            var allocator = new PianoVoiceAllocator(2, FadeSeconds);

            allocator.NoteOn(60, 100);
            allocator.NoteOn(64, 100);
            Assert.AreEqual(0, allocator.StealCount, "池未满时不得计抢占");

            allocator.NoteOn(67, 100);   // 池满 → 抢占一个槽位
            Assert.AreEqual(1, allocator.StealCount, "池满后每次 NoteOn 记一次 voice-stealing");
            Assert.AreEqual(2, allocator.OccupiedCount, "抢占后占用数不得超过容量");

            allocator.NoteOn(67, 50);    // 同音高重复触发（repeat note）复用原槽位，不算抢占
            Assert.AreEqual(1, allocator.StealCount, "同音高复用不得计抢占");
        }

        [Test]
        public void Allocator_VoiceStealCount_NotIncrementedAfterSlotFreed()
        {
            var allocator = new PianoVoiceAllocator(2, FadeSeconds);

            allocator.NoteOn(60, 100);
            allocator.NoteOn(64, 100);
            allocator.NoteOff(60);
            allocator.AdvanceReleases(FadeSeconds * 1.5f);

            allocator.NoteOn(67, 100);   // 淡出完成后有真实空闲槽位

            Assert.AreEqual(0, allocator.StealCount, "有空闲槽位时不得记抢占");
            Assert.AreEqual(2, allocator.ActiveCount);
        }

        [Test]
        public void Allocator_AdvanceReleases_FreesSlotOnlyAfterFadeCompletes()
        {            var allocator = new PianoVoiceAllocator(4, FadeSeconds);

            int slot = allocator.NoteOn(60, 100);
            allocator.NoteOff(60);

            Assert.AreEqual(0, allocator.ActiveCount, "Note Off 后该声部立即不再计入在发声部");
            Assert.AreEqual(1, allocator.OccupiedCount, "淡出期间槽位仍被占用");

            allocator.AdvanceReleases(FadeSeconds * 0.5f);
            Assert.AreEqual(1, allocator.OccupiedCount, "淡出未完成时不得归还槽位");
            Assert.AreEqual(60, allocator.GetPitch(slot));

            int completed = allocator.AdvanceReleases(FadeSeconds * 0.6f);
            Assert.AreEqual(1, completed, "淡出完成后应归还 1 个槽位");
            Assert.AreEqual(0, allocator.OccupiedCount);
            Assert.AreEqual(-1, allocator.GetPitch(slot));
        }

        [Test]
        public void Allocator_PrefersReleasingVoiceWhenPoolIsSaturated()
        {
            var allocator = new PianoVoiceAllocator(2, FadeSeconds);

            int first = allocator.NoteOn(60, 100);
            int second = allocator.NoteOn(64, 100);
            allocator.NoteOff(60);                        // first 进入淡出

            allocator.NoteOn(67, 100);                    // 池已满 → 应抢占正在淡出的 first

            Assert.AreEqual(67, allocator.GetPitch(first), "应优先复用正在淡出的槽位");
            Assert.AreEqual(64, allocator.GetPitch(second), "不应抢占仍在发声的声部");
            Assert.AreEqual(2, allocator.OccupiedCount);
        }

        [Test]
        public void Allocator_SamePitchRetrigger_ReusesSingleSlot()
        {
            var allocator = new PianoVoiceAllocator(8, FadeSeconds);

            int a = allocator.NoteOn(60, 100);
            int b = allocator.NoteOn(60, 40);

            Assert.AreEqual(a, b, "同音重复触发应复用同一槽位，避免无谓占用声部");
            Assert.AreEqual(1, allocator.ActiveCount);
        }

        [Test]
        public void Allocator_MasterVolumeAndVelocityGainAreClamped()
        {
            var allocator = new PianoVoiceAllocator(4, FadeSeconds);

            allocator.SetMasterVolume(2f);
            Assert.AreEqual(1f, allocator.MasterVolume, 1e-6f);

            allocator.SetMasterVolume(-1f);
            Assert.AreEqual(0f, allocator.MasterVolume, 1e-6f);

            Assert.Greater(PianoVoiceAllocator.VelocityGain(0), 0f, "力度 0 仍应保留最小可闻底噪（不为负）");
            Assert.AreEqual(1f, PianoVoiceAllocator.VelocityGain(127), 1e-6f, "最强力度应为满增益");
            Assert.LessOrEqual(PianoVoiceAllocator.VelocityGain(127), 1f, "力度增益不得溢出 1");
            Assert.Greater(PianoVoiceAllocator.VelocityGain(100), PianoVoiceAllocator.VelocityGain(40),
                "力度越大音量增益越高");

            allocator.SetMasterVolume(0.5f);
            int slot = allocator.NoteOn(60, 127);
            Assert.AreEqual(0.5f, allocator.GetVolume(slot), 1e-5f, "音量 = 主音量 × 力度增益");

            allocator.SetMasterVolume(0f);
            Assert.AreEqual(0f, allocator.GetVolume(slot), 1e-6f, "主音量为 0 时输出必须为静音");
        }
    }
}
