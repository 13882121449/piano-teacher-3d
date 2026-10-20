using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 扁平控件（chip / 分段项 / 卡片 / 图标按钮）的悬停与焦点反馈。
    /// <para>
    /// 为什么需要独立组件：这些控件的**常态底色是透明的**，而 <see cref="Selectable"/> 的
    /// ColorTint 是「乘算」——alpha 为 0 的底色乘任何系数仍然是 0，悬停不会出现任何变化。
    /// 因此这里直接插值 <see cref="Image"/> 的颜色（透明 → <c>ControlHover</c>），
    /// 顺带负责卡片「悬停升起 2px」与 2px 键盘焦点环。
    /// </para>
    /// <para>
    /// 纯表现层：不读取任何业务状态，不改变控件的可交互性（那是
    /// <see cref="UITheme.PaintChip"/> / <see cref="UITheme.PaintSegment"/> 的职责）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class UIHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image _target;
        private RectTransform _rt;
        private Vector2 _basePosition;
        private Color _idle;
        private Color _hover;
        private Color _pressed;
        private float _lift;
        private bool _over;
        private bool _focused;
        private bool _down;
        private bool _active = true;

        private bool _outlineOn;
        private Color _outlineColor = Color.black;
        private bool _liftCaptured;

        /// <summary>
        /// 给按钮挂上扁平反馈。<paramref name="idle"/> = 常态底色，<paramref name="hover"/> = 悬停底色，
        /// <paramref name="liftPx"/> = 悬停时向上抬起的像素（卡片 2px，其余 0）。
        /// </summary>
        public static UIHoverFeedback Attach(Button button, Color idle, Color hover, float liftPx = 0f)
        {
            if (button == null) return null;
            var feedback = button.gameObject.GetComponent<UIHoverFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<UIHoverFeedback>();
            feedback.Bind(button, idle, hover, liftPx);
            return feedback;
        }

        private void Bind(Button button, Color idle, Color hover, float liftPx)
        {
            _target = button.targetGraphic as Image;
            _rt = button.transform as RectTransform;
            _idle = idle;
            _hover = hover;
            _pressed = PressedOf(idle);
            _lift = Mathf.Max(0f, liftPx);
            _liftCaptured = false;
            Apply();
        }

        /// <summary>更新常态 / 悬停底色（状态变化后由绘制助手调用）。</summary>
        public void SetColors(Color idle, Color hover)
        {
            _idle = idle;
            _hover = hover;
            _pressed = PressedOf(idle);
            Apply();
        }

        /// <summary>按下色：透明底控件用 <see cref="UITheme.ControlPressed"/>（乘算在透明底上没有效果）。</summary>
        private static Color PressedOf(Color idle)
            => idle.a < 0.01f
                ? UITheme.ControlPressed
                : new Color(idle.r * 0.75f, idle.g * 0.75f, idle.b * 0.75f, idle.a);

        /// <summary>记录「非焦点时」的描边状态，失焦后据此恢复（避免焦点环把状态边永久改掉）。</summary>
        public void SetOutlineState(bool on, Color color)
        {
            _outlineOn = on;
            _outlineColor = color;
            if (!_focused && _target != null) UITheme.SetOutline(_target, on, color, 1f);
        }

        /// <summary>禁用时退出所有悬停 / 焦点效果（禁用态由绘制助手直接上色）。</summary>
        public void SetInteractable(bool value)
        {
            _active = value;
            if (!value)
            {
                _over = false;
                _down = false;
                if (_focused) _focused = false;
            }
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _over = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _over = false;
            _down = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _down = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
            Apply();
        }

        public void OnSelect(BaseEventData eventData)
        {
            // 键盘焦点必须可见（2px BorderSelected 环）
            _focused = true;
            if (_target != null) UITheme.SetFocusRing(_target, true);
            Apply();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _focused = false;
            if (_target != null) UITheme.SetOutline(_target, _outlineOn, _outlineColor, 1f);
            Apply();
        }

        private void OnDisable()
        {
            _over = false;
            _down = false;
            _focused = false;
            RestoreLift();
            Apply();
        }

        private void Update()
        {
            // 颜色插值（unscaled：暂停播放时悬停反馈仍然跟手）
            if (_target == null) return;
            Color wanted = Wanted();
            var current = _target.color;
            float t = Mathf.Clamp01(Time.unscaledDeltaTime * 12f);
            if (current != wanted)
                _target.color = Color.Lerp(current, wanted, t);

            if (_rt != null && _lift > 0f)
            {
                if (IsHighlighted && !_liftCaptured)
                {
                    // 首次悬停时才记录基线：卡片的布局位置是在创建之后才写入的
                    _basePosition = _rt.anchoredPosition;
                    _liftCaptured = true;
                }
                if (_liftCaptured)
                {
                    Vector2 pos = _rt.anchoredPosition;
                    float targetY = _basePosition.y + (IsHighlighted ? _lift : 0f);
                    if (!Mathf.Approximately(pos.y, targetY))
                    {
                        pos.y = Mathf.MoveTowards(pos.y, targetY, Time.unscaledDeltaTime * 60f);
                        _rt.anchoredPosition = pos;
                    }
                }
            }
        }

        private bool IsHighlighted => _active && (_over || _focused);

        private Color Wanted()
        {
            if (!_active) return _idle;
            if (_down && _active) return _pressed;
            return IsHighlighted ? _hover : _idle;
        }

        private void Apply()
        {
            if (_target == null) return;
            // 立即应用一次，避免第一帧仍是旧颜色
            _target.color = Wanted();
            if (!IsHighlighted && _liftCaptured) RestoreLift();
        }

        /// <summary>把抬起位移还原到基线（失焦 / 移出 / 禁用时调用）。</summary>
        private void RestoreLift()
        {
            if (_rt == null || !_liftCaptured) return;
            _rt.anchoredPosition = new Vector2(_rt.anchoredPosition.x, _basePosition.y);
            _liftCaptured = false;
        }
    }
}
