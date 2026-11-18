using PianoTeacher3D.Piano;
using UniVRM10;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// VRM 演奏者的「活人感」（2026-11-18）：呼吸、上身随演奏区域转向与前倾、头和眼睛看向正在弹的手、眨眼。
    /// <para>
    /// <b>执行顺序</b>：躯干在 <c>Update</c> 里摆（早于所有 <c>LateUpdate</c>），手臂 IK（LateUpdate, 3）
    /// 按摆好的躯干解算，指尖仍然落在键上；头颈在 LateUpdate（500）里转，晚于 IK 与手指、早于
    /// <c>Vrm10Instance</c>（11000，视线 / 表情 / 头发弹簧骨骼）。
    /// </para>
    /// <para>
    /// 只拥有脊柱、胸、颈、头四类骨骼：每帧以落座后记录的基准旋转为底叠加增量，不会累积漂移；
    /// 坐姿应用器只动髋与腿，IK 只动肩臂手，互不抢骨骼。
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class VrmPerformerLife : MonoBehaviour
    {
        private Animator _animator;
        private Vrm10Instance _vrm;
        private Transform _spine, _chest, _upperChest, _neck, _head, _leftHand, _rightHand, _hips;
        private Quaternion _spineBase, _chestBase, _upperChestBase, _neckBase, _headBase;
        private bool _hasBase;
        private Transform _lookTarget;
        private PianoKeyAnimator _keys;

        private float _activity;          // 0 = 静坐，1 = 演奏中（平滑）
        private float _lastPlayTime = -10f;
        private float _yaw, _yawVel, _lean, _leanVel;
        private Vector3 _focus, _focusVel;
        private float _nextBlink;
        private float _blinkStart = -1f;
        private bool _doubleBlink;
        private float _phase;

        /// <summary>当前上身转向角（度，绕世界 +Y；演奏者面向 −z，正值转向其右侧 / 高音区）。诊断用。</summary>
        public float TorsoYaw => _yaw;

        /// <summary>当前眨眼权重 0–1。诊断用。</summary>
        public float BlinkWeight { get; private set; }

        internal void Bind(Animator animator)
        {
            _animator = animator;
            _vrm = GetComponentInChildren<Vrm10Instance>(true);
            Transform Bone(HumanBodyBones id) => animator != null ? animator.GetBoneTransform(id) : null;
            _hips = Bone(HumanBodyBones.Hips);
            _spine = Bone(HumanBodyBones.Spine);
            _chest = Bone(HumanBodyBones.Chest);
            _upperChest = Bone(HumanBodyBones.UpperChest);
            _neck = Bone(HumanBodyBones.Neck);
            _head = Bone(HumanBodyBones.Head);
            _leftHand = Bone(HumanBodyBones.LeftHand);
            _rightHand = Bone(HumanBodyBones.RightHand);
            _hasBase = false;
            _phase = Random.value * 10f;
            _nextBlink = Time.time + Random.Range(1.5f, 4f);

            if (_lookTarget == null)
            {
                _lookTarget = new GameObject("LookTarget").transform;
                _lookTarget.SetParent(transform, false);
            }
            if (_vrm != null)
            {
                _vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
                _vrm.LookAtTarget = _lookTarget;
            }
        }

        /// <summary>落座之后调用：记录躯干与头颈的基准旋转（之后每帧以它为底叠加增量）。</summary>
        internal void CaptureBase()
        {
            if (_spine == null || _neck == null || _head == null) return;
            _spineBase = _spine.localRotation;
            _chestBase = _chest != null ? _chest.localRotation : Quaternion.identity;
            _upperChestBase = _upperChest != null ? _upperChest.localRotation : Quaternion.identity;
            _neckBase = _neck.localRotation;
            _headBase = _head.localRotation;
            _hasBase = true;
            if (_leftHand != null && _rightHand != null) _focus = (_leftHand.position + _rightHand.position) * 0.5f;
        }

        /// <summary>还原躯干与头颈的基准旋转（角色停用前调用）。</summary>
        internal void RestoreBase()
        {
            if (!_hasBase) return;
            _spine.localRotation = _spineBase;
            if (_chest != null) _chest.localRotation = _chestBase;
            if (_upperChest != null) _upperChest.localRotation = _upperChestBase;
            _neck.localRotation = _neckBase;
            _head.localRotation = _headBase;
            _hasBase = false;
        }

        private void Update()
        {
            if (!_hasBase || _hips == null) return;
            float dt = Time.deltaTime;
            if (_keys == null) _keys = FindFirstObjectByType<PianoKeyAnimator>();
            if (_keys != null && _keys.PressedKeyCount > 0) _lastPlayTime = Time.time;
            float playing = Time.time - _lastPlayTime < 1.2f ? 1f : 0f;
            _activity = Mathf.MoveTowards(_activity, playing, dt / 0.8f);

            // 演奏区域：两手中点相对髋的横向偏移（世界 x，演奏者面向 −z，左手侧是 +x）
            Vector3 hands = _leftHand != null && _rightHand != null ? (_leftHand.position + _rightHand.position) * 0.5f : _hips.position;
            float offset = hands.x - _hips.position.x;
            // 绕 +Y 正转把面向 −z 的身体转向 −x（右手 / 高音侧），所以手在 +x 时取负角
            float yawTarget = Mathf.Clamp(-offset * 16f, -7f, 7f) * _activity;
            _yaw = Mathf.SmoothDamp(_yaw, yawTarget, ref _yawVel, 0.55f, Mathf.Infinity, dt);
            float leanTarget = Mathf.Lerp(1.5f, 5f, _activity);
            _lean = Mathf.SmoothDamp(_lean, leanTarget, ref _leanVel, 0.9f, Mathf.Infinity, dt);

            _phase += dt;
            float breath = Mathf.Sin(_phase * Mathf.PI * 2f * 0.24f);          // 约 4 s 一次呼吸
            float sway = Mathf.Sin(_phase * Mathf.PI * 2f * 0.13f) * _activity;  // 随乐句的轻微左右摆

            // 角色根绕 Y 转了 180°（面向 −z）：在骨骼局部空间里叠加世界轴旋转，用父节点换算
            ApplyWorldDelta(_spine, _spineBase, Quaternion.AngleAxis(_yaw * 0.45f, Vector3.up) *
                                                Quaternion.AngleAxis(_lean * 0.5f, ForwardTiltAxis()) *
                                                Quaternion.AngleAxis(sway * 1.1f, FacingAxis()));
            if (_chest != null)
                ApplyWorldDelta(_chest, _chestBase, Quaternion.AngleAxis(_yaw * 0.35f, Vector3.up) *
                                                    Quaternion.AngleAxis(_lean * 0.3f + breath * 0.6f, ForwardTiltAxis()));
            if (_upperChest != null)
                ApplyWorldDelta(_upperChest, _upperChestBase, Quaternion.AngleAxis(_yaw * 0.2f, Vector3.up) *
                                                              Quaternion.AngleAxis(_lean * 0.2f - breath * 0.9f, ForwardTiltAxis()));
        }

        private void LateUpdate()
        {
            if (!_hasBase) return;
            float dt = Time.deltaTime;

            // 视线焦点：正在弹的区域（两手中点略偏向更低的那只手），平滑跟随
            Vector3 hands = _leftHand != null && _rightHand != null ? (_leftHand.position + _rightHand.position) * 0.5f : _head.position + Vector3.down;
            Vector3 focusTarget = Vector3.Lerp(_head.position + transform.rotation * new Vector3(0f, -0.35f, 0.55f), hands, Mathf.Lerp(0.55f, 1f, _activity));
            _focus = Vector3.SmoothDamp(_focus, focusTarget, ref _focusVel, 0.35f, Mathf.Infinity, dt);
            _lookTarget.position = _focus;

            // 头颈：看向焦点的旋转按 35% / 45% 分给颈和头（其余交给眼睛），俯角上限 28°
            Vector3 toFocus = _focus - _head.position;
            Vector3 facing = FacingDirection();
            float yaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(facing, Vector3.up),
                Vector3.ProjectOnPlane(toFocus, Vector3.up), Vector3.up), -25f, 25f);
            float pitch = Mathf.Clamp(Vector3.Angle(Vector3.ProjectOnPlane(toFocus, Vector3.up), toFocus) *
                                      (toFocus.y < 0f ? 1f : -1f), -10f, 28f);
            ApplyWorldDelta(_neck, _neckBase, Quaternion.AngleAxis(yaw * 0.35f, Vector3.up) *
                                              Quaternion.AngleAxis(pitch * 0.35f, ForwardTiltAxis()));
            ApplyWorldDelta(_head, _headBase, Quaternion.AngleAxis(yaw * 0.45f, Vector3.up) *
                                              Quaternion.AngleAxis(pitch * 0.45f, ForwardTiltAxis()));

            UpdateBlink();
        }

        private void UpdateBlink()
        {
            float now = Time.time;
            if (_blinkStart < 0f && now >= _nextBlink)
            {
                _blinkStart = now;
                _doubleBlink = Random.value < 0.18f;
            }
            float w = 0f;
            if (_blinkStart >= 0f)
            {
                float t = now - _blinkStart;
                // 闭眼 0.06 s → 停 0.03 s → 睁眼 0.10 s
                if (t < 0.06f) w = t / 0.06f;
                else if (t < 0.09f) w = 1f;
                else if (t < 0.19f) w = 1f - (t - 0.09f) / 0.10f;
                else
                {
                    if (_doubleBlink)
                    {
                        _doubleBlink = false;
                        _blinkStart = now + 0.05f;
                    }
                    else
                    {
                        _blinkStart = -1f;
                        _nextBlink = now + Random.Range(2.2f, 5.5f);
                    }
                }
                w = w * w * (3f - 2f * w);
            }
            BlinkWeight = w;
            if (_vrm != null && _vrm.Runtime != null)
                _vrm.Runtime.Expression.SetWeight(ExpressionKey.Blink, w);
        }

        /// <summary>演奏者正面朝向（世界，水平）。</summary>
        private Vector3 FacingDirection()
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.back;
        }

        /// <summary>前倾旋转轴：绕演奏者右侧水平轴转正角 = 向前低头 / 前倾。</summary>
        private Vector3 ForwardTiltAxis() => Vector3.Cross(Vector3.up, FacingDirection()).normalized;

        /// <summary>左右摆的旋转轴（沿面向方向）。</summary>
        private Vector3 FacingAxis() => FacingDirection();

        /// <summary>把世界空间的增量旋转叠到骨骼的基准局部旋转上。</summary>
        private static void ApplyWorldDelta(Transform bone, Quaternion baseLocal, Quaternion worldDelta)
        {
            if (bone == null) return;
            Quaternion parent = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            Quaternion baseWorld = parent * baseLocal;
            bone.rotation = worldDelta * baseWorld;
        }
    }
}
