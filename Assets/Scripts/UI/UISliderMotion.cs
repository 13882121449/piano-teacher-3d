using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 进度条 / 滑块的「跟手」反馈（2026-11-18）：悬停时滑钮放大、轨道变粗，拖动时再大一档，松开弹回。
    /// 与 <see cref="UIPressMotion"/> 共用节拍器，静止时零开销。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UISliderMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IUIMotionTickable
    {
        private Slider _slider;
        private RectTransform _handle;
        private RectTransform _track;
        private bool _over;
        private bool _down;
        private float _k, _kVel;   // 0 常态，1 悬停，1.5 拖动

        public static UISliderMotion Attach(Slider slider)
        {
            if (slider == null) return null;
            var motion = slider.GetComponent<UISliderMotion>();
            if (motion == null) motion = slider.gameObject.AddComponent<UISliderMotion>();
            return motion;
        }

        private void Awake()
        {
            _slider = GetComponent<Slider>();
            _handle = _slider != null ? _slider.handleRect : null;
            _track = _slider != null && _slider.fillRect != null ? _slider.fillRect.parent as RectTransform : null;
        }

        private void OnDisable()
        {
            UIMotionTicker.Remove(this);
            _over = _down = false;
            _k = _kVel = 0f;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData) { _over = true; UIMotionTicker.Add(this); }
        public void OnPointerExit(PointerEventData eventData) { _over = false; UIMotionTicker.Add(this); }
        public void OnPointerDown(PointerEventData eventData) { _down = true; UIMotionTicker.Add(this); }
        public void OnPointerUp(PointerEventData eventData) { _down = false; UIMotionTicker.Add(this); }

        public bool Tick(float dt)
        {
            if (this == null) return false;
            bool live = _slider == null || _slider.interactable;
            float target = !live ? 0f : (_down ? 1.5f : (_over ? 1f : 0f));
            if (UIMotion.Reduced) { _k = target; _kVel = 0f; }
            else UIMotion.Spring(ref _k, ref _kVel, target, 8f, 0.6f, dt);
            Apply();
            return !UIMotion.Settled(_k, _kVel, target);
        }

        private void Apply()
        {
            if (_handle != null)
            {
                float s = 1f + 0.22f * _k;
                _handle.localScale = new Vector3(s, s, 1f);
            }
            if (_track != null)
            {
                float y = 1f + 0.35f * Mathf.Clamp01(_k);
                _track.localScale = new Vector3(1f, y, 1f);
            }
        }
    }
}
