using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

/// <summary>
/// MIDI 兼容性回归测试（EditMode，对应方案 11.1「MIDI Format 0/1/2、running status、VLQ、
/// tempo 变化、和弦、空文件和损坏文件」与 6.4 的兼容策略）。
/// <para>全部用内存 / 临时文件构造字节流，不依赖场景，可在 EditMode 直接运行。</para>
/// </summary>
public class MidiCompatibilityTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_MidiCompat");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch (Exception) { /* 清理失败不影响测试结论 */ }
    }

    // ==================== Format 0 / 1 / 2 ====================

    [Test]
    public void Parse_Format0_SingleTrack_ReportsFormat0WithoutWarnings()
    {
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(0, result.MidiFormat);
        Assert.AreEqual(1, result.Notes.Count);
        Assert.IsNotNull(result.Warnings, "Warnings 永不为 null");
        Assert.AreEqual(0, result.Warnings.Count, "Format 0 正常文件不应产生 Warnings");
    }

    [Test]
    public void Parse_Format1_TwoSynchronousTracks_BothNotesStartAtZero()
    {
        var data = BuildMidi(1, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480)),
            Concat(NoteOn(1, 48, 90, 0), NoteOff(1, 48, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.MidiFormat);
        Assert.AreEqual(2, result.TrackCount);
        Assert.AreEqual(2, result.Notes.Count);
        Assert.AreEqual(0f, result.Notes.Min(n => n.StartTime), 1e-3f, "Format 1 各轨应同步从 0 开始");
        Assert.AreEqual(0, result.Warnings.Count, "Format 1 不应产生兼容性 Warnings");
    }

    [Test]
    public void Parse_Format2_ConcatenatesPatternsSequentially_AndWarns()
    {
        // 两个独立序列：各自都是 0..480 tick 的一个音；串接后第二个应平移到 480 tick 之后
        var data = BuildMidi(2, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480)),
            Concat(NoteOn(0, 64, 100, 0), NoteOff(0, 64, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(2, result.MidiFormat);
        Assert.AreEqual(2, result.Notes.Count);
        Assert.Greater(result.Warnings.Count, 0, "Format 2 必须给出兼容性提示");

        var starts = result.Notes.Select(n => n.StartTime).OrderBy(t => t).ToList();
        Assert.AreEqual(0f, starts[0], 0.02f);
        // 480 tick @120BPM/PPQ480 = 0.5s → 第二个序列从 0.5s 开始
        Assert.AreEqual(0.5f, starts[1], 0.03f, "Format 2 的各序列应顺序串接到同一时间轴");
    }

    [Test]
    public void Parse_Format2_SingleTrack_StillWarns()
    {
        var data = BuildMidi(2, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(2, result.MidiFormat);
        Assert.Greater(result.Warnings.Count, 0);
    }

    // ==================== running status / VLQ ====================

    [Test]
    public void Parse_RunningStatus_ReusesPreviousStatusByte()
    {
        var track = new List<byte>();
        track.AddRange(Tempo(500000, 0));
        track.AddRange(NoteOn(0, 60, 100, 0));
        track.AddRange(Concat(VLQ(0), new byte[] { 0x3C, 0x50 }));   // running status：再按一次 60（力度 80）
        track.AddRange(NoteOff(0, 60, 240));
        track.AddRange(Concat(VLQ(0), new byte[] { 0x3C, 0x00 }));   // running status：再松开一次 60

        var result = MidiParser.Parse(BuildMidi(0, new[] { track.ToArray() }));

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(2, result.Notes.Count, "running status 事件不应丢失");
        Assert.AreEqual(0, result.DanglingNoteCount);
    }

    [Test]
    public void Parse_VlqMultiByteDelta_IsDecodedCorrectly()
    {
        // delta 128 用两字节 VLQ 编码（0x81 0x00）：128 tick @120BPM/PPQ480 ≈ 0.1333s
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 128), NoteOff(0, 60, 240))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.Notes.Count);
        Assert.AreEqual(0.1333f, result.Notes[0].StartTime, 0.005f, "多字节 VLQ 应被正确解码");
    }

    // ==================== tempo ====================

    [Test]
    public void Parse_TempoChange_ShiftsSubsequentTiming()
    {
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), Tempo(1000000, 480), NoteOff(0, 60, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1.5f, result.Notes[0].Duration, 0.02f, "tempo 变化后时长应分段累积");
        Assert.AreEqual(2, result.TempoChanges.Count);
    }

    [Test]
    public void Parse_InvalidTempo_FallsBackTo120Bpm_WithWarning()
    {
        // tempo = 0 属异常值，应回落 120 BPM：480 tick = 0.5s
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(0, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(0.5f, result.Notes[0].Duration, 0.02f, "异常 tempo 应回落到 120 BPM");
        Assert.Greater(result.Warnings.Count, 0, "异常 tempo 应产生 Warnings");
        Assert.AreEqual(120f, result.TempoChanges[0].Bpm, 0.01f);
    }

    [Test]
    public void Parse_AbsurdlyLargeTempo_FallsBackTo120BpmWithWarning()
    {
        // tempo = 0xFFFFFF（≈16.8 秒/拍，远低于 10 BPM）属异常值，应回落 120 BPM → 480 tick = 0.5s
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(0xFFFFFF, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(0.5f, result.Notes[0].Duration, 0.02f, "极大 tempo 应回落到 120 BPM");
        Assert.Greater(result.Warnings.Count, 0, "极大 tempo 应产生 Warnings");
    }

    // ==================== 和弦 ====================

    [Test]
    public void Parse_Chord_SimultaneousNoteOnsProduceSeparateNotes()
    {
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0),
                NoteOn(0, 48, 70, 0), NoteOn(0, 52, 70, 0), NoteOn(0, 55, 70, 0),
                NoteOff(0, 48, 480), NoteOff(0, 52, 480), NoteOff(0, 55, 480))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(3, result.Notes.Count);
        Assert.AreEqual(0, result.DanglingNoteCount);
        Assert.AreEqual(0f, result.Notes.Max(n => n.StartTime), 1e-3f);
    }

    // ==================== 空文件 / 损坏文件（不得抛异常） ====================

    [Test]
    public void Parse_EmptyArray_ReturnsFailWithoutThrowing()
    {
        MidiParseResult result = null;
        Assert.DoesNotThrow(() => result = MidiParser.Parse(new byte[0]));
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_NullData_ReturnsFailWithoutThrowing()
    {
        MidiParseResult result = null;
        Assert.DoesNotThrow(() => result = MidiParser.Parse(null));
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_CorruptedHeaderChunkId_ReturnsFail()
    {
        var data = BuildMidi(0, new[] { NoteOn(0, 60, 100, 0) });
        data[1] = (byte)'X'; // "MThd" → "MXhd"

        var result = MidiParser.Parse(data);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_TruncatedTrackData_ReturnsFail()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Ascii("MThd"));
        bytes.AddRange(U32(6));
        bytes.AddRange(U16(0));
        bytes.AddRange(U16(1));
        bytes.AddRange(U16(480));
        bytes.AddRange(Ascii("MTrk"));
        bytes.AddRange(U32(100));                 // 声称 100 字节
        bytes.AddRange(new byte[] { 0x90, 0x3C, 0x64 }); // 实际只有 3 字节

        var result = MidiParser.Parse(bytes.ToArray());

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_MissingMThd_ReturnsFail()
    {
        var result = MidiParser.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 });
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_SmpteTimeBase_ReturnsFail()
    {
        var data = BuildMidi(0, new[] { Concat(NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480)) }, division: 0xE250);
        var result = MidiParser.Parse(data);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [Test]
    public void Parse_ZeroPpq_ReturnsFail()
    {
        var data = BuildMidi(0, new[] { Concat(NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480)) }, division: 0);
        var result = MidiParser.Parse(data);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    // ==================== 缺 Note Off（悬空音符） ====================

    [Test]
    public void Parse_MissingNoteOff_StillProducesNotesAndCountsDangling()
    {
        var data = BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOn(0, 64, 90, 240), NoteOff(0, 72, 240))
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(2, result.Notes.Count, "缺 Note Off 的音符仍应产出");
        Assert.Greater(result.DanglingNoteCount, 0, "应上报被补全的音符数");
    }

    // ==================== 校验器：上限与拒绝归类（方案 6.4 / 8.2） ====================

    [Test]
    public void Validate_NoteCountOverLimit_ReturnsTooComplex()
    {
        var path = Path.Combine(_dir, "many.mid");
        File.WriteAllBytes(path, BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0),
                NoteOn(0, 60, 100, 0), NoteOff(0, 60, 120),
                NoteOn(0, 62, 100, 0), NoteOff(0, 62, 120),
                NoteOn(0, 64, 100, 0), NoteOff(0, 64, 120))
        }));

        var check = MidiFileValidator.Validate(path, maxNotes: 2);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.TooComplex, check.Reason);
        Assert.IsFalse(string.IsNullOrEmpty(check.UserMessage));
        Assert.AreEqual(RecoveryAction.ChooseFile, check.ToError().Recovery);
    }

    [Test]
    public void Validate_DurationOverLimit_ReturnsTooComplex()
    {
        var path = Path.Combine(_dir, "long.mid");
        File.WriteAllBytes(path, BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 4800))
        }));

        var check = MidiFileValidator.Validate(path, maxDuration: 1f); // 曲目约 5 秒，上限 1 秒

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.TooComplex, check.Reason);
    }

    [Test]
    public void Validate_ValidFile_ReturnsOkWithParseResult()
    {
        var path = Path.Combine(_dir, "ok.mid");
        File.WriteAllBytes(path, BuildMidi(0, new[]
        {
            Concat(Tempo(500000, 0), NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480))
        }));

        var check = MidiFileValidator.Validate(path);

        Assert.IsTrue(check.Ok, check.UserMessage);
        Assert.AreEqual(ImportRejection.None, check.Reason);
        Assert.IsNotNull(check.Parse);
        Assert.AreEqual(1, check.Parse.Notes.Count);
    }

    [Test]
    public void Validate_CorruptedMidi_ReturnsHeaderInvalid()
    {
        var path = Path.Combine(_dir, "broken.mid");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });

        var check = MidiFileValidator.Validate(path);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.HeaderInvalid, check.Reason);
        Assert.AreEqual(ErrorCode.MidiHeaderInvalid, check.ToError().Code);
    }

    // ==================== 字节构造 helper ====================

    private static byte[] BuildMidi(int format, byte[][] tracks, ushort division = 480)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Ascii("MThd"));
        bytes.AddRange(U32(6));
        bytes.AddRange(U16((ushort)format));
        bytes.AddRange(U16((ushort)tracks.Length));
        bytes.AddRange(U16(division));

        for (int i = 0; i < tracks.Length; i++)
        {
            var body = new List<byte>(tracks[i]);
            body.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 }); // End of Track
            bytes.AddRange(Ascii("MTrk"));
            bytes.AddRange(U32((uint)body.Count));
            bytes.AddRange(body);
        }
        return bytes.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var list = new List<byte>();
        for (int i = 0; i < parts.Length; i++) list.AddRange(parts[i]);
        return list.ToArray();
    }

    private static byte[] Tempo(int microsPerQuarter, int deltaTicks)
        => Concat(VLQ(deltaTicks), new byte[]
        {
            0xFF, 0x51, 0x03,
            (byte)(microsPerQuarter >> 16), (byte)(microsPerQuarter >> 8), (byte)microsPerQuarter
        });

    private static byte[] NoteOn(int channel, int pitch, int velocity, int deltaTicks)
        => Concat(VLQ(deltaTicks), new byte[] { (byte)(0x90 | channel), (byte)pitch, (byte)velocity });

    private static byte[] NoteOff(int channel, int pitch, int deltaTicks)
        => Concat(VLQ(deltaTicks), new byte[] { (byte)(0x80 | channel), (byte)pitch, 0x00 });

    private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    private static byte[] U16(ushort v) => new[] { (byte)(v >> 8), (byte)v };

    private static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static byte[] VLQ(int value)
    {
        var bytes = new List<byte>();
        int buffer = value & 0x7F;
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
