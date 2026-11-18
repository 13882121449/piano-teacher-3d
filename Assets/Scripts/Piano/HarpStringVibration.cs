using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 竖琴钢琴的琴弦振动：琴键按下时对应的弦被「拨响」，振幅随时间衰减，松键后由制音器迅速止振。
    /// <para>
    /// 输入只读 <see cref="PianoKeyAnimator.GetKeyAngle"/>（键程角度），不订阅音符事件，
    /// 所以示范演奏、MIDI 键盘、练习模式触发的按键都会让弦振动。
    /// 输出是全局数组 <c>_PT3D_HarpAmp[88]</c>，由 <c>PianoTeacher3D/HarpString</c> 着色器做横向位移和发光。
    /// </para>
    /// </summary>
    public sealed class HarpStringVibration : MonoBehaviour
    {
        private static readonly int AmpId = Shader.PropertyToID("_PT3D_HarpAmp");

        private readonly float[] _amp = new float[PianoKeyMap.KeyCount];
        private readonly float[] _lastAngle = new float[PianoKeyMap.KeyCount];
        private readonly bool[] _held = new bool[PianoKeyMap.KeyCount];
        private PianoKeyAnimator _animator;
        private bool _idleUploaded;
        private Vector3[] _bottoms;
        private Vector3[] _tops;

        /// <summary>某根弦被拨响：弦下端、弦上端（世界坐标）与力度 0–1。海底氛围据此沿弦放出小气泡。</summary>
        public static event System.Action<Vector3, Vector3, float> StringStruck;

        /// <summary>由琴壳生成器写入每根弦的本地端点（与本节点同一坐标系）。</summary>
        internal void SetStringGeometry(Vector3[] bottoms, Vector3[] tops)
        {
            _bottoms = bottoms;
            _tops = tops;
        }

        /// <summary>第 <paramref name="index"/> 根弦（0 = A0）当前振幅 0–1（诊断 / 测试用）。</summary>
        public float GetAmplitude(int index) => index >= 0 && index < _amp.Length ? _amp[index] : 0f;

        private void OnEnable()
        {
            Shader.SetGlobalFloatArray(AmpId, _amp);
        }

        private void OnDisable()
        {
            System.Array.Clear(_amp, 0, _amp.Length);
            Shader.SetGlobalFloatArray(AmpId, _amp);
        }

        private void Update()
        {
            if (_animator == null)
            {
                _animator = FindFirstObjectByType<PianoKeyAnimator>();
                if (_animator == null) return;
            }

            float press = Mathf.Max(_animator.PressAngle, 0.01f);
            float dt = Time.deltaTime;
            bool any = false;
            for (int i = 0; i < _amp.Length; i++)
            {
                int pitch = PianoKeyMap.MinPitch + i;
                float angle = _animator.GetKeyAngle(pitch);
                float depth = angle / press;
                float last = _lastAngle[i] / press;
                if (!_held[i] && depth > 0.55f && last <= 0.55f)
                {
                    _held[i] = true;
                    // 键下得越快拨得越响：用本帧键程增量估算力度
                    float speed = Mathf.Clamp01((depth - last) / Mathf.Max(dt, 1e-3f) * 0.022f);
                    _amp[i] = Mathf.Max(_amp[i], Mathf.Lerp(0.55f, 1f, speed));
                    if (_bottoms != null && i < _bottoms.Length)
                        StringStruck?.Invoke(transform.TransformPoint(_bottoms[i]), transform.TransformPoint(_tops[i]), speed);
                }
                else if (_held[i] && depth < 0.3f)
                {
                    _held[i] = false;
                }
                _lastAngle[i] = angle;

                if (_amp[i] > 0f)
                {
                    float register = i / (float)(_amp.Length - 1);
                    // 按住时自然衰减（低音长、高音短），松开后制音器在约 0.15 s 内止振
                    float tau = _held[i] ? Mathf.Lerp(2.2f, 0.6f, register) : 0.15f;
                    _amp[i] *= Mathf.Exp(-dt / tau);
                    if (_amp[i] < 0.004f) _amp[i] = 0f;
                    any = true;
                }
            }

            if (any || !_idleUploaded)
            {
                Shader.SetGlobalFloatArray(AmpId, _amp);
                _idleUploaded = !any;
            }
        }
    }
}
