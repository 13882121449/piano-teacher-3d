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
    [DefaultExecutionOrder(2)]
    public class PianoKeyAnimator : MonoBehaviour
    {
        [SerializeField] private float _pressAngle = 3f;

        /// <summary>判定"回弹到位"的角度阈值（度），低于它即脱离活动列表。</summary>
        private const float RestAngleThreshold = 0.05f;

        // 2026-11-18 P5 音画同步：琴键改为按真实键程计时运动，不再用指数逼近（原 Slerp ×20/s）。
        // 原写法在 60 fps 下约 100 ms 才压到 90%，而声音在 note-on 当帧就响，画面里「先响后按」。
        // 真实钢琴是键到底、琴槌击弦时出声：键程 16–32 ms（越响越快），松键回弹约 70 ms。
        // 这样键到底的时刻与声音起点（再加上约 20 ms 的音频缓冲）基本重合。
        public const float LoudPressSeconds = 0.016f;
        public const float SoftPressSeconds = 0.032f;
        public const float ReleaseSeconds = 0.070f;

        private PianoKeyMap _keyMap;

        // ---- 预分配缓存（Initialize 中一次性创建）----
        private float[] _targetAngle;   // 目标角度（度）：> 0 表示按下
        private float[] _angle;         // 当前角度（度）
        private float[] _pressSpeed;    // 本次按下的角速度（度/秒，按力度决定）
        private bool[] _tracked;        // 是否已在 _activeIndex 列表中
        private int[] _activeIndex;     // 需要在本帧插值的键（pitch - MinPitch）
        private int _activeCount;
        private int _pressedCount;
        private readonly MIDI.ActiveNoteHands _pressedHands = new();

        /// <summary>当前处于按下状态（目标角度 &gt; 0）的琴键数量。</summary>
        public int PressedKeyCount => _pressedCount;

        /// <summary>琴键完全按下时的局部旋转，供腕位预先计算稳定的触键位置。</summary>
        internal Quaternion DepressedRotation => Quaternion.Euler(_pressAngle, 0f, 0f);

        public void Initialize(PianoKeyMap keyMap)
        {
            _keyMap = keyMap;

            int size = PianoKeyMap.MaxPitch - PianoKeyMap.MinPitch + 1;
            if (_targetAngle == null || _targetAngle.Length != size)
            {
                _targetAngle = new float[size];
                _angle = new float[size];
                _pressSpeed = new float[size];
                _tracked = new bool[size];
                _activeIndex = new int[size];
            }
            else
            {
                System.Array.Clear(_targetAngle, 0, size);
                System.Array.Clear(_angle, 0, size);
                System.Array.Clear(_tracked, 0, size);
            }

            _activeCount = 0;
            _pressedCount = 0;
            _pressedHands.Clear();
        }

        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (_targetAngle == null || !PianoKeyMap.IsInRange(e.Pitch)) return;
            _pressedHands.Press(e);

            int i = e.Pitch - PianoKeyMap.MinPitch;
            if (!_tracked[i])
            {
                _tracked[i] = true;
                _activeIndex[_activeCount++] = i;
            }

            if (_targetAngle[i] <= 0f) _pressedCount++;
            _targetAngle[i] = _pressAngle;
            _pressSpeed[i] = _pressAngle / PressSeconds(e.Velocity);
        }

        /// <summary>按力度换算键程时间：力度 127 约 16 ms，力度 1 约 32 ms。</summary>
        public static float PressSeconds(int velocity)
            => Mathf.Lerp(SoftPressSeconds, LoudPressSeconds, Mathf.Clamp01(velocity / 127f));

        public void OnNoteOff(MIDI.MidiNoteEvent e)
        {
            if (_targetAngle == null || !PianoKeyMap.IsInRange(e.Pitch)) return;
            if (!_pressedHands.Release(e)) return;

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
                System.Array.Clear(_angle, 0, _angle.Length);
                System.Array.Clear(_tracked, 0, _tracked.Length);
            }
            _activeCount = 0;
            _pressedCount = 0;
            _pressedHands.Clear();

            if (_keyMap == null) return;
            foreach (var kv in _keyMap.KeyRoots)
            {
                if (kv.Value != null) kv.Value.localRotation = Quaternion.identity;
            }
        }

        private void Update()
        {
            if (_keyMap == null || _targetAngle == null || _activeCount == 0) return;

            float dt = Time.deltaTime;
            float releaseSpeed = _pressAngle / ReleaseSeconds;

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

                // 绕 X 正角：键前端（+Z）向下按下。按下用本次力度的键程速度，回弹用固定速度。
                float target = _targetAngle[i];
                float speed = target > _angle[i] ? _pressSpeed[i] : releaseSpeed;
                if (speed <= 0f) speed = releaseSpeed;
                _angle[i] = Mathf.MoveTowards(_angle[i], target, speed * dt);
                key.localRotation = Quaternion.Euler(_angle[i], 0f, 0f);

                // 目标为回弹且已到位：脱离活动列表，避免每帧空插值
                if (target <= 0f && _angle[i] <= RestAngleThreshold)
                {
                    key.localRotation = Quaternion.identity;
                    _angle[i] = 0f;
                    _tracked[i] = false;
                    RemoveAt(k);
                    k--;
                }
            }
        }

        /// <summary>某个琴键当前的下沉角度（度，0 = 静止，满键程 = <see cref="PressAngle"/>）。音画同步测试用。</summary>
        public float GetKeyAngle(int pitch)
        {
            if (_angle == null || !PianoKeyMap.IsInRange(pitch)) return 0f;
            return _angle[pitch - PianoKeyMap.MinPitch];
        }

        /// <summary>满键程角度（度）。</summary>
        public float PressAngle => _pressAngle;

        /// <summary>把索引 k 的元素用列表尾部元素覆盖（顺序无关，O(1) 且无分配）。</summary>
        private void RemoveAt(int k)
        {
            _activeCount--;
            _activeIndex[k] = _activeIndex[_activeCount];
        }
    }
}
