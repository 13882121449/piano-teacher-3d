using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 按钮的「跟手」动效（2026-11-18）：
    /// ① 悬停轻微放大、按下回缩、松开带一点回弹（弹簧，中途换目标速度连续）；
    /// ② 玻璃表面一团柔光跟着鼠标移动（悬停时出现）；
    /// ③ 按下时从按压点扩散一圈淡淡的光环。
    /// <para>
    /// 光斑与光环放在按钮内一个带 <see cref="RectMask2D"/> 的动效层里，不会溢出按钮；文字不在动效层里，不受裁剪。
    /// 只在动画进行中由 <see cref="UIMotionTicker"/> 逐帧推进，静止后退出，平时零开销
    /// （组件本身保持启用：禁用的 Behaviour 收不到指针事件）；「减少动效」开启时只保留颜色反馈。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIPressMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerMoveHandler, IUIMotionTickable
    {
        private const float HoverScale = 1.035f;
        private const float PressScale = 0.955f;

        private Button _button;
        private RectTransform _rt;
        private RectTransform _fx;
        private Image _sheen;
        private Image _ripple;
        private bool _over;
        private bool _down;
        private float _scale = 1f, _scaleVel;
        private float _sheenAlpha, _sheenAlphaVel;
        private Vector2 _sheenPos, _sheenPosVel, _sheenTarget;
        private float _rippleTime = -1f;

        /// <summary>挂到按钮上（重复调用安全）。</summary>
        public static UIPressMotion Attach(Button button)
        {
            if (button == null) return null;
            var motion = button.GetComponent<UIPressMotion>();
            if (motion == null) motion = button.gameObject.AddComponent<UIPressMotion>();
            return motion;
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _rt = transform as RectTransform;
        }

        private void OnDisable()
        {
            UIMotionTicker.Remove(this);
            _over = _down = false;
            _scale = 1f;
            _scaleVel = 0f;
            if (_rt != null) _rt.localScale = Vector3.one;
            if (_sheen != null) _sheen.color = new Color(1f, 1f, 1f, 0f);
            if (_ripple != null) _ripple.color = new Color(1f, 1f, 1f, 0f);
            _sheenAlpha = 0f;
            _rippleTime = -1f;
        }

        private bool Interactable => _button == null || (_button.interactable && _button.IsActive());

        public void OnPointerEnter(PointerEventData eventData)
        {
            _over = true;
            TrackPointer(eventData, snap: true);
            Wake();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _over = false;
            _down = false;
            Wake();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _down = true;
            TrackPointer(eventData, snap: true);
            if (!UIMotion.Reduced && Interactable)
            {
                EnsureFx();
                _ripple.rectTransform.anchoredPosition = _sheenTarget;
                _rippleTime = 0f;
            }
            Wake();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
            Wake();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!_over) return;
            TrackPointer(eventData, snap: false);
            Wake();
        }

        private void Wake()
        {
            if (isActiveAndEnabled) UIMotionTicker.Add(this);
        }

        private void TrackPointer(PointerEventData eventData, bool snap)
        {
            if (_rt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, eventData.position,
                    eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera, out var local))
                return;
            // 光斑中心限制在按钮内缩一圈的范围里（换算到以中心为原点的坐标）
            Rect r = _rt.rect;
            Vector2 center = r.center;
            Vector2 half = r.size * 0.5f;
            Vector2 p = local - center;
            p.x = Mathf.Clamp(p.x, -half.x * 0.8f, half.x * 0.8f);
            p.y = Mathf.Clamp(p.y, -half.y * 0.6f, half.y * 0.6f);
            _sheenTarget = p;
            if (snap)
            {
                _sheenPos = p;
                _sheenPosVel = Vector2.zero;
            }
        }

        /// <summary>推进一帧；返回 false 表示已静止（由节拍器移出队列）。</summary>
        public bool Tick(float dt)
        {
            if (this == null || _rt == null) return false;
            bool live = Interactable;
            float scaleTarget = !live ? 1f : (_down ? PressScale : (_over ? HoverScale : 1f));
            float sheenTarget = live && _over && !UIMotion.Reduced ? (_down ? 0.10f : 0.17f) : 0f;

            if (UIMotion.Reduced)
            {
                _scale = scaleTarget;
                _scaleVel = 0f;
            }
            else
            {
                // 按下更快更「实」（阻尼大），松开 / 悬停带轻微回弹
                float freq = _down ? 14f : 9f;
                float damping = _down ? 0.9f : 0.55f;
                UIMotion.Spring(ref _scale, ref _scaleVel, scaleTarget, freq, damping, dt);
            }
            _rt.localScale = new Vector3(_scale, _scale, 1f);

            if (sheenTarget > 0f || _sheenAlpha > 0.001f || _rippleTime >= 0f) EnsureFx();
            if (_sheen != null)
            {
                UIMotion.Spring(ref _sheenAlpha, ref _sheenAlphaVel, sheenTarget, 6f, 1f, dt);
                UIMotion.Spring(ref _sheenPos, ref _sheenPosVel, _sheenTarget, 7f, 0.9f, dt);
                float size = Mathf.Min(_rt.rect.width, _rt.rect.height) * 1.5f;
                _sheen.rectTransform.sizeDelta = new Vector2(size, size);
                _sheen.rectTransform.anchoredPosition = _sheenPos;
                _sheen.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_sheenAlpha));
            }
            if (_ripple != null && _rippleTime >= 0f)
            {
                _rippleTime += dt;
                float t = Mathf.Clamp01(_rippleTime / 0.5f);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                float size = Mathf.Max(_rt.rect.width, _rt.rect.height) * Mathf.Lerp(0.15f, 1.5f, eased);
                _ripple.rectTransform.sizeDelta = new Vector2(size, size);
                _ripple.color = new Color(1f, 1f, 1f, 0.22f * (1f - t));
                if (t >= 1f) _rippleTime = -1f;
            }

            bool settled = UIMotion.Settled(_scale, _scaleVel, scaleTarget) &&
                           (_sheen == null || UIMotion.Settled(_sheenAlpha, _sheenAlphaVel, sheenTarget)) &&
                           _rippleTime < 0f && !_over;
            if (settled)
            {
                _rt.localScale = new Vector3(scaleTarget, scaleTarget, 1f);
                return false;
            }
            return true;
        }

        private void EnsureFx()
        {
            if (_fx != null) return;
            var go = new GameObject("MotionFx", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.SetSiblingIndex(0);
            _fx = (RectTransform)go.transform;
            _fx.anchorMin = Vector2.zero;
            _fx.anchorMax = Vector2.one;
            _fx.offsetMin = new Vector2(2f, 2f);
            _fx.offsetMax = new Vector2(-2f, -2f);
            var mask = go.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(6, 6);
            _sheen = UIMotion.CreateFx(_fx, "Sheen", UIMotion.SoftDot);
            _ripple = UIMotion.CreateFx(_fx, "Ripple", UIMotion.SoftDot);
        }
    }
}
