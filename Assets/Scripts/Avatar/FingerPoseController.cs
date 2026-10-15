using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 控制基础手指姿态（方案 4.2）。首版为简化姿态：
    /// 按下键按 pitch 排序后映射到手指（指1..指5 对应低..高），按下弯曲、抬起伸直。
    /// 不承诺专业指法准确性（方案 2.3）。
    /// </summary>
    public class FingerPoseController : MonoBehaviour
    {
        public float PressAngle = 32f; // 手指按下弯曲角度（绕 X 正角 = 指尖下压向琴键）
        public float RestAngle = 18f;  // 未按下时的自然弯曲（避免僵直的手型）
        public float SlerpSpeed = 15f;

        private readonly HashSet<int> _pressed = new();
        private IHandRig _leftHand;
        private IHandRig _rightHand;

        public void Initialize(IHandRig leftHand, IHandRig rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
        }

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (Piano.PianoKeyMap.IsInRange(e.Pitch)) _pressed.Add(e.Pitch);
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e) => _pressed.Remove(e.Pitch);

        public void ReleaseAll() => _pressed.Clear();

        private void Update()
        {
            UpdateFingers(_leftHand, leftHand: true);
            UpdateFingers(_rightHand, leftHand: false);
        }

        private void UpdateFingers(IHandRig hand, bool leftHand)
        {
            if (hand == null) return;

            var pitches = new List<int>();
            foreach (var p in _pressed)
            {
                bool isLeft = p < MIDI.PianoNoteFilter.SplitPitch;
                if (isLeft == leftHand) pitches.Add(p);
            }
            pitches.Sort();

            int pressedCount = Mathf.Min(pitches.Count, hand.Fingers.Count);
            for (int f = 0; f < hand.Fingers.Count; f++)
            {
                var finger = hand.Fingers[f];
                if (finger == null) continue;
                float targetAngle = f < pressedCount ? PressAngle : RestAngle;
                var target = Quaternion.Euler(targetAngle, 0f, 0f);
                finger.localRotation = Quaternion.Slerp(finger.localRotation, target, Time.deltaTime * SlerpSpeed);
            }
        }
    }
}
