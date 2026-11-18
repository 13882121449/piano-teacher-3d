using System.Collections.Generic;
using PianoTeacher3D.Piano;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>琴键音名显示方式。</summary>
    public enum NoteNameStyle
    {
        Off = 0,
        Letters = 1,    // C D E F G A B
        Solfege = 2     // do re mi fa sol la si
    }

    /// <summary>
    /// 琴键音名（2026-11-18）：在每个白键前端平躺一行小字（C 键额外标八度号，例如 C4），
    /// 挂在键根下，按键时跟着键一起下沉。可切换字母 / 唱名 / 关闭。
    /// </summary>
    public sealed class KeyNameLabels : MonoBehaviour
    {
        private static readonly string[] Letters = { "C", "", "D", "", "E", "F", "", "G", "", "A", "", "B" };
        private static readonly string[] Solfege = { "do", "", "re", "", "mi", "fa", "", "sol", "", "la", "", "si" };

        private readonly List<(int pitch, Text text)> _labels = new List<(int, Text)>();
        private NoteNameStyle _style = NoteNameStyle.Off;

        public NoteNameStyle Style => _style;

        public void Initialize(PianoKeyMap keys)
        {
            var font = PianoTeacher3D.UI.UITheme.FontMedium;
            foreach (var kv in keys.KeyRoots)
            {
                int pitch = kv.Key;
                if (PianoKeyMap.IsBlackKey(pitch) || kv.Value == null) continue;
                var mesh = kv.Value.Find("Mesh");
                if (mesh == null) continue;

                var go = new GameObject("NoteName", typeof(RectTransform));
                go.transform.SetParent(kv.Value, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(40f, 20f);
                // 键面前端：键根在键后端，键长 0.15，键面高 = 白键高；平躺朝上
                rt.localPosition = new Vector3(mesh.localPosition.x, keys.WhiteHeight + 0.0006f, keys.WhiteLength - 0.022f);
                // 画布 x → 世界 −x、画布 y → 世界 −z：坐在 +z 的演奏者正着读（字头朝钢琴内侧）
                rt.localRotation = Quaternion.Euler(90f, 180f, 0f);
                rt.localScale = Vector3.one * 0.00045f;

                var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
                text.transform.SetParent(rt, false);
                text.rectTransform.sizeDelta = new Vector2(40f, 20f);
                text.font = font;
                text.fontSize = 16;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = pitch % 12 == 0 ? new Color(0.05f, 0.30f, 0.62f, 0.95f) : new Color(0.18f, 0.22f, 0.28f, 0.85f);
                text.raycastTarget = false;
                _labels.Add((pitch, text));
                go.SetActive(false);
            }
        }

        public void SetStyle(NoteNameStyle style)
        {
            _style = style;
            foreach (var (pitch, text) in _labels)
            {
                bool on = style != NoteNameStyle.Off;
                text.transform.parent.gameObject.SetActive(on);
                if (!on) continue;
                int pc = pitch % 12;
                string name = style == NoteNameStyle.Letters ? Letters[pc] : Solfege[pc];
                if (style == NoteNameStyle.Letters && pc == 0) name += (pitch / 12 - 1).ToString();
                text.text = name;
            }
        }
    }
}
