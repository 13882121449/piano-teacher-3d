using System.Reflection;
using NUnit.Framework;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Tests
{
    public sealed class WindowsMidiInputTests
    {
        [TestCase(0x00643C91u, true, 60, 100)]
        [TestCase(0x00003C91u, false, 60, 0)]
        [TestCase(0x00403C81u, false, 60, 64)]
        public void Decode_ReportsNoteOnAndRelease(uint packed, bool down, int pitch, int velocity)
        {
            var method = typeof(MidiNoteEvent).Assembly.GetType("PianoTeacher3D.MIDI.WindowsMidiInput")
                .GetMethod("TryDecode", BindingFlags.Static | BindingFlags.NonPublic);
            object[] args = { packed, 2.125, null };
            Assert.IsTrue((bool)method.Invoke(null, args));
            var note = args[2];
            var type = note.GetType();
            Assert.AreEqual(down, type.GetField("IsNoteOn").GetValue(note));
            Assert.AreEqual(pitch, type.GetField("Pitch").GetValue(note));
            Assert.AreEqual(velocity, type.GetField("Velocity").GetValue(note));
            Assert.AreEqual(1, type.GetField("Channel").GetValue(note));
            Assert.AreEqual(2.125, type.GetField("TimestampSeconds").GetValue(note));
            args[0] = 0x007F40B1u;
            Assert.IsFalse((bool)method.Invoke(null, args));
        }
    }
}
