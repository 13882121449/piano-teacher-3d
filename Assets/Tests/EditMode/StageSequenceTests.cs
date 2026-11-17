using NUnit.Framework;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Piano;
using UnityEngine;

public class StageSequenceTests
{
    [Test]
    public void BuiltInDemo_UsesTheThreeAuthoredChordSections()
    {
        var profile = StageSequenceProfile.ForSong("demo.mid");
        Assert.AreEqual(StageSection.Opening, StageSequence.Evaluate(1.99f, 7.5f, profile).Section);
        Assert.AreEqual(StageSection.Reef, StageSequence.Evaluate(2f, 7.5f, profile).Section);
        Assert.AreEqual(StageSection.Whale, StageSequence.Evaluate(4f, 7.5f, profile).Section);
    }

    [Test]
    public void CinematicSong_UsesTheAuthoredBarBoundaries()
    {
        var profile = StageSequenceProfile.ForSong("Cinematic_tide.MID");
        Assert.AreEqual(StageSection.Reef, StageSequence.Evaluate(6f, 24f, profile).Section);
        Assert.AreEqual(StageSection.Whale, StageSequence.Evaluate(12f, 24f, profile).Section);
        Assert.AreEqual(StageSection.Ending, StageSequence.Evaluate(18f, 24f, profile).Section);
    }

    [Test]
    public void SeekBack_GivesTheSameCueAsFirstVisit()
    {
        var profile = StageSequenceProfile.ForSong("example.mid");
        var atStart = StageSequence.Evaluate(3f, 12f, profile);
        var atClimax = StageSequence.Evaluate(8f, 12f, profile);
        var atLoopRestart = StageSequence.Evaluate(3f, 12f, profile);
        Assert.AreEqual(atStart, atLoopRestart);
        Assert.AreNotEqual(atStart.Section, atClimax.Section);
        Assert.Greater(atClimax.SunStrength, atStart.SunStrength);
        Assert.Greater(atClimax.WhaleCrossing, atStart.WhaleCrossing);
    }

    [Test]
    public void ShortSongAndInvalidDuration_GiveFiniteCameraProgress()
    {
        var profile = StageSequenceProfile.ForSong("demo.mid");
        Assert.That(StageSequence.Evaluate(3f, 0f, profile).Progress, Is.EqualTo(0f));
        Assert.That(StageSequence.Evaluate(0.2f, 0.1f, profile).Progress, Is.EqualTo(1f));
    }

    [Test]
    public void StageCameraTrack_CoversTheFourCompositions()
    {
        var keyboard = new Vector3(0f, 0.74f, 0.05f);
        var opening = StageCameraTrack.Evaluate(0.1f, keyboard);
        var reef = StageCameraTrack.Evaluate(0.4f, keyboard);
        var hands = StageCameraTrack.Evaluate(0.65f, keyboard);
        var ending = StageCameraTrack.Evaluate(0.9f, keyboard);
        Assert.Greater(Vector3.Distance(opening.Position, reef.Position), 1f);
        Assert.Less(Vector3.Distance(hands.Position, keyboard), 1.2f);
        Assert.Greater(Vector3.Distance(ending.Position, keyboard), 2.5f);
        Assert.Less(hands.FieldOfView, ending.FieldOfView);
    }
}
