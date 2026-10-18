using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 响应音符事件并控制琴键按下、回弹动画（方案 4.2 / 审查 1.3A 第 2 项）。
    /// 订阅 <see cref="MIDI.MidiEventScheduler"/> 的 OnNoteOn/OnNoteOff，做平滑角度插值。
    /// <para>
    /// <b>运动语义</b>：键根 pivot 位于键后端，按下为绕键根<b>局部 X 轴</b>的正角（<see cref="Quaternion.Euler"/>），
    /// 键前端（+Z）向下。键身改为倒角网格后该轴向与旋转中心均未改变。
    /// </para>
    /// <para>
    /// <b>性能（方案 7.2）</b>：目标角度、跟踪标记与活动键列表全部<b>预分配</b>（按 <c>pitch - MinPitch</c> 索引），
    /// Update 路径不再枚举 <c>Dictionary</c>，也不产生任何托管分配。
    /// </para>
    /// </summary>
    public class PianoKeyAnimator : MonoBehaviour
    {
        [SerializeField] private float _pressAngle = 3f;
        [SerializeField] private float _slerpSpeed = 20f;

        /// <summary>判定"回弹到位"的角度阈值（度），低于它即脱离活动列表。</summary>
        private const float RestAngleThreshold = 0.05f;

        private PianoKeyMap _keyMap;

        // ---- 预分配缓存（Initialize 中一次性创建）----
        private float[] _targetAngle;   // 目标角度（度）：> 0 表示按下
        private bool[] _tracked;        // 是否已在 _activeIndex 列表中
        private int[] _activeIndex;     // 需要在本帧插值的键（pitch - MinPitch）
        private int _activeCount;
        private int _pressedCount;

        /// <summary>当前处于按下状态（目标角度 &gt; 0）的琴键数量。</summary>
        public int PressedKeyCount => _pressedCount;

        public void Initialize(PianoKeyMap keyMap)
        {
            _keyMap = keyMap;

            int size = PianoKeyMap.MaxPitch - PianoKeyMap.MinPitch + 1;
            if (_targetAngle == null || _targetAngle.Length != size)
            {
                _targetAngle = new float[size];
                _tracked = new bool[size];
                _activeIndex = new int[size];
            }
            else
            {
                System.Array.Clear(_targetAngle, 0, size);
                System.Array.Clear(_tracked, 0, size);
            }

            _activeCount = 0;
            _pressedCount = 0;
        }

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (_targetAngle == null || !PianoKeyMap.IsInRange(e.Pitch)) return;

            int i = e.Pitch - PianoKeyMap.MinPitch;
            if (!_tracked[i])
            {
                _tracked[i] = true;
                _activeIndex[_activeCount++] = i;
            }

            if (_targetAngle[i] <= 0f) _pressedCount++;
            _targetAngle[i] = _pressAngle;
        }

        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (_targetAngle == null || !PianoKeyMap.IsInRange(e.Pitch)) return;

            int i = e.Pitch - PianoKeyMap.MinPitch;
            if (_targetAngle[i] > 0f)
            {
                _pressedCount--;
                if (_pressedCount < 0) _pressedCount = 0;
            }
            _targetAngle[i] = 0f;

            // 即使从未按下，也要纳入列表让它（保持）回到 identity，行为与原实现一致
            if (!_tracked[i])
            {
                _tracked[i] = true;
                _activeIndex[_activeCount++] = i;
            }
        }

        /// <summary>停止/重载/Seek 时释放所有琴键，并立即复位角度。</summary>
        public void ReleaseAll()
        {
            if (_targetAngle != null)
            {
                System.Array.Clear(_targetAngle, 0, _targetAngle.Length);
                System.Array.Clear(_tracked, 0, _tracked.Length);
            }
            _activeCount = 0;
            _pressedCount = 0;

            if (_keyMap == null) return;
            foreach (var kv in _keyMap.KeyRoots)
            {
                if (kv.Value != null) kv.Value.localRotation = Quaternion.identity;
            }
        }

        private void Update()
        {
            if (_keyMap == null || _targetAngle == null || _activeCount == 0) return;

            float step = Time.deltaTime * _slerpSpeed;

            for (int k = 0; k < _activeCount; k++)
            {
                int i = _activeIndex[k];

                if (!_keyMap.TryGetKey(i + PianoKeyMap.MinPitch, out var key) || key == null)
                {
                    // 键对象已失效：直接移出列表（无分配）
                    _tracked[i] = false;
                    RemoveAt(k);
                    k--;
                    continue;
                }

                var target = Quaternion.Euler(_targetAngle[i], 0f, 0f); // 绕 X 正角：键前端（+Z）向下按下
                var rot = Quaternion.Slerp(key.localRotation, target, step);
                key.localRotation = rot;

                // 目标为回弹且已到位：脱离活动列表，避免每帧空插值
                if (_targetAngle[i] <= 0f && Quaternion.Angle(rot, Quaternion.identity) <= RestAngleThreshold)
                {
                    key.localRotation = Quaternion.identity;
                    _tracked[i] = false;
                    RemoveAt(k);
                    k--;
                }
            }
        }

        /// <summary>把索引 k 的元素用列表尾部元素覆盖（顺序无关，O(1) 且无分配）。</summary>
        private void RemoveAt(int k)
        {
            _activeCount--;
            _activeIndex[k] = _activeIndex[_activeCount];
        }
    }
}
