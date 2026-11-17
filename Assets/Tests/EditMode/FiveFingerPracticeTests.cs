using NUnit.Framework;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Tests.EditMode
{
    public sealed class FiveFingerPracticeTests
    {
        [Test]
        public void Notes_KeepTheWrittenPitchesFingersAndBeats()
        {
            int[] pitches = { 60, 62, 64, 65, 67, 64, 62, 60 };
            int[] fingers = { 1, 2, 3, 4, 5, 3, 2, 1 };
            Assert.That(FiveFingerPractice.Notes.Count, Is.EqualTo(8));
            for (int i = 0; i < pitches.Length; i++)
            {
                var note = FiveFingerPractice.Notes[i];
                Assert.That(note.Pitch, Is.EqualTo(pitches[i]));
                Assert.That(note.Finger, Is.EqualTo(fingers[i]));
                Assert.That(note.Measure, Is.EqualTo(i / 4 + 1));
                Assert.That(note.Beat, Is.EqualTo(i % 4 + 1));
            }
        }

        [Test]
        public void WaitForCorrect_WrongKeyDoesNotAdvanceAndCorrectKeyDoes()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.WaitForCorrect, 3);
            practice.Advance(65);

            var wrong = practice.PlayNote(61, 65);
            Assert.That(wrong.Kind, Is.EqualTo(PracticeFeedbackKind.WrongPitch));
            Assert.That(wrong.ExpectedPitch, Is.EqualTo(60));
            Assert.That(wrong.ActualPitch, Is.EqualTo(61));
            Assert.That(practice.CurrentNoteIndex, Is.Zero);
            Assert.That(practice.PlayNote(60, 66).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
            Assert.That(practice.CurrentNoteIndex, Is.EqualTo(1));
        }

        [Test]
        public void KeepTime_CorrectsWrongPitchAndReportsEarlyAndLate()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.KeepTime, 10);
            Assert.That(practice.FirstNoteTime, Is.EqualTo(14));
            Assert.That(practice.PlayNote(60, 12).Kind, Is.EqualTo(PracticeFeedbackKind.None));
            Assert.That(practice.PlayNote(61, 13.8).Kind, Is.EqualTo(PracticeFeedbackKind.WrongPitch));
            Assert.That(practice.CurrentNoteIndex, Is.Zero);

            var early = practice.PlayNote(60, 13.8);
            var late = practice.PlayNote(62, 15.21);
            Assert.That(early.Kind, Is.EqualTo(PracticeFeedbackKind.Early));
            Assert.That(early.OffsetSeconds, Is.EqualTo(-0.2).Within(0.0001));
            Assert.That(late.Kind, Is.EqualTo(PracticeFeedbackKind.Late));
            Assert.That(late.OffsetSeconds, Is.EqualTo(0.21).Within(0.0001));
            Assert.That(practice.PlayNote(64, 16.1).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
        }

        [Test]
        public void KeepTime_MarksSkippedBeatsOnClockJumpAndFinishesOnlyOnce()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.KeepTime, 10);

            practice.Advance(15.8);
            Assert.That(practice.Results.Count, Is.EqualTo(2));
            Assert.That(practice.Results[0].Kind, Is.EqualTo(PracticeFeedbackKind.Missed));
            Assert.That(practice.Results[1].NoteIndex, Is.EqualTo(1));
            Assert.That(practice.CurrentNoteIndex, Is.EqualTo(2));

            practice.Advance(30);
            practice.Advance(40);
            Assert.That(practice.IsComplete, Is.True);
            Assert.That(practice.CurrentNoteIndex, Is.EqualTo(-1));
            Assert.That(practice.Results.Count, Is.EqualTo(8));
            Assert.That(practice.PlayNote(60, 40).Kind, Is.EqualTo(PracticeFeedbackKind.None));
        }

        [Test]
        public void Independent_UsesSecondMeasureAndNeverPrompts()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.Independent, 100, bpm: 120, measure: 2);
            Assert.That(practice.ShowsPrompt, Is.False);
            Assert.That(practice.SelectedMeasure, Is.EqualTo(2));
            Assert.That(practice.StartNoteIndex, Is.EqualTo(4));
            Assert.That(practice.EndNoteIndexExclusive, Is.EqualTo(8));
            Assert.That(practice.FirstNoteTime, Is.EqualTo(102));
            Assert.That(practice.BeatSeconds, Is.EqualTo(0.5));

            Assert.That(practice.PlayNote(67, 102).NoteIndex, Is.EqualTo(4));
            Assert.That(practice.PlayNote(64, 102.5).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
            Assert.That(practice.PlayNote(62, 103).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
            Assert.That(practice.PlayNote(60, 103.5).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
            Assert.That(practice.IsComplete, Is.True);
            Assert.That(practice.ShowsPrompt, Is.False);
            Assert.That(practice.Results.Count, Is.EqualTo(4));
        }

        [Test]
        public void KeepTime_LogsAnExtraKeyWithoutAssigningItToNextBeat()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.KeepTime, 10);
            practice.PlayNote(60, 14);
            var repeated = practice.PlayNote(60, 14.1);

            Assert.That(repeated.Kind, Is.EqualTo(PracticeFeedbackKind.Extra));
            Assert.That(repeated.NoteIndex, Is.EqualTo(0));
            Assert.That(repeated.ExpectedPitch, Is.EqualTo(-1));
            Assert.That(practice.CurrentNoteIndex, Is.EqualTo(1));
            Assert.That(practice.PlayNote(62, 15).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
        }

        [Test]
        public void RestartingForOneMeasure_ClearsPreviousAttempt()
        {
            var practice = new FiveFingerPractice();
            practice.Start(PracticeMode.WaitForCorrect, 0);
            practice.PlayNote(61, 1);

            practice.Start(PracticeMode.WaitForCorrect, 5, measure: 2);
            Assert.That(practice.CurrentNoteIndex, Is.EqualTo(4));
            Assert.That(practice.Results, Is.Empty);
            Assert.That(practice.PlayNote(67, 5).Kind, Is.EqualTo(PracticeFeedbackKind.Correct));
        }
    }
}
