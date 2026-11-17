using UnityEngine;

namespace PianoTeacher3D.CameraControl
{
    /// <summary>展示用的四组机位。在每组机位内缓移，分镜点直接切换。</summary>
    public static class StageCameraTrack
    {
        private const float ReefCut = 0.25f;
        private const float HandsCut = 0.54f;
        private const float WideCut = 0.73f;

        public static CameraPose Evaluate(float progress, Vector3 keyboard)
        {
            progress = Mathf.Clamp01(progress);
            if (progress < ReefCut)
                return Shot(progress / ReefCut, keyboard,
                    new Vector3(2.21f, 0.66f, 0.94f), new Vector3(1.87f, 0.70f, 1.15f),
                    new Vector3(-0.12f, 0.17f, -0.62f), 40f);
            if (progress < HandsCut)
                return Shot((progress - ReefCut) / (HandsCut - ReefCut), keyboard,
                    new Vector3(2.62f, 0.80f, 3.50f), new Vector3(2.72f, 0.89f, 3.17f),
                    new Vector3(-0.96f, 0.39f, -0.78f), 45f);
            if (progress < WideCut)
                return Shot((progress - HandsCut) / (WideCut - HandsCut), keyboard,
                    new Vector3(0.10f, 0.84f, 0.38f), new Vector3(-0.05f, 0.87f, 0.31f),
                    new Vector3(0.00f, 0.040f, 0.05f), 44f);
            return Shot((progress - WideCut) / (1f - WideCut), keyboard,
                new Vector3(3.32f, 0.91f, 1.49f), new Vector3(3.85f, 1.15f, 1.72f),
                new Vector3(-0.29f, 0.22f, -0.50f), 48f);
        }

        private static CameraPose Shot(float t, Vector3 keyboard, Vector3 from, Vector3 to,
            Vector3 focus, float fieldOfView)
        {
            t = t * t * (3f - 2f * t);
            return CameraPose.LookAt(keyboard + Vector3.Lerp(from, to, t), keyboard + focus, fieldOfView);
        }
    }
}
