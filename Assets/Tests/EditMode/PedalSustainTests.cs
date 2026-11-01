using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using PianoTeacher3D.MIDI;
using UnityEngine;

/// <summary>
/// 延音踏板数据链（CC64 → 踏板区间 → 自动延音兜底）的单元测试。
/// <para>
/// 为什么单独测这一层：脚部动作是「数据 → 骨骼」的单向链路，骨骼那半截只能靠截图验收，
/// 而数据这半截（区间配对、开区间边界、无踏板兜底）完全可以断言。
/// </para>
/// </summary>
public class PedalSustainTests
{
    private const int Division = 480;

    // ==================== CC64 解析 ====================

    [Test]
    public void Parse_MidiWithoutCc64_ReturnsEmptySustainSpans()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 480),
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.SustainSpans);
        Assert.AreEqual(0, result.SustainSpans.Count);
    }

    [Test]
    public void Parse_Cc64DownThenUp_ProducesOneSpanWithSecondTimes()
    {
        // 120 BPM + PPQ 480 → 960 tick = 1 秒
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            Cc64(0, 127, 0),        // tick 0 踩下
            Cc64(0, 0, 960),        // tick 960 抬起 = 1.0 s
            NoteOff(0, 60, 960),
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.SustainSpans.Count);
        Assert.AreEqual(0f, result.SustainSpans[0].Start, 0.01f);
        Assert.AreEqual(1f, result.SustainSpans[0].End, 0.01f);
    }

    [Test]
    public void Parse_TwoPedalCycles_ProducesTwoOrderedSpans()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            Cc64(0, 127, 0),
            Cc64(0, 0, 480),        // 0 → 0.5 s
            Cc64(0, 127, 480),      // tick 960 再次踩下
            Cc64(0, 0, 480),        // tick 1440 抬起 = 1.5 s
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(2, result.SustainSpans.Count);
        Assert.Less(result.SustainSpans[0].End, result.SustainSpans[1].Start + 1e-4f,
            "区间必须升序且不重叠");
        Assert.AreEqual(1.5f, result.SustainSpans[1].End, 0.01f);
    }

    [Test]
    public void Parse_PedalHeldToEnd_ClosesSpanAtLastTick()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            NoteOn(0, 60, 100, 0),
            Cc64(0, 127, 0),
            NoteOff(0, 60, 960),
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.SustainSpans.Count);
        Assert.AreEqual(1f, result.SustainSpans[0].End, 0.01f, "一直踩到底应收到全曲末尾而不是丢弃");
    }

    [Test]
    public void Parse_PedalValueBelow64_IsTreatedAsReleased()
    {
        var data = BuildMidi(new[]
        {
            Tempo(500000, 0),
            Cc64(0, 127, 0),
            Cc64(0, 63, 480),       // 63 < 64：踏板已抬起
        });

        var result = MidiParser.Parse(data);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.SustainSpans.Count);
        Assert.AreEqual(0.5f, result.SustainSpans[0].End, 0.01f);
    }

    [Test]
    public void SustainSpan_ContainsIsHalfOpen()
    {
        var span = new SustainSpan(1f, 2f);

        Assert.IsFalse(span.Contains(0.99f));
        Assert.IsTrue(span.Contains(1f), "按下瞬间就算踩住");
        Assert.IsTrue(span.Contains(1.99f));
        Assert.IsFalse(span.Contains(2f), "抬起瞬间算松开，避免与下一段重叠");
    }

    // ==================== 无 CC64 的自动延音 ====================

    [Test]
    public void AutoSustain_LegatoNotes_ProducesSingleSpan()
    {
        var notes = new List<MidiNoteEvent>
        {
            new MidiNoteEvent(60, 0f, 1f, 90, 0, 0),
            new MidiNoteEvent(62, 0.9f, 1f, 90, 0, 0),   // 与上音小幅重叠
        };

        var spans = PedalFootAnimator.BuildAutoSustainSpans(notes);

        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(0f, spans[0].Start, 0.01f);
        Assert.AreEqual(1.9f, spans[0].End, 0.01f);
    }

    [Test]
    public void AutoSustain_GapBetweenNotes_ProducesTwoSpans()
    {
        var notes = new List<MidiNoteEvent>
        {
            new MidiNoteEvent(60, 0f, 0.4f, 90, 0, 0),
            new MidiNoteEvent(62, 1.0f, 0.4f, 90, 0, 0),
        };

        var spans = PedalFootAnimator.BuildAutoSustainSpans(notes);

        Assert.AreEqual(2, spans.Count, "断开的地方必须抬踏板，否则听起来一直在响");
        Assert.Less(spans[0].End, spans[1].Start + 1e-4f);
    }

    [Test]
    public void AutoSustain_LongHeldChord_SplitsIntoSegments()
    {
        var notes = new List<MidiNoteEvent>
        {
            new MidiNoteEvent(60, 0f, 5f, 90, 0, 0),   // 单音持续 5 秒
        };

        var spans = PedalFootAnimator.BuildAutoSustainSpans(notes, maxSpanSeconds: 1f);

        Assert.GreaterOrEqual(spans.Count, 2, "长音应分段换踏板，而不是一口气踩 5 秒");
        for (int i = 0; i < spans.Count; i++)
            Assert.LessOrEqual(spans[i].End - spans[i].Start, 1f + 1e-3f);
    }

    [Test]
    public void AutoSustain_NoNotes_ReturnsEmpty()
    {
        Assert.AreEqual(0, PedalFootAnimator.BuildAutoSustainSpans(null).Count);
        Assert.AreEqual(0, PedalFootAnimator.BuildAutoSustainSpans(new List<MidiNoteEvent>()).Count);
    }

    // ==================== 组件边界行为 ====================

    [Test]
    public void PedalFootAnimator_WithoutBindingIsInert()
    {
        var go = new GameObject("PedalFootTest");
        try
        {
            var animator = go.AddComponent<PedalFootAnimator>();

            Assert.IsFalse(animator.IsBound);
            Assert.DoesNotThrow(() => animator.Tick(true, 0.016f), "没有骨架时不能抛异常");
            Assert.DoesNotThrow(() => animator.Bind(null), "传 null 必须安全");
            Assert.AreEqual(0f, animator.CurrentAngleDegrees, 1e-4f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // ==================== MIDI 字节构造（最小实现，只覆盖本文件用到的消息）====================

    private static byte[] BuildMidi(IEnumerable<byte[]> events)
    {
        var track = new List<byte>();
        foreach (var e in events) track.AddRange(e);
        track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });

        var bytes = new List<byte>();
        bytes.AddRange(Ascii("MThd"));
        bytes.AddRange(U32(6));
        bytes.AddRange(U16(0));        // format 0
        bytes.AddRange(U16(1));        // 1 track
        bytes.AddRange(U16(Division));
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

    private static byte[] Cc64(int channel, int value, int deltaTicks)
    {
        var b = new List<byte>();
        b.AddRange(VLQ(deltaTicks));
        b.Add((byte)(0xB0 | channel));
        b.Add(64);
        b.Add((byte)value);
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
