using System;
using UnityEngine;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 面板出现 / 消失动效（2026-11-18）：出现时从略小、略偏移、透明弹到原位（轻微回弹），
    /// 消失时反向收起再隐藏。每次 <c>OnEnable</c> 自动播放出现动画，所以只要把它挂在面板根节点，
    /// 原来的 <c>SetActive(true)</c> 调用不用改；需要消失动画的地方改调 <see cref="Hide"/>。
    /// <para>依靠 <see cref="CanvasGroup"/> 改整体透明度，只动缩放与位置，不触发布局重建。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIAppear : MonoBehaviour, IUIMotionTickable
    {
        /// <summary>出现前的缩放。</summary>
        public float FromScale = 0.94f;

        /// <summary>
        /// 出现前的位置偏移（像素，相对最终位置）。默认 0：只缩放 + 淡入（围绕面板自身的轴心，
        /// 右上角锚定的浮层就从右上角弹出）；只有布局固定的面板才设偏移，避免与布局代码抢位置。
        /// </summary>
        public Vector2 FromOffset = Vector2.zero;

        /// <summary>出现延迟（秒），用于多个面板依次浮现。</summary>
        public float Delay;

        /// <summary>弹簧频率（Hz）。</summary>
        public float Frequency = 5.5f;

        /// <summary>阻尼比（&lt;1 带回弹）。</summary>
        public float Damping = 0.72f;

        private RectTransform _rt;
        private CanvasGroup _group;
        private Vector2 _appliedOffset;
        private float _t, _tVel;        // 0 = 收起，1 = 完全展开
        private float _target;
        private float _wait;
        private Action _onHidden;

        /// <summary>挂到面板上（重复调用安全；在布局完成后调用以应用入场参数）。</summary>
        public static UIAppear Attach(GameObject panel, float delay = 0f, float fromScale = 0.94f, Vector2? fromOffset = null)
        {
            if (panel == null) return null;
            var appear = panel.GetComponent<UIAppear>();
            if (appear == null) appear = panel.AddComponent<UIAppear>();
            appear.RevertOffset();
            appear.Delay = delay;
            appear.FromScale = fromScale;
            appear.FromOffset = fromOffset ?? Vector2.zero;
            if (panel.activeInHierarchy) appear.BeginAppear();
            return appear;
        }

        /// <summary>播放消失动画后隐藏（没有挂 <see cref="UIAppear"/> 或开启了「减少动效」时立即隐藏）。</summary>
        public static void Hide(GameObject panel, Action onHidden = null)
        {
            if (panel == null) return;
            var appear = panel.GetComponent<UIAppear>();
            if (appear == null || !panel.activeInHierarchy || UIMotion.Reduced)
            {
                panel.SetActive(false);
                onHidden?.Invoke();
                return;
            }
            appear._onHidden = onHidden;
            appear._target = 0f;
            appear._wait = 0f;
            UIMotionTicker.Add(appear);
        }

        /// <summary>是否正在播放（诊断 / 测试用）。</summary>
        public bool Animating => _target > 0.5f ? _t < 0.999f : _t > 0.001f;

        /// <summary>当前展开进度 0–1。</summary>
        public float Progress => _t;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _group = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            BeginAppear();
        }

        private void BeginAppear()
        {
            if (_rt == null) Awake();
            RevertOffset();
            _onHidden = null;
            _target = 1f;
            if (UIMotion.Reduced)
            {
                _t = 1f;
                _tVel = 0f;
                Apply();
                return;
            }
            _t = 0f;
            _tVel = 0f;
            _wait = Delay;
            Apply();
            UIMotionTicker.Add(this);
        }

        private void RevertOffset()
        {
            if (_rt != null && _appliedOffset != Vector2.zero)
            {
                _rt.anchoredPosition -= _appliedOffset;
                _appliedOffset = Vector2.zero;
            }
        }

        private void OnDisable()
        {
            UIMotionTicker.Remove(this);
            RevertOffset();
            if (_rt != null) _rt.localScale = Vector3.one;
            if (_group != null) _group.alpha = 1f;
        }

        /// <summary>保留兼容签名：改用相对位移后布局变化不再漂移。</summary>
        public void RecaptureHome()
        {
        }

        public bool Tick(float dt)
        {
            if (this == null || _rt == null) return false;
            if (_wait > 0f)
            {
                _wait -= dt;
                return true;
            }
            bool closing = _target < 0.5f;
            // 收起更快、无回弹（iOS 的消失动画比出现短）
            UIMotion.Spring(ref _t, ref _tVel, _target, closing ? Frequency * 1.5f : Frequency, closing ? 1f : Damping, dt);
            Apply();
            if (UIMotion.Settled(_t, _tVel, _target, 0.002f))
            {
                _t = _target;
                Apply();
                if (closing)
                {
                    var callback = _onHidden;
                    _onHidden = null;
                    gameObject.SetActive(false);
                    callback?.Invoke();
                }
                return false;
            }
            return true;
        }

        private void Apply()
        {
            if (_rt == null) return;
            float t = _t;
            float alpha = Mathf.Clamp01(t * 1.6f);
            float scale = Mathf.LerpUnclamped(FromScale, 1f, t);
            if (_group != null) _group.alpha = alpha;
            _rt.localScale = new Vector3(scale, scale, 1f);
            Vector2 wanted = FromOffset != Vector2.zero
                ? FromOffset * (1f - Mathf.Clamp(t, 0f, 1.2f))
                : Vector2.zero;
            if (wanted != _appliedOffset)
            {
                _rt.anchoredPosition += wanted - _appliedOffset;
                _appliedOffset = wanted;
            }
        }
    }
}
