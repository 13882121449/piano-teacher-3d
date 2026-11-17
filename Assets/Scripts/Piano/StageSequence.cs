using UnityEngine;

namespace PianoTeacher3D.Piano
{
    public enum StageSection { Opening, Reef, Whale, Ending }

    /// <summary>给当前曲段的照明、镜头与生物提供同一个可复现的采样状态。</summary>
    public readonly struct StageCue
    {
        public readonly StageSection Section;
        public readonly float Seconds;
        public readonly float Progress;
        public readonly float SunStrength;
        public readonly float SunOffset;
        public readonly float FogDensity;
        public readonly float ShoalApproach;
        public readonly float WhaleCrossing;
        public readonly float JellyGlow;
        public readonly Color WaterTint;

        public StageCue(StageSection section, float seconds, float progress,
            float sunStrength, float sunOffset, float fogDensity, float shoalApproach,
            float whaleCrossing, float jellyGlow, Color waterTint)
        {
            Section = section;
            Seconds = seconds;
            Progress = progress;
            SunStrength = sunStrength;
            SunOffset = sunOffset;
            FogDensity = fogDensity;
            ShoalApproach = shoalApproach;
            WhaleCrossing = whaleCrossing;
            JellyGlow = jellyGlow;
            WaterTint = waterTint;
        }
    }

    public static class StageSequence
    {
        public static StageCue Evaluate(float seconds, float duration, StageSequenceProfile profile)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f) duration = 0f;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
            seconds = Mathf.Clamp(seconds, 0f, duration);
            float position = duration > 0f ? seconds / duration : 0f;
            float reef = Ease(position, profile.ReefStart * 0.72f, profile.WhaleStart);
            float swell = Ease(position, profile.WhaleStart - 0.09f,
                Mathf.Lerp(profile.WhaleStart, profile.EndingStart, 0.55f));
            float closing = Ease(position, profile.EndingStart, 1f);
            float center = Mathf.Lerp(profile.WhaleStart, profile.EndingStart, 0.52f);
            float whale = Ease(position, profile.ReefStart * 0.46f, Mathf.Lerp(center, 1f, 0.87f));
            var section = position < profile.ReefStart ? StageSection.Opening
                : position < profile.WhaleStart ? StageSection.Reef
                : position < profile.EndingStart ? StageSection.Whale : StageSection.Ending;

            Color waterTint = Color.Lerp(new Color(1.18f, 1.27f, 1.15f), new Color(0.65f, 0.89f, 1.30f), reef);
            waterTint = Color.Lerp(waterTint, new Color(0.48f, 0.66f, 0.95f), closing);
            return new StageCue(section, seconds, position,
                0.86f - reef * 0.19f + swell * 0.94f - closing * 1.16f,
                Mathf.Lerp(-0.48f, 0.39f, swell) - closing * 0.31f,
                0.72f + reef * 0.34f + closing * 0.38f,
                Mathf.Max(0f, reef * (1f - closing * 0.78f)),
                whale, 0.21f + swell * 0.14f + closing * 0.17f, waterTint);
        }

        private static float Ease(float position, float from, float to)
        {
            float t = Mathf.InverseLerp(from, to, position);
            return t * t * (3f - 2f * t);
        }
    }
}
