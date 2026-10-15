using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using PianoTeacher3D.MIDI;

/// <summary>
/// MidiParser 纯逻辑单元测试（EditMode）。覆盖方案 8.1 的可解析/不可解析、和弦、力度、tempo。
/// </summary>
public class MidiParserTests
{
    // ==================== 可解析 ====================

    [Test]
    public void Parse_SingleNote_ReturnsOneNoteWithCorrectFields()
    {
        var data = BuildMidi(
            new[] {
                Tempo(500000, 0),              // 120 BPM
                NoteOn(0, 60, 100, 0),         // C4, vel=100, tick 0
                NoteOff(0, 60, 480)            // tick 480 结束
            });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.Notes.Count);
        var n = result.Notes[0];
        Assert.AreEqual(60, n.Pitch);
        Assert.AreEqual(100, n.Velocity);
        Assert.AreEqual(0, n.Channel);
        Assert.AreEqual(0, n.Track);
        // 120 BPM, PPQ 480 → 每拍 0.5s；480 tick = 1 拍 = 0.5s
        Assert.AreEqual(0.5f, n.Duration, 0.01f);
    }

    [Test]
    public void Parse_Chord_ReturnsMultipleNotesAtSameTime()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 48, 70, 0), NoteOn(0, 52, 70, 0), NoteOn(0, 55, 70, 0),
            NoteOff(0, 48, 480), NoteOff(0, 52, 480), NoteOff(0, 55, 480)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(3, result.Notes.Count);
        Assert.AreEqual(0f, result.Notes.Min(n => n.StartTime), 0.01f);
    }

    [Test]
    public void Parse_NoteOnVelocityZero_TreatedAsNoteOff()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOn(0, 60, 0, 480)  // velocity=0 视为 off
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Notes.Count);
        Assert.AreEqual(0.5f, result.Notes[0].Duration, 0.01f);
    }

    [Test]
    public void Parse_TempoChange_AffectsTiming()
    {
        // NoteOn 在 tick 0（120BPM），tick 480 处变 60BPM，NoteOff 在 tick 960
        // → 480 tick @120BPM(0.5s) + 480 tick @60BPM(1.0s) = 1.5s
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),      // tick 0, 120 BPM
            NoteOn(0, 60, 100, 0), // tick 0, Note On
            Tempo(1000000, 480),   // tick 480, 60 BPM
            NoteOff(0, 60, 480)    // tick 960, Note Off
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1.5f, result.Notes[0].Duration, 0.01f);
    }

    [Test]
    public void Parse_OutOfRangePitch_StillParsed()
    {
        // pitch 0 与 127 均在 MIDI 范围但超出 88 键，解析器不应崩溃（范围过滤由 PianoKeyMap 负责）
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 0, 100, 0), NoteOff(0, 0, 480),
            NoteOn(0, 127, 100, 0), NoteOff(0, 127, 480)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Notes.Count);
    }

    // ==================== 不可解析 ====================

    [Test]
    public void Parse_EmptyData_ReturnsFail()
    {
        var result = MidiParser.Parse(new byte[0]);
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_NullData_ReturnsFail()
    {
        var result = MidiParser.Parse(null);
        Assert.IsFalse(result.Success);
    }

    [Test]
    public void Parse_SmpteDivision_ReturnsFail()
    {
        // division 最高位 1 = SMPTE，首版不支持
        var data = BuildMidi(new[] { NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480) }, division: 0xE250);
        var result = MidiParser.Parse(data);
        Assert.IsFalse(result.Success);
    }

    [Test]
    public void Parse_TruncatedTrack_ReturnsFail()
    {
        // MTrk 声明长度大于实际数据
        var body = new List<byte>();
        body.AddRange(Ascii("MThd"));
        body.AddRange(U32(6));
        body.AddRange(U16(0));
        body.AddRange(U16(1));
        body.AddRange(U16(480));
        body.AddRange(Ascii("MTrk"));
        body.AddRange(U32(100)); // 声称 100 字节
        body.AddRange(new byte[] { 0x90, 0x3C, 0x64 }); // 实际只有 3 字节

        var result = MidiParser.Parse(body.ToArray());
        Assert.IsFalse(result.Success);
    }

    [Test]
    public void Parse_MissingHeader_ReturnsFail()
    {
        var result = MidiParser.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 });
        Assert.IsFalse(result.Success);
    }

    // ==================== tempo map（方案 2.2 节拍器 / 练习反馈的数据基础） ====================

    [Test]
    public void Parse_NoTempoEvent_HasDefaultTempoChangeAtZero()
    {
        var data = BuildMidi(new[]
        {
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 480)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.TempoChanges, "TempoChanges 不应为 null");
        Assert.AreEqual(1, result.TempoChanges.Count);
        Assert.AreEqual(0f, result.TempoChanges[0].TimeSeconds, 1e-4f, "首项必须为 t=0");
        Assert.AreEqual(TempoChange.DefaultMpq, result.TempoChanges[0].MicrosecondsPerQuarter);
        Assert.AreEqual(120f, result.TempoChanges[0].Bpm, 0.01f);
    }

    [Test]
    public void Parse_TempoChange_TimeConvertedToSeconds()
    {
        // tick 0 起 120BPM；tick 480（= 1 拍 = 0.5s）起 60BPM
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            Tempo(1000000, 480),
            NoteOff(0, 60, 480)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.TempoChanges.Count);
        Assert.AreEqual(0f, result.TempoChanges[0].TimeSeconds, 1e-3f);
        Assert.AreEqual(0.5f, result.TempoChanges[1].TimeSeconds, 1e-3f, "480 tick @120BPM = 0.5s");
        Assert.AreEqual(60f, result.TempoChanges[1].Bpm, 0.01f);
    }

    [Test]
    public void Parse_FailedResult_StillExposesDefaultTempoChanges()
    {
        var result = MidiParser.Parse(new byte[0]);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.TempoChanges, "失败结果也不应返回 null tempo map");
        Assert.AreEqual(1, result.TempoChanges.Count);
        Assert.AreEqual(120f, result.TempoChanges[0].Bpm, 0.01f);
    }

    // ==================== 方案 8.1：快速 / 长 / 弱 / 强音事件 ====================

    [Test]
    public void Parse_VeryShortNote_PreservesSmallDuration()
    {
        // 5ms 的极短音符（480 PPQ @120BPM → 1 tick ≈ 1.04ms；5 tick ≈ 5.2ms）
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 5)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Notes.Count);
        Assert.Greater(result.Notes[0].Duration, 0f, "极短音符仍应有正的持续时间");
        Assert.Less(result.Notes[0].Duration, 0.02f);
    }

    [Test]
    public void Parse_LongNote_PreservesLargeDuration()
    {
        // 10 拍长音（480 PPQ × 10 = 4800 tick @120BPM ≈ 5 秒）
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 4800)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(5f, result.Notes[0].Duration, 0.02f);
        Assert.AreEqual(5f, result.TotalDurationSeconds, 0.02f);
    }

    [Test]
    public void Parse_ExtremeVelocity_PreservedExactly()
    {
        // 最弱（1）与最强（127）都应原样保留
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 1, 0),
            NoteOff(0, 60, 240),
            NoteOn(0, 64, 127, 0),
            NoteOff(0, 64, 240)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Notes.Count);
        Assert.AreEqual(1, result.Notes[0].Velocity, "最弱力度应保留为 1");
        Assert.AreEqual(127, result.Notes[1].Velocity, "最强力度应保留为 127");
    }

    [Test]
    public void Parse_RapidSequence_AllNotesRetained()
    {
        // 连续 16 个 1/16 音符（每个 120 tick ≈ 0.125s），全部应保留且时间递增
        var events = new List<byte[]> { Tempo(500000, 0) };
        for (int i = 0; i < 16; i++)
        {
            events.Add(NoteOn(0, 60 + i, 90, 0));
            events.Add(NoteOff(0, 60 + i, 120));
        }

        var data = BuildMidi(events);
        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(16, result.Notes.Count, "快速连续音符不应丢失");
        for (int i = 1; i < result.Notes.Count; i++)
            Assert.GreaterOrEqual(result.Notes[i].StartTime, result.Notes[i - 1].StartTime);
    }

    // ==================== 回归：未关闭 Note On 不再静默丢弃（缺陷 B5） ====================

    [Test]
    public void Parse_UnclosedNoteOn_IsCompletedNotDroppedAndCounted()
    {
        // 两个 Note On 均无对应 Note Off；另有一个悬空 Note Off 仅用于推进轨道末尾
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOn(0, 64, 90, 120),
            NoteOff(0, 72, 240)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Notes.Count, "未关闭的音符不应被丢弃");
        Assert.AreEqual(2, result.DanglingNoteCount, "应上报被补全的音符数");
        Assert.Greater(result.Notes[0].Duration, 0f, "补全的音符应有正的持续时间");
    }

    [Test]
    public void Parse_AllNotesProperlyClosed_DanglingCountIsZero()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 480)
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Notes.Count);
        Assert.AreEqual(0, result.DanglingNoteCount, "正常曲目不应上报异常音符");
    }

    // ==================== MIDI 字节构造 helper ====================

    private static byte[] BuildMidi(IEnumerable<byte[]> events, ushort division = 480)
    {
        var track = new List<byte>();
        foreach (var e in events) track.AddRange(e);
        track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 }); // End of Track（delta 0 + meta 0x2F）

        var bytes = new List<byte>();
        bytes.AddRange(Ascii("MThd"));
        bytes.AddRange(U32(6));
        bytes.AddRange(U16(0)); // format 0
        bytes.AddRange(U16(1)); // 1 track
        bytes.AddRange(U16(division));
        bytes.AddRange(Ascii("MTrk"));
        bytes.AddRange(U32((uint)track.Count));
        bytes.AddRange(track);
        return bytes.ToArray();
    }

    private static byte[] Tempo(int microsPerQuarter, int deltaTicks)
    {
        var b = new List<byte>();
        b.AddRange(VLQ(deltaTicks));
        b.Add(0xFF);
        b.Add(0x51);
        b.Add(0x03);
        b.Add((byte)(microsPerQuarter >> 16));
        b.Add((byte)(microsPerQuarter >> 8));
        b.Add((byte)microsPerQuarter);
        return b.ToArray();
    }

    private static byte[] NoteOn(int channel, int pitch, int velocity, int deltaTicks)
    {
        var b = new List<byte>();
        b.AddRange(VLQ(deltaTicks));
        b.Add((byte)(0x90 | channel));
        b.Add((byte)pitch);
        b.Add((byte)velocity);
        return b.ToArray();
    }

    private static byte[] NoteOff(int channel, int pitch, int deltaTicks)
    {
        var b = new List<byte>();
        b.AddRange(VLQ(deltaTicks));
        b.Add((byte)(0x80 | channel));
        b.Add((byte)pitch);
        b.Add(0);
        return b.ToArray();
    }

    private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    private static byte[] U16(ushort v) => new[] { (byte)(v >> 8), (byte)v };

    private static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static byte[] VLQ(int value)
    {
        var bytes = new List<byte>();
        var buffer = value & 0x7F;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= (value & 0x7F) | 0x80;
        }
        while (true)
        {
            bytes.Add((byte)(buffer & 0xFF));
            if ((buffer & 0x80) != 0) buffer >>= 8;
            else break;
        }
        return bytes.ToArray();
    }
}
