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
        [SerializeField] private float _pressAngleDegrees = 20f;

        /// <summary>踩到底时踏板本体下压的角度（度）。脚被琴凳挡住时，至少踏板在动。</summary>
        [SerializeField] private float _pedalTravelDegrees = 9f;

        /// <summary>角度跟随时间常数（秒）。太硬会像机器，太软会与音符脱节。</summary>
        [SerializeField] private float _smoothSeconds = 0.07f;

        /// <summary>自动延音分段时，换踏板瞬间的抬起时长（秒）：听感上是「换踏板」，不是一个长按。</summary>
        private const float RePedalGapSeconds = 0.03f;

        /// <summary>是否用右腿两骨 IK 把脚踝放到踏板上（关掉即退回只转脚踝的旧行为）。</summary>
        [SerializeField] private bool _useLegIk = true;

        /// <summary>脚踝相对踏板中心的高度偏移（米）：鞋底厚度 + 预留压下行程。</summary>
        [SerializeField] private float _legIkOffsetY = 0.03f;

        private Transform _upperLeg;
        private Transform _lowerLeg;
        private Quaternion _upperRest;
        private Quaternion _lowerRest;

        private Transform _foot;
        private Transform _toes;
        private Transform _pedal;
        private Transform _contact;
        private Transform _heel;
        private Animator _boundAnimator;
        private Vector3 _ballLocal;
        private Vector3 _heelLocal;
        private Quaternion _pedalRest;
        private Quaternion _restWorld;
        private Vector3 _pitchAxis;
        private float _angle;
        private float _targetAngle;

        /// <summary>是否已找到可驱动的踏板模型（与骨架无关，程序化角色也有）。</summary>
        public bool HasPedal => _pedal != null;

        /// <summary>是否已绑定到可驱动的脚骨。</summary>
        public bool IsBound => _foot != null;

        public Animator BoundAnimator => IsBound ? _boundAnimator : null;

        /// <summary>鞋掌代理点到踏板的距离，未绑定时无样本。</summary>
        public float ContactErrorMeters => _foot != null && _contact != null
            ? Vector3.Distance(_foot.TransformPoint(_ballLocal), _contact.position)
            : float.NaN;

        /// <summary>当前脚尖抬起角度（度），供测试与调试读取。</summary>
        public float CurrentAngleDegrees => _angle;

        /// <summary>绑定 Humanoid 骨架。重复绑定同一骨架是幂等的；不可用时不改变状态。</summary>
        public void Bind(Animator animator)
        {
            if (_boundAnimator == animator) return;
            Unbind();
            _boundAnimator = animator;
            if (animator == null || animator.avatar == null || !animator.isHuman) return;

            var foot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (foot == null) return;

            _foot = foot;
            _toes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            _restWorld = foot.rotation;

            // 腿 IK 需要髋/膝两段：优先 RightUpperLeg → RightLowerLeg，拿不到就退回 RightLeg 链。
            _upperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _lowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (_upperLeg != null) _upperRest = _upperLeg.rotation;
            if (_lowerLeg != null) _lowerRest = _lowerLeg.rotation;

            // 抬脚是「绕脚的左右轴俯仰」，不是绕脚骨自身的局部 X 轴：
            // 导入骨架的局部轴朝向各异（Rocketbox 的 foot 局部 X 接近竖直，绕它转只是在原地打转），
            // 因此用世界空间里「脚指向 × 上方向」构造水平俯仰轴，与导出设置无关。
            Vector3 forward = _toes != null ? _toes.position - foot.position : foot.forward;
            if (forward.sqrMagnitude < 1e-8f) forward = foot.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, forward.normalized);
            _pitchAxis = axis.sqrMagnitude < 1e-8f ? foot.right : axis.normalized;

            _ballLocal = foot.InverseTransformPoint((_toes != null ? _toes.position : foot.position + forward.normalized * 0.12f)
                - Vector3.up * 0.025f);
            _heelLocal = foot.InverseTransformPoint(foot.position - forward.normalized * 0.045f - Vector3.up * 0.045f);
        }

        /// <summary>
        /// 右腿两骨解析 IK：把脚踝放到踏板上方，使脚掌真正落在踏板上。
        /// <para>
        /// <b>为什么必须做这一步</b>：踏板是 <c>PianoKeyMap</c> 里写死的场景摆件（世界 y≈0.19），
        /// 坐姿踝高只有 0.063，两者从未建立约束——实测脚比踏板低 130 mm、深 243 mm，
        /// 脚整只埋在琴体里，所以“脚在动”但在画面里根本看不到。
        /// </para>
        /// <para>
        /// 解析解：先按余弦定理算出目标膝角，绕膝轴把小腿转到该角，再用 FromToRotation
        /// 把大腿摆到「髋 → 目标」方向上。每帧从绑定姿态重建，不累加。
        /// </para>
        /// </summary>
        private void SolveLegIk(Vector3 target)
        {
            if (_upperLeg == null || _lowerLeg == null || _foot == null || _pedal == null) return;

            _upperLeg.rotation = _upperRest;
            _lowerLeg.rotation = _lowerRest;
            _foot.rotation = _restWorld;

            Vector3 hip = _upperLeg.position;
            Vector3 knee = _lowerLeg.position;
            Vector3 ankle = _foot.position;
            float l1 = Vector3.Distance(hip, knee);
            float l2 = Vector3.Distance(knee, ankle);
            if (l1 < 1e-4f || l2 < 1e-4f) return;

            Vector3 toTarget = target - hip;
            float dist = Mathf.Clamp(toTarget.magnitude,
                Mathf.Abs(l1 - l2) + 2e-3f, l1 + l2 - 2e-3f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;

            Vector3 bendAxis = Vector3.Cross(knee - hip, ankle - knee);
            if (bendAxis.sqrMagnitude < 1e-8f) bendAxis = _upperLeg.right;
            bendAxis.Normalize();

            float kneeNow = Vector3.Angle(knee - hip, ankle - knee) * Mathf.Deg2Rad;
            float cosWant = Mathf.Clamp((dist * dist - l1 * l1 - l2 * l2) / (2f * l1 * l2), -1f, 1f);
            float kneeWant = Mathf.Acos(cosWant);

            _lowerLeg.rotation = Quaternion.AngleAxis((kneeWant - kneeNow) * Mathf.Rad2Deg, bendAxis) *
                                 _lowerLeg.rotation;

            Vector3 ankleNow = _foot.position;
            Vector3 from = ankleNow - hip;
            if (from.sqrMagnitude > 1e-8f)
            {
                _upperLeg.rotation = Quaternion.FromToRotation(from.normalized, dir) * _upperLeg.rotation;
            }
        }

        /// <summary>解除绑定（换角色时调用），把脚恢复为绑定时的姿态。</summary>
        public void Unbind()
        {
            if (_upperLeg != null) _upperLeg.rotation = _upperRest;
            if (_lowerLeg != null) _lowerLeg.rotation = _lowerRest;
            if (_foot != null) _foot.rotation = _restWorld;
            _boundAnimator = null;
            _upperLeg = _lowerLeg = _foot = _toes = null;
        }

        /// <summary>
        /// 每帧推进。回到 0 后若本就静止则不做任何旋转写入，避免与其它姿态代码打架。
        /// </summary>
        public void Tick(bool pressed, float deltaTime)
        {
            if (_foot == null && _pedal == null) return;

            _targetAngle = pressed ? _pressAngleDegrees : 0f;
            float k = _smoothSeconds <= 1e-4f ? 1f : Mathf.Clamp01(deltaTime / _smoothSeconds);
            _angle = Mathf.Lerp(_angle, _targetAngle, k);
            if (Mathf.Abs(_angle - _targetAngle) < 0.05f) _angle = _targetAngle;
            ApplyPose();
        }

        public void SetImmediate(bool pressed)
        {
            _angle = _targetAngle = pressed ? _pressAngleDegrees : 0f;
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_pedal != null)
            {
                float amount = _pressAngleDegrees > 1e-3f ? _angle / _pressAngleDegrees : 0f;
                _pedal.localRotation = _pedalRest * Quaternion.AngleAxis(_pedalTravelDegrees * amount, Vector3.right);
            }

            if (_foot == null) return;
            if (_contact != null && _heel != null && _useLegIk)
            {
                // 先移动踏板，以当前鞋掌接触点和鞋跟为基准求踝部。
                Vector3 ball = _contact.position;
                Vector3 shoeDirection = ball - _heel.position;
                Vector3 sole = _ballLocal - _heelLocal;
                if (shoeDirection.sqrMagnitude > 1e-8f && sole.sqrMagnitude > 1e-8f)
                {
                    Quaternion aim = Quaternion.FromToRotation(_restWorld * sole, shoeDirection) * _restWorld;
                    Vector3 target = ball - aim * _ballLocal;
                    SolveLegIk(target);
                    _foot.rotation = aim;
                    return;
                }
            }

            if (_useLegIk && _pedal != null)
                SolveLegIk(_pedal.position + new Vector3(0f, _legIkOffsetY, 0.02f));
            _foot.rotation = Quaternion.AngleAxis(-_angle, _pitchAxis) * _restWorld;
        }

        /// <summary>按琴的铰链和接触标记绑定，琴体重建时恢复旧姿态。</summary>
        public void BindPedal(Transform hinge, Transform contact, Transform heel)
        {
            if (_pedal == hinge && _contact == contact && _heel == heel) return;
            if (_pedal != null) _pedal.localRotation = _pedalRest;
            _pedal = hinge;
            _contact = contact;
            _heel = heel;
            if (_pedal != null) _pedalRest = _pedal.localRotation;
        }

        /// <summary>旧场景和独立诊断可以按名称绑定。</summary>
        public void BindPedal(string pedalName = "Pedal_R")
        {
            if (_pedal != null) return;
            BindPedal(GameObject.Find(pedalName)?.transform, null, null);
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
