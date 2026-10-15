using NUnit.Framework;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

/// <summary>
/// 边界条件补充测试（EditMode）：空曲目、过滤空结果、时间轴参数越界等（方案 8.1）。
/// </summary>
public class EdgeCaseTests
{
    // ---- 音符过滤的边界 ----

    [Test]
    public void FilterByHand_EmptyList_ReturnsEmpty()
    {
        var result = PianoNoteFilter.FilterByHand(new System.Collections.Generic.List<MidiNoteEvent>(), PracticeHand.LeftOnly);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void FilterByHand_BoundaryPitch60_GoesToRightHand()
    {
        // C4(60) 是左右手分界：60 归右手
        var notes = new System.Collections.Generic.List<MidiNoteEvent>
        {
            new MidiNoteEvent(59, 0f, 1f, 80, 0, 0),
            new MidiNoteEvent(60, 0f, 1f, 80, 0, 0)
        };
        var left = PianoNoteFilter.FilterByHand(notes, PracticeHand.LeftOnly);
        var right = PianoNoteFilter.FilterByHand(notes, PracticeHand.RightOnly);
        Assert.AreEqual(1, left.Count);
        Assert.AreEqual(59, left[0].Pitch);
        Assert.AreEqual(1, right.Count);
        Assert.AreEqual(60, right[0].Pitch);
    }

    [Test]
    public void MidiNoteEvent_EndTime_IsStartPlusDuration()
    {
        var n = new MidiNoteEvent(60, 1.5f, 0.25f, 90, 0, 0);
        Assert.AreEqual(1.75f, n.EndTime, 0.0001f);
    }

    // ---- 音高范围边界（88 键）----

    [Test]
    public void PianoKeyRange_BoundariesAreCorrect()
    {
        Assert.IsFalse(PianoKeyMap.IsInRange(20));  // 低于 A0
        Assert.IsTrue(PianoKeyMap.IsInRange(21));   // A0
        Assert.IsTrue(PianoKeyMap.IsInRange(108));  // C8
        Assert.IsFalse(PianoKeyMap.IsInRange(109)); // 高于 C8
    }

    [Test]
    public void PianoKeyMap_BlackKeyClassification_IsCorrect()
    {
        Assert.IsFalse(PianoKeyMap.IsBlackKey(60)); // C4 白键
        Assert.IsTrue(PianoKeyMap.IsBlackKey(61));  // C#4 黑键
        Assert.IsTrue(PianoKeyMap.IsBlackKey(66));  // F#4
        Assert.IsFalse(PianoKeyMap.IsBlackKey(64)); // E4 白键
    }

    // ---- 解析器边界 ----

    [Test]
    public void Parse_OnlyEndOfTrack_ReturnsSuccessWithNoNotes()
    {
        // 合法但无音符的曲目：MThd + MTrk(仅 End of Track)
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(new byte[] { 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0 });
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MTrk"));
        bytes.AddRange(new byte[] { 0, 0, 0, 4, 0x00, 0xFF, 0x2F, 0x00 });

        var r = MidiParser.Parse(bytes.ToArray());
        Assert.IsTrue(r.Success, r.Error);
        Assert.AreEqual(0, r.Notes.Count);
    }

    [Test]
    public void Parse_InvalidHeaderLength_ReturnsFail()
    {
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(new byte[] { 0, 0, 0, 2 }); // 声明长度 2（< 6，非法）
        bytes.AddRange(new byte[] { 0, 0, 0, 1, 0x01, 0xE0, 0, 0 });

        var r = MidiParser.Parse(bytes.ToArray());
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Parse_ZeroPpq_ReturnsFail()
    {
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(new byte[] { 0, 0, 0, 6, 0, 0, 0, 1, 0x00, 0x00 }); // division=0

        var r = MidiParser.Parse(bytes.ToArray());
        Assert.IsFalse(r.Success);
    }
}
