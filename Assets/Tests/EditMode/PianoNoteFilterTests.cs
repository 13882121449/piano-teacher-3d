using NUnit.Framework;
using System.Collections.Generic;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

/// <summary>
/// PianoNoteFilter 纯逻辑单元测试（EditMode），覆盖方案 2.2「左右手单独练习」的过滤边界。
/// </summary>
public class PianoNoteFilterTests
{
    private static MidiNoteEvent Note(int pitch) => new MidiNoteEvent(pitch, 0f, 1f, 80, 0, 0);

    [Test]
    public void FilterByHand_Both_ReturnsAll()
    {
        var notes = new List<MidiNoteEvent> { Note(48), Note(60), Note(72) };
        var result = PianoNoteFilter.FilterByHand(notes, PracticeHand.Both);
        Assert.AreEqual(3, result.Count);
    }

    [Test]
    public void FilterByHand_LeftOnly_KeepsOnlyLeftHand()
    {
        // 59 < 60 属左手；60/72 属右手
        var notes = new List<MidiNoteEvent> { Note(48), Note(59), Note(60), Note(72) };
        var result = PianoNoteFilter.FilterByHand(notes, PracticeHand.LeftOnly);
        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.TrueForAll(n => n.Pitch < PianoNoteFilter.SplitPitch));
    }

    [Test]
    public void FilterByHand_RightOnly_KeepsOnlyRightHand()
    {
        var notes = new List<MidiNoteEvent> { Note(48), Note(60), Note(72) };
        var result = PianoNoteFilter.FilterByHand(notes, PracticeHand.RightOnly);
        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.TrueForAll(n => n.Pitch >= PianoNoteFilter.SplitPitch));
    }

    [Test]
    public void FilterByHand_Null_ReturnsEmpty()
    {
        var result = PianoNoteFilter.FilterByHand(null, PracticeHand.Both);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void FilterByHand_NoMatchingHand_ReturnsEmpty()
    {
        var notes = new List<MidiNoteEvent> { Note(72), Note(84) }; // 全是右手音
        var result = PianoNoteFilter.FilterByHand(notes, PracticeHand.LeftOnly);
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void FilterByHand_Both_ReturnsCopyNotSameReference()
    {
        var notes = new List<MidiNoteEvent> { Note(60) };
        var result = PianoNoteFilter.FilterByHand(notes, PracticeHand.Both);
        Assert.AreEqual(1, result.Count);
        result.Clear();
        Assert.AreEqual(1, notes.Count, "Both 应返回副本，不修改原列表");
    }
}
