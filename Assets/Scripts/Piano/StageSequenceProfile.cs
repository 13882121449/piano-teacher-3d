using System;
using System.IO;

namespace PianoTeacher3D.Piano
{
    /// <summary>按曲长归一化的乐段边界。内置曲目的边界来自谱面的和弦与小节。</summary>
    public readonly struct StageSequenceProfile
    {
        public readonly float ReefStart;
        public readonly float WhaleStart;
        public readonly float EndingStart;

        private StageSequenceProfile(float reefStart, float whaleStart, float endingStart)
        {
            ReefStart = reefStart;
            WhaleStart = whaleStart;
            EndingStart = endingStart;
        }

        public static StageSequenceProfile ForSong(string path)
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, "cinematic_tide.mid", StringComparison.OrdinalIgnoreCase))
                return new StageSequenceProfile(0.25f, 0.50f, 0.75f);
            if (string.Equals(name, "demo.mid", StringComparison.OrdinalIgnoreCase))
                return new StageSequenceProfile(2f / 7.5f, 4f / 7.5f, 6f / 7.5f);
            return new StageSequenceProfile(0.34f, 0.60f, 0.84f);
        }
    }
}
