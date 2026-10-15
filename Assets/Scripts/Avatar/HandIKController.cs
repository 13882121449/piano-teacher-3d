using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 把音符目标转换为手腕位置与手部目标（方案 4.2 / 3.3 数据流第 6 步）。
    /// 左右手以 C4（pitch=60）分界；维护「当前按下键」集合，每帧将手腕平滑移动到目标琴键上方，
    /// 并拉伸手臂（肩→腕）。首版为基础目标移动，不承诺专业指法。
    /// </summary>
    public class HandIKController : MonoBehaviour
    {
        [Header("手部目标几何")]
        public float HandHeight = 0.5f;   // 手在键面上方高度
        public float HandDepth = 2.5f;    // 相对键后端的深度偏移（白键中段）
        public float MoveSpeed = 12f;     // 手腕移动平滑速度

        private AvatarManager _avatar;
        private Piano.PianoKeyMap _keyMap;
        private readonly HashSet<int> _pressed = new();

        public void Initialize(AvatarManager avatar, Piano.PianoKeyMap keyMap)
        {
            _avatar = avatar;
            _keyMap = keyMap;
        }

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (Piano.PianoKeyMap.IsInRange(e.Pitch)) _pressed.Add(e.Pitch);
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e) => _pressed.Remove(e.Pitch);

        public void ReleaseAll() => _pressed.Clear();

        private void Update()
        {
            if (_avatar == null || _keyMap == null) return;
            UpdateHand(leftHand: true);
            UpdateHand(leftHand: false);
        }

        private void UpdateHand(bool leftHand)
        {
            int? targetPitch = FindHandTarget(leftHand);
            Vector3 targetPos = targetPitch.HasValue && _keyMap.TryGetKey(targetPitch.Value, out var key)
                ? KeyToHandPos(key)
                : DefaultHandPos(leftHand ? -1f : 1f);

            var hand = leftHand ? _avatar.LeftHand : _avatar.RightHand;
            var shoulder = leftHand ? _avatar.LeftShoulder : _avatar.RightShoulder;
            var arm = leftHand ? _avatar.LeftArm : _avatar.RightArm;

            if (hand == null || shoulder == null || arm == null) return;

            hand.Wrist.position = Vector3.Lerp(hand.Wrist.position, targetPos, Time.deltaTime * MoveSpeed);
            StretchArm(shoulder, arm, hand.Wrist);
        }

        private int? FindHandTarget(bool leftHand)
        {
            int? best = null;
            foreach (var pitch in _pressed)
            {
                bool isLeft = pitch < MIDI.PianoNoteFilter.SplitPitch;
                if (isLeft != leftHand) continue;
                if (!best.HasValue) { best = pitch; continue; }
                if (leftHand && pitch < best.Value) best = pitch;    // 左手取最低音
                if (!leftHand && pitch > best.Value) best = pitch;   // 右手取最高音
            }
            return best;
        }

        private Vector3 KeyToHandPos(Transform key)
        {
            var p = key.position;
            return new Vector3(p.x, HandHeight, p.z + HandDepth);
        }

        private Vector3 DefaultHandPos(float side) => new Vector3(side * 8f, HandHeight, 1.5f);

        private static void StretchArm(Transform shoulder, Transform arm, Transform wrist)
        {
            var delta = wrist.position - shoulder.position;
            if (delta.sqrMagnitude < 1e-6f) return;
            arm.position = shoulder.position + delta * 0.5f;
            arm.up = delta.normalized;
            var s = arm.localScale;
            arm.localScale = new Vector3(s.x, delta.magnitude * 0.5f, s.z);
        }
    }
}
