using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 响应音符事件并控制琴键按下、回弹动画（方案 4.2）。
    /// 订阅 <see cref="MIDI.MidiEventScheduler"/> 的 OnNoteOn/OnNoteOff，做平滑角度插值。
    /// </summary>
    public class PianoKeyAnimator : MonoBehaviour
    {
        [SerializeField] private float _pressAngle = 3f;
        [SerializeField] private float _slerpSpeed = 20f;

        private PianoKeyMap _keyMap;
        private readonly Dictionary<int, float> _targetAngle = new();

        public void Initialize(PianoKeyMap keyMap)
        {
            _keyMap = keyMap;
        }

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (PianoKeyMap.IsInRange(e.Pitch))
                _targetAngle[e.Pitch] = _pressAngle;
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (PianoKeyMap.IsInRange(e.Pitch))
                _targetAngle[e.Pitch] = 0f;
        }

        /// <summary>停止/重载/Seek 时释放所有琴键，并立即复位角度。</summary>
        public void ReleaseAll()
        {
            _targetAngle.Clear();
            if (_keyMap == null) return;
            foreach (var kv in _keyMap.KeyRoots)
            {
                if (kv.Value != null) kv.Value.localRotation = Quaternion.identity;
            }
        }

        private void Update()
        {
            if (_keyMap == null) return;

            foreach (var kv in _targetAngle)
            {
                if (!_keyMap.TryGetKey(kv.Key, out var key) || key == null) continue;
                var target = Quaternion.Euler(-kv.Value, 0f, 0f);
                key.localRotation = Quaternion.Slerp(key.localRotation, target, Time.deltaTime * _slerpSpeed);
            }
        }
    }
}
