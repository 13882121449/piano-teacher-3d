using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.UI
{
    /// <summary>动效节拍里的一个参与者：<see cref="Tick"/> 返回 false 表示已静止，节拍器把它移出队列。</summary>
    public interface IUIMotionTickable
    {
        bool Tick(float dt);
    }

    /// <summary>
    /// 界面动效的统一节拍器（2026-11-18）：只推进「正在动」的控件，静止的控件不占每帧开销。
    /// 用不受暂停影响的 <c>unscaledDeltaTime</c>，单帧步长上限 50 ms（卡顿后不跳变）。
    /// 跨场景常驻（隐藏、不保存）。
    /// </summary>
    public sealed class UIMotionTicker : MonoBehaviour
    {
        private static UIMotionTicker _instance;
        private readonly List<IUIMotionTickable> _active = new List<IUIMotionTickable>();
        private readonly HashSet<IUIMotionTickable> _set = new HashSet<IUIMotionTickable>();

        /// <summary>当前在动的控件数（诊断 / 测试用）。</summary>
        public static int ActiveCount => _instance != null ? _instance._active.Count : 0;

        public static void Add(IUIMotionTickable item)
        {
            if (item == null) return;
            if (_instance == null)
            {
                var go = new GameObject("UIMotionTicker") { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying) DontDestroyOnLoad(go);
                _instance = go.AddComponent<UIMotionTicker>();
            }
            if (_instance._set.Add(item)) _instance._active.Add(item);
        }

        public static void Remove(IUIMotionTickable item)
        {
            if (_instance == null || item == null) return;
            if (_instance._set.Remove(item)) _instance._active.Remove(item);
        }

        private readonly List<IUIMotionTickable> _snapshot = new List<IUIMotionTickable>();

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            // 遍历快照：Tick 里面板可能隐藏自己（OnDisable → Remove），直接遍历原列表会越界
            _snapshot.Clear();
            _snapshot.AddRange(_active);
            foreach (var item in _snapshot)
            {
                if (!_set.Contains(item)) continue;
                bool alive;
                try { alive = item != null && item.Tick(dt); }
                catch (MissingReferenceException) { alive = false; }
                if (!alive) Remove(item);
            }
            _snapshot.Clear();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
