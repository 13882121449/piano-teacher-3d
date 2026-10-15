using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 控制基础手指姿态（方案 4.2）。
    /// <para>
    /// 阶段五改进（方案 7 阶段五「改善手指姿态」）：由原先「按音高排序取前 N 根手指」
    /// 改为 <b>按音高比例映射手指索引</b>——跨度大时手指自然展开、跨度小时相邻收拢，
    /// 并按音高比例给出左右张开角（和弦手型）；单音默认落在中间手指，更接近自然手型。
    /// 冲突时顺延到最近空闲手指；按键数超过手指数时复用理想手指，不会越界。
    /// </para>
    /// 不承诺专业指法准确性（方案 2.3）。
    /// </summary>
    public class FingerPoseController : MonoBehaviour
    {
        public float PressAngle = 32f;  // 手指按下弯曲角度（绕 X 正角 = 指尖下压向琴键）
        public float RestAngle = 18f;   // 未按下时的自然弯曲（避免僵直的手型）
        public float SlerpSpeed = 15f;
        public float SpreadAngle = 4f;  // 和弦手型左右张开角（绕 Y），按音高比例分配

        private const int MaxFingers = 8;

        private readonly HashSet<int> _pressed = new();
        private readonly List<int> _scratch = new();
        private readonly bool[] _used = new bool[MaxFingers];

        // 左右手各持一份分配结果：必须隔离，否则后处理的手会清空前一只手的数据
        private readonly int[] _leftFingerPitch = new int[MaxFingers];
        private readonly int[] _rightFingerPitch = new int[MaxFingers];

        private IHandRig _leftHand;
        private IHandRig _rightHand;

        public void Initialize(IHandRig leftHand, IHandRig rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
        }

        public void OnNoteOn(MidiNoteEvent e)
        {
            if (Piano.PianoKeyMap.IsInRange(e.Pitch)) _pressed.Add(e.Pitch);
        }

        public void OnNoteOff(MidiNoteEvent e) => _pressed.Remove(e.Pitch);

        public void ReleaseAll()
        {
            _pressed.Clear();
            for (int i = 0; i < MaxFingers; i++)
            {
                _leftFingerPitch[i] = -1;
                _rightFingerPitch[i] = -1;
            }
        }

        /// <summary>指定手指当前分配到的音高（-1 = 未按下），供测试与调试使用。</summary>
        public int GetFingerPitch(bool leftHand, int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= MaxFingers) return -1;
            return leftHand ? _leftFingerPitch[fingerIndex] : _rightFingerPitch[fingerIndex];
        }

        private void Update()
        {
            UpdateFingers(_leftHand, leftHand: true);
            UpdateFingers(_rightHand, leftHand: false);
        }

        private void UpdateFingers(IHandRig hand, bool leftHand)
        {
            if (hand == null) return;

            int fingerCount = Mathf.Min(hand.Fingers.Count, MaxFingers);
            if (fingerCount == 0) return;

            // 该手的分配结果数组（左右手隔离）
            var assign = leftHand ? _leftFingerPitch : _rightFingerPitch;

            CollectHandPitches(leftHand);
            AssignFingers(_scratch, fingerCount, assign);

            int minPitch = _scratch.Count > 0 ? _scratch[0] : 0;
            int maxPitch = _scratch.Count > 0 ? _scratch[_scratch.Count - 1] : 0;
            int span = maxPitch - minPitch;

            for (int f = 0; f < hand.Fingers.Count && f < MaxFingers; f++)
            {
                var finger = hand.Fingers[f];
                if (finger == null) continue;

                int pitch = assign[f];
                float pressAngle = RestAngle;
                float spreadAngle = 0f;

                if (pitch >= 0)
                {
                    pressAngle = PressAngle;
                    // 和弦手型：按该音高在整个音高区间中的比例决定张开方向与幅度
                    float ratio = span > 0 ? (pitch - minPitch) / (float)span : 0.5f;
                    spreadAngle = (ratio - 0.5f) * 2f * SpreadAngle;
                }

                var target = Quaternion.Euler(pressAngle, spreadAngle, 0f);
                finger.localRotation = Quaternion.Slerp(finger.localRotation, target, Time.deltaTime * SlerpSpeed);
            }
        }

        private void CollectHandPitches(bool leftHand)
        {
            _scratch.Clear();
            foreach (var pitch in _pressed)
            {
                if (PianoNoteFilter.IsLeftHand(pitch) == leftHand) _scratch.Add(pitch);
            }
            _scratch.Sort();
        }

        /// <summary>
        /// 按音高比例把按键分配到手指：跨度大 → 手指展开；单音 → 中间手指。
        /// 同索引冲突时顺延到最近的空闲手指；手指用尽后复用理想索引（不越界）。
        /// 结果写入 <paramref name="assign"/>（由调用方按手提供，左右手互不干扰）。
        /// </summary>
        private void AssignFingers(List<int> pitches, int fingerCount, int[] assign)
        {
            for (int i = 0; i < MaxFingers; i++)
            {
                assign[i] = -1;
                _used[i] = false;
            }
            if (pitches.Count == 0) return;

            int minPitch = pitches[0];
            int span = pitches[pitches.Count - 1] - minPitch;

            for (int i = 0; i < pitches.Count; i++)
            {
                float ratio = span > 0 ? (pitches[i] - minPitch) / (float)span : 0.5f;
                int ideal = Mathf.RoundToInt(ratio * (fingerCount - 1));
                int index = FindNearestFree(fingerCount, ideal);

                _used[index] = true;
                assign[index] = pitches[i];
            }
        }

        private int FindNearestFree(int fingerCount, int ideal)
        {
            ideal = Mathf.Clamp(ideal, 0, fingerCount - 1);
            if (!_used[ideal]) return ideal;

            for (int d = 1; d < fingerCount; d++)
            {
                int lower = ideal - d;
                int upper = ideal + d;
                if (lower >= 0 && !_used[lower]) return lower;
                if (upper < fingerCount && !_used[upper]) return upper;
            }
            return ideal; // 按键数 ≥ 手指数：复用理想手指
        }
    }
}
