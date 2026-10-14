using UnityEngine;

namespace PianoTeacher3D.Camera
{
    public enum CameraMode
    {
        Closeup, // 手部特写
        Full,    // 全景
        Free     // 自由观察
    }

    /// <summary>
    /// 管理手部特写 / 全景 / 自由观察三种视角（方案 4.2 / 3.1）。
    /// 切换只改表现层，不触碰时间轴；三种模式间平滑过渡；Free 模式支持鼠标旋转缩放，
    /// 并用距离/俯仰 clamp 做基础防穿模；按 R 重置视角。
    /// </summary>
    public class CameraModeController : MonoBehaviour
    {
        public CameraMode Mode { get; private set; } = CameraMode.Full;

        [Header("全景预设")]
        public Vector3 FullPosition = new Vector3(0f, 9f, -20f);
        public Vector3 FullTarget = new Vector3(0f, 1.5f, 0f);
        public float FullFov = 50f;

        [Header("手部特写预设")]
        public float CloseupFov = 32f;
        public float CloseupBack = 3.2f;  // 摄像机在手腕后上方距离
        public float CloseupHeight = 1.6f;

        [Header("自由观察")]
        public float OrbitSensitivity = 4f;
        public float ZoomSensitivity = 3f;
        public float MinDistance = 4f;
        public float MaxDistance = 45f;
        public float MinPitch = 5f;
        public float MaxPitch = 85f;
        public float MinHeight = 0.6f; // 防穿地

        [Header("过渡")]
        public float TransitionSpeed = 6f;

        private Camera _cam;
        private Avatar.AvatarManager _avatar;

        private float _yaw;
        private float _pitch;
        private float _distance;
        private Vector3 _orbitTarget;

        public void Initialize(Camera cam, Avatar.AvatarManager avatar)
        {
            _cam = cam;
            _avatar = avatar;

            _orbitTarget = FullTarget;
            _distance = Vector3.Distance(FullPosition, FullTarget);
            var dir = (FullPosition - FullTarget).normalized;
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;

            // 立即就位全景
            _cam.transform.position = FullPosition;
            _cam.transform.LookAt(FullTarget);
            _cam.fieldOfView = FullFov;
        }

        public void SetMode(CameraMode mode) => Mode = mode;

        public void CycleMode()
        {
            Mode = (CameraMode)(((int)Mode + 1) % 3);
        }

        public void ResetView()
        {
            _orbitTarget = FullTarget;
            _yaw = 0f;
            _pitch = 40f;
            _distance = Vector3.Distance(FullPosition, FullTarget);
        }

        private void Update()
        {
            if (_cam == null) return;

            if (Mode == CameraMode.Free) HandleFreeInput();

            Vector3 targetPos, targetLook;
            float targetFov;
            switch (Mode)
            {
                case CameraMode.Closeup:
                    ComputeCloseup(out targetPos, out targetLook);
                    targetFov = CloseupFov;
                    break;
                case CameraMode.Free:
                    ComputeFree(out targetPos, out targetLook);
                    targetFov = FullFov;
                    break;
                default:
                    targetPos = FullPosition;
                    targetLook = FullTarget;
                    targetFov = FullFov;
                    break;
            }

            targetPos.y = Mathf.Max(targetPos.y, MinHeight); // 防穿地

            float t = 1f - Mathf.Exp(-TransitionSpeed * Time.deltaTime);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, targetPos, t);
            var lookRot = Quaternion.LookRotation(targetLook - _cam.transform.position);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, lookRot, t);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, t);
        }

        private void ComputeCloseup(out Vector3 pos, out Vector3 look)
        {
            look = HandsCenter();
            // 摄像机位于手腕中心后上方，向前下方观察，避免手指被琴身遮挡
            var offset = new Vector3(0f, CloseupHeight, -CloseupBack);
            pos = look + _cam.transform.right * 0f + offset;
        }

        private Vector3 HandsCenter()
        {
            if (_avatar == null || _avatar.LeftHand == null || _avatar.RightHand == null)
                return FullTarget;
            return (_avatar.LeftHand.Wrist.position + _avatar.RightHand.Wrist.position) * 0.5f;
        }

        private void HandleFreeInput()
        {
            if (Input.GetMouseButton(1)) // 右键旋转
            {
                _yaw += Input.GetAxis("Mouse X") * OrbitSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * OrbitSensitivity;
                _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            }
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 1e-4f)
            {
                _distance = Mathf.Clamp(_distance - scroll * ZoomSensitivity * _distance, MinDistance, MaxDistance);
            }
        }

        private void ComputeFree(out Vector3 pos, out Vector3 look)
        {
            look = _orbitTarget;
            float pitchRad = _pitch * Mathf.Deg2Rad;
            float yawRad = _yaw * Mathf.Deg2Rad;
            var dir = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * Mathf.Cos(pitchRad));
            pos = look - dir * _distance;
        }
    }
}
