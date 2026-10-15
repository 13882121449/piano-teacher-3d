using System.Collections.Generic;
using UnityEngine;
using PianoTeacher3D.MIDI;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 琴键高亮提示（对应方案 2.2「音符或琴键高亮提示」）。
    /// <para>
    /// 与 <see cref="PianoKeyAnimator"/>（物理下压动画）职责解耦：本组件只改颜色，不动角度，
    /// 因此关闭高亮时不会影响按键动画与时间轴。
    /// </para>
    /// <para>
    /// 颜色通过 <see cref="MaterialPropertyBlock"/> 覆盖，不实例化材质，
    /// 88 个琴键不产生额外材质与 draw call；还原时清除属性块即恢复共享材质原色。
    /// </para>
    /// </summary>
    public class PianoKeyHighlighter : MonoBehaviour
    {
        /// <summary>预提示色（即将按下的音符）：暖金。</summary>
        public static readonly Color UpcomingColor = new Color(0.98f, 0.80f, 0.36f);

        /// <summary>左手发声高亮：冷蓝。</summary>
        public static readonly Color LeftHandColor = new Color(0.35f, 0.68f, 1.00f);

        /// <summary>右手发声高亮：暖橙。</summary>
        public static readonly Color RightHandColor = new Color(1.00f, 0.55f, 0.26f);

        private static readonly int ColorId = Shader.PropertyToID("_Color");        // Standard / 内置
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit 兼容

        private PianoKeyMap _keyMap;
        private HighlightMode _mode = HighlightMode.Off;

        // 注意：MaterialPropertyBlock 是 UnityEngine 原生对象，不能在字段初始化器中 new
        // （Unity 禁止在 MonoBehaviour 构造期创建原生对象），因此惰性创建。
        private MaterialPropertyBlock _block;
        private readonly Dictionary<int, Renderer> _renderers = new();
        private readonly HashSet<int> _active = new();
        private readonly HashSet<int> _upcoming = new();
        private readonly HashSet<int> _touched = new();
        private readonly HashSet<int> _pending = new();   // 预提示集合暂存区（用于变更检测）
        private readonly List<int> _scratch = new();

        private MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

        /// <summary>当前高亮模式。切换时立即重算所有已涉及琴键的颜色。</summary>
        public HighlightMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                if (!WantsUpcoming) _upcoming.Clear();
                RecolorAll();
            }
        }

        /// <summary>当前被着色的琴键数量（测试与调试用）。</summary>
        public int HighlightedKeyCount => _touched.Count;

        /// <summary>当前处于发声高亮状态的音符数。</summary>
        public int ActiveNoteCount => _active.Count;

        /// <summary>当前处于预提示状态的音符数。</summary>
        public int UpcomingNoteCount => _upcoming.Count;

        /// <summary>
        /// 累计 MaterialPropertyBlock 写入次数（性能诊断与测试用）。
        /// 预提示集合未变化时不应增长——用于验证「集合不变则不重写材质」的优化。
        /// </summary>
        public int MpbWriteCount { get; private set; }

        private bool WantsUpcoming => _mode == HighlightMode.Upcoming || _mode == HighlightMode.Both;

        public void Initialize(PianoKeyMap keyMap)
        {
            _keyMap = keyMap;
            _renderers.Clear();
            _active.Clear();
            _upcoming.Clear();
            _pending.Clear();
            _touched.Clear();
        }

        /// <summary>音符开始：置为该手对应的高亮色（需 Active / Both 模式）。</summary>
        public void OnNoteOn(MidiNoteEvent e)
        {
            if (!PianoKeyMap.IsInRange(e.Pitch)) return;
            if (!_active.Add(e.Pitch)) return;      // 同音重复触发（连音）不重复着色
            if (_mode != HighlightMode.Off) Recolor(e.Pitch);
        }

        /// <summary>音符结束：清除发声高亮；若该键仍在预提示窗口内则回落到预提示色。</summary>
        public void OnNoteOff(MidiNoteEvent e)
        {
            if (!_active.Remove(e.Pitch)) return;
            if (_mode != HighlightMode.Off) Recolor(e.Pitch);
        }

        /// <summary>
        /// 刷新预提示集合（由 <see cref="Core.LessonSessionController"/> 每帧传入
        /// 落在 [当前时间, 当前时间 + 预提示窗口) 内的音符）。传 null 等同清空。
        /// </summary>
        public void RefreshUpcoming(IReadOnlyList<MidiNoteEvent> notes)
        {
            // 先把新集合构建到暂存区，再与当前集合比较：
            // 集合无变化时直接返回，避免每帧对全部预提示键重写 MaterialPropertyBlock。
            _pending.Clear();
            if (WantsUpcoming && notes != null)
            {
                for (int i = 0; i < notes.Count; i++)
                {
                    int pitch = notes[i].Pitch;
                    if (!PianoKeyMap.IsInRange(pitch)) continue;
                    if (_active.Contains(pitch)) continue;   // 已按下：保持发声色
                    _pending.Add(pitch);
                }
            }

            if (_pending.SetEquals(_upcoming)) return;   // 无变化 → 零 GPU 操作

            _upcoming.Clear();
            _upcoming.UnionWith(_pending);
            RecolorAll();
        }

        /// <summary>清空全部高亮并恢复琴键原始颜色（停止播放 / 换曲 / Seek / 关闭高亮时调用）。</summary>
        public void ClearAll()
        {
            _scratch.Clear();
            _scratch.AddRange(_touched);

            _active.Clear();
            _upcoming.Clear();
            _pending.Clear();
            _touched.Clear();

            for (int i = 0; i < _scratch.Count; i++)
            {
                var r = GetRenderer(_scratch[i]);
                if (r != null) r.SetPropertyBlock(null); // 清除属性块 = 恢复共享材质原色
            }
            _scratch.Clear();
        }

        // ==================== 内部 ====================

        private void ClearUpcoming()
        {
            if (_upcoming.Count == 0) return;
            _upcoming.Clear();
            RecolorAll();
        }

        /// <summary>重算所有涉及琴键的颜色（已着色键 ∪ 当前预提示键）。</summary>
        private void RecolorAll()
        {
            if (_mode == HighlightMode.Off && _touched.Count == 0) return;

            _scratch.Clear();
            _scratch.AddRange(_touched);
            foreach (var pitch in _upcoming)
            {
                if (!_touched.Contains(pitch)) _scratch.Add(pitch);
            }

            for (int i = 0; i < _scratch.Count; i++) Recolor(_scratch[i]);
            _scratch.Clear();
        }

        /// <summary>按「发声优先于预提示」重算单个琴键的应显示颜色。</summary>
        private void Recolor(int pitch)
        {
            if (_mode == HighlightMode.Off)
            {
                RestoreColor(pitch);
                return;
            }

            bool active = _active.Contains(pitch) &&
                          (_mode == HighlightMode.Active || _mode == HighlightMode.Both);
            if (active)
            {
                ApplyColor(pitch, PianoNoteFilter.IsLeftHand(pitch) ? LeftHandColor : RightHandColor);
                return;
            }

            bool upcoming = WantsUpcoming && _upcoming.Contains(pitch);
            if (upcoming) ApplyColor(pitch, UpcomingColor);
            else RestoreColor(pitch);
        }

        private void ApplyColor(int pitch, Color color)
        {
            var r = GetRenderer(pitch);
            if (r == null) return;

            var block = Block;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
            MpbWriteCount++;
            _touched.Add(pitch);
        }

        private void RestoreColor(int pitch)
        {
            if (!_touched.Remove(pitch)) return;
            var r = GetRenderer(pitch);
            if (r == null) return;
            r.SetPropertyBlock(null);
            MpbWriteCount++;
        }

        private Renderer GetRenderer(int pitch)
        {
            if (_renderers.TryGetValue(pitch, out var cached) && cached != null) return cached;
            if (_keyMap == null || !_keyMap.TryGetKey(pitch, out var key) || key == null) return null;

            var r = key.GetComponentInChildren<Renderer>(); // 键身是键根的子物体 "Mesh"
            if (r != null) _renderers[pitch] = r;
            return r;
        }
    }
}
