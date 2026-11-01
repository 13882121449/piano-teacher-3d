using System.Collections.Generic;
using PianoTeacher3D.MIDI;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 右脚延音踏板动作。
    /// <para>
    /// <b>为什么单独一个组件</b>：脚部与手部走的是两条完全不同的链路——手是「音符 → IK 目标」，
    /// 脚是「踏板事件 → 脚尖角度」；把它塞进 <see cref="SittingPoseApplier"/> 会让建姿代码沾上逐帧状态。
    /// </para>
    /// <para>
    /// <b>骨骼约定</b>：使用 Humanoid 的 <c>RightFoot</c> 与 <c>RightToes</c>，只改局部旋转，
    /// 位置由坐姿与踏板本身决定，避免脚离地。绑定失败（程序化角色没有 Humanoid）时静默不动。
    /// </para>
    /// </summary>
    public sealed class PedalFootAnimator : MonoBehaviour
    {
        /// <summary>踩到底时脚尖抬起的角度（度）。钢琴延音踏板行程约 20–30 mm，映射到 10–15° 观感自然。</summary>
        [SerializeField] private float _pressAngleDegrees = 13f;

        /// <summary>角度跟随时间常数（秒）。太硬会像机器，太软会与音符脱节。</summary>
        [SerializeField] private float _smoothSeconds = 0.07f;

        /// <summary>自动延音分段时，换踏板瞬间的抬起时长（秒）：听感上是「换踏板」，不是一个长按。</summary>
        private const float RePedalGapSeconds = 0.03f;

        private Transform _foot;
        private Transform _toes;
        private Quaternion _restWorld;
        private Vector3 _pitchAxis;
        private float _angle;
        private float _targetAngle;

        /// <summary>是否已绑定到可驱动的脚骨。</summary>
        public bool IsBound => _foot != null;

        /// <summary>当前脚尖抬起角度（度），供测试与调试读取。</summary>
        public float CurrentAngleDegrees => _angle;

        /// <summary>绑定 Humanoid 骨架。重复绑定同一骨架是幂等的；不可用时不改变状态。</summary>
        public void Bind(Animator animator)
        {
            if (animator == null || animator.avatar == null || !animator.isHuman) return;

            var foot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (foot == null) return;
            if (_foot == foot) return;

            _foot = foot;
            _toes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            _restWorld = foot.rotation;

            // 抬脚是「绕脚的左右轴俯仰」，不是绕脚骨自身的局部 X 轴：
            // 导入骨架的局部轴朝向各异（Rocketbox 的 foot 局部 X 接近竖直，绕它转只是在原地打转），
            // 因此用世界空间里「脚指向 × 上方向」构造水平俯仰轴，与导出设置无关。
            Vector3 forward = _toes != null ? _toes.position - foot.position : foot.forward;
            if (forward.sqrMagnitude < 1e-8f) forward = foot.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, forward.normalized);
            _pitchAxis = axis.sqrMagnitude < 1e-8f ? foot.right : axis.normalized;

            _angle = 0f;
            _targetAngle = 0f;
        }

        /// <summary>解除绑定（换角色时调用），把脚恢复为绑定时的姿态。</summary>
        public void Unbind()
        {
            if (_foot != null) _foot.rotation = _restWorld;
            _foot = null;
            _toes = null;
            _angle = 0f;
        }

        /// <summary>
        /// 每帧推进。回到 0 后若本就静止则不做任何旋转写入，避免与其它姿态代码打架。
        /// </summary>
        public void Tick(bool pressed, float deltaTime)
        {
            if (_foot == null) return;

            _targetAngle = pressed ? _pressAngleDegrees : 0f;
            if (Mathf.Approximately(_angle, _targetAngle) && Mathf.Approximately(_angle, 0f)) return;

            float k = _smoothSeconds <= 1e-4f ? 1f : Mathf.Clamp01(deltaTime / _smoothSeconds);
            _angle = Mathf.Lerp(_angle, _targetAngle, k);
            if (Mathf.Abs(_angle - _targetAngle) < 0.05f) _angle = _targetAngle;

            // 绕水平俯仰轴负转 = 脚尖抬起（脚跟着地），这正是踩踏板的方向（符号按 Rocketbox 实测）
            _foot.rotation = Quaternion.AngleAxis(-_angle, _pitchAxis) * _restWorld;
        }

        /// <summary>
        /// 曲目没有 CC64 时的自动延音（legato 踏板）：有音符在响就踩住，全部松开就抬起。
        /// <para>
        /// 这样内置 demo（无踏板事件）也能看到脚在动，而不是为了「有动作」硬编一段假动画；
        /// 有 CC64 的曲目优先使用文件里真实录下的踏板。
        /// </para>
        /// </summary>
        public static List<SustainSpan> BuildAutoSustainSpans(IReadOnlyList<MidiNoteEvent> notes,
            float maxSpanSeconds = 2.2f)
        {
            var spans = new List<SustainSpan>();
            if (notes == null || notes.Count == 0) return spans;

            // 事件扫描：+1 起音、-1 收音，计数从 0 变正即踩下，回到 0 即抬起
            var edges = new List<(float time, int delta)>(notes.Count * 2);
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Duration <= 0f) continue;
                edges.Add((n.StartTime, 1));
                edges.Add((n.EndTime, -1));
            }
            if (edges.Count == 0) return spans;
            edges.Sort((a, b) => a.time.CompareTo(b.time));

            int sounding = 0;
            float spanStart = 0f;
            foreach (var e in edges)
            {
                int before = sounding;
                sounding += e.delta;
                if (before <= 0 && sounding > 0)
                {
                    spanStart = e.time;
                }
                else if (before > 0 && sounding <= 0)
                {
                    AddSpan(spanStart, e.time);
                }
            }
            if (sounding > 0) AddSpan(spanStart, spanStart + maxSpanSeconds);

            void AddSpan(float from, float to)
            {
                // 长音不断时按窗口切段，模拟真实演奏里「换踏板」而不是一口气踩到底
                while (to - from > maxSpanSeconds)
                {
                    float cut = from + maxSpanSeconds;
                    spans.Add(new SustainSpan(from, cut));
                    from = cut + RePedalGapSeconds;
                }

                if (to - from < 0.05f) return;   // 太短的踏板听感上无意义，也看不出动作
                spans.Add(new SustainSpan(from, to));
            }

            return spans;
        }
    }
}
