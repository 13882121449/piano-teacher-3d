using System.Collections.Generic;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 滚动大谱表（2026-11-18）：顶部一块毛玻璃，左侧高音 / 低音谱号，青色播放线固定，
    /// 音符从右往左流过播放线——过线即该按下。右手音在高音谱表、左手音在低音谱表，
    /// 升号、加线、指法数字齐全；符头颜色与下落音符一致（左手青、右手金）。
    /// </summary>
    public sealed class StaffView : MonoBehaviour
    {
        private const float Width = 640f;
        private const float Height = 176f;
        private const float Line = 9f;                 // 线间距（像素）
        private const float PlayheadX = 118f;
        private const float PixelsPerSecond = 120f;
        private const float TrebleBottom = 104f;       // 高音谱表第一线（距面板底）
        private const float BassBottom = 34f;          // 低音谱表第一线
        private const int TrebleRef = 64;              // E4 = 高音谱表第一线
        private const int BassRef = 43;                // G2 = 低音谱表第一线

        private static readonly Color LeftColor = new Color(0.36f, 0.88f, 1f, 1f);
        private static readonly Color RightColor = new Color(1f, 0.78f, 0.34f, 1f);

        private RectTransform _panel;
        private RectTransform _content;
        private readonly List<Image> _heads = new List<Image>();
        private readonly List<Text> _accidentals = new List<Text>();
        private readonly List<Text> _fingers = new List<Text>();
        private readonly List<Image> _ledgers = new List<Image>();
        private IReadOnlyList<MidiNoteEvent> _notes;
        private int[] _fingerPlan;

        public RectTransform Panel => _panel;

        /// <summary>当前画出的符头数（诊断 / 测试用）。</summary>
        public int VisibleHeads { get; private set; }

        public void Build(Transform canvas)
        {
            _panel = UITheme.CreatePanel(canvas, "StaffPanel", Vector2.zero, new Vector2(Width, Height),
                UITheme.PanelBgGlass, UITheme.RadiusLarge);
            _panel.GetComponent<Image>().raycastTarget = false;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = new Vector2(0f, -(UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space16));
            UIAppear.Attach(_panel.gameObject, 0f, 0.95f);

            _content = new GameObject("Staff", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_panel, false);
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = new Vector2(10f, 0f);
            _content.offsetMax = new Vector2(-10f, 0f);
            _content.gameObject.AddComponent<RectMask2D>();

            Color lineColor = new Color(1f, 1f, 1f, 0.55f);
            for (int s = 0; s < 2; s++)
            {
                float bottom = s == 0 ? TrebleBottom : BassBottom;
                for (int i = 0; i < 5; i++) Rect("StaffLine", 0f, bottom + i * Line - 0.5f, Width - 20f, 1f, lineColor);
            }
            Rect("Brace", 2f, BassBottom, 2f, TrebleBottom + 4f * Line - BassBottom, lineColor);
            Glyph(StaffGlyphs.Treble, 8f, TrebleBottom, 3.6f, 9.2f, -0.2f, -2.6f);
            Glyph(StaffGlyphs.Bass, 10f, BassBottom, 3.8f, 5.2f, -0.2f, -0.6f);
            var head = Rect("Playhead", PlayheadX - 1f, 14f, 2f, Height - 28f, new Color(0.40f, 0.92f, 1f, 0.9f));
            head.name = "Playhead";

            for (int i = 0; i < 40; i++) _ledgers.Add(Rect("Ledger", 0f, 0f, 16f, 1f, lineColor));
            for (int i = 0; i < 72; i++)
            {
                var img = new GameObject("Head", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(_content, false);
                img.sprite = StaffGlyphs.NoteHead;
                img.raycastTarget = false;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.sizeDelta = new Vector2(Line * 1.35f, Line * 1.02f);
                img.gameObject.SetActive(false);
                _heads.Add(img);
                _fingers.Add(Label("Finger", 11, UITheme.FontBold));
            }
            for (int i = 0; i < 24; i++) _accidentals.Add(Label("Sharp", 15, UITheme.Font));
        }

        public void SetNotes(IReadOnlyList<MidiNoteEvent> notes, int[] fingers)
        {
            _notes = notes;
            _fingerPlan = fingers;
        }

        public void SetVisible(bool visible)
        {
            if (_panel == null) return;
            if (visible) _panel.gameObject.SetActive(true);
            else UIAppear.Hide(_panel.gameObject);
        }

        public void Refresh(float now)
        {
            if (_panel == null || !_panel.gameObject.activeInHierarchy) return;
            int head = 0, acc = 0, ledger = 0;
            if (_notes != null)
            {
                float from = now - (PlayheadX - 20f) / PixelsPerSecond;
                float to = now + (Width - 20f - PlayheadX) / PixelsPerSecond;
                for (int i = 0; i < _notes.Count && head < _heads.Count; i++)
                {
                    var n = _notes[i];
                    if (n.StartTime < from) continue;
                    if (n.StartTime > to) break;
                    bool treble = n.Hand == NoteHand.Right || (n.Hand != NoteHand.Left && n.Pitch >= 60);
                    int step = ScoreFingering.WhiteIndex(n.Pitch) - ScoreFingering.WhiteIndex(treble ? TrebleRef : BassRef);
                    float bottom = treble ? TrebleBottom : BassBottom;
                    float y = bottom + step * Line * 0.5f;
                    float x = PlayheadX + (n.StartTime - now) * PixelsPerSecond;
                    bool past = n.StartTime < now - 0.02f;
                    bool sounding = n.StartTime <= now && n.EndTime > now;
                    Color c = n.Hand == NoteHand.Left ? LeftColor : RightColor;
                    if (past && !sounding) c.a = 0.35f;

                    var img = _heads[head];
                    img.rectTransform.anchoredPosition = new Vector2(x, y);
                    img.color = sounding ? Color.Lerp(c, Color.white, 0.35f) : c;
                    img.rectTransform.localScale = Vector3.one * (sounding ? 1.18f : 1f);
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

                    var finger = _fingers[head];
                    int f = _fingerPlan != null && i < _fingerPlan.Length ? _fingerPlan[i] : 0;
                    if (f > 0)
                    {
                        finger.text = ScoreFingering.Label(f);
                        finger.color = new Color(1f, 1f, 1f, c.a);
                        finger.rectTransform.anchoredPosition = new Vector2(x, y + (treble ? Line * 1.6f : -Line * 1.6f));
                        if (!finger.gameObject.activeSelf) finger.gameObject.SetActive(true);
                    }
                    else if (finger.gameObject.activeSelf) finger.gameObject.SetActive(false);
                    head++;

                    if (PianoTeacher3D.Piano.PianoKeyMap.IsBlackKey(n.Pitch) && acc < _accidentals.Count)
                    {
                        var sharp = _accidentals[acc++];
                        sharp.text = "♯";
                        sharp.color = c;
                        sharp.rectTransform.anchoredPosition = new Vector2(x - Line * 1.35f, y + 1f);
                        if (!sharp.gameObject.activeSelf) sharp.gameObject.SetActive(true);
                    }

                    // 加线：第一线以下 / 第五线以上的偶数位置
                    for (int s = -2; s >= step && ledger < _ledgers.Count; s -= 2) PlaceLedger(ref ledger, x, bottom + s * Line * 0.5f, c.a);
                    for (int s = 10; s <= step && ledger < _ledgers.Count; s += 2) PlaceLedger(ref ledger, x, bottom + s * Line * 0.5f, c.a);
                }
            }
            VisibleHeads = head;
            for (int i = head; i < _heads.Count; i++)
            {
                if (_heads[i].gameObject.activeSelf) _heads[i].gameObject.SetActive(false);
                if (_fingers[i].gameObject.activeSelf) _fingers[i].gameObject.SetActive(false);
            }
            for (int i = acc; i < _accidentals.Count; i++)
                if (_accidentals[i].gameObject.activeSelf) _accidentals[i].gameObject.SetActive(false);
            for (int i = ledger; i < _ledgers.Count; i++)
                if (_ledgers[i].gameObject.activeSelf) _ledgers[i].gameObject.SetActive(false);
        }

        private void PlaceLedger(ref int index, float x, float y, float alpha)
        {
            var l = _ledgers[index++];
            l.rectTransform.anchoredPosition = new Vector2(x - 8f, y - 0.5f);
            l.color = new Color(1f, 1f, 1f, 0.55f * alpha);
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        }

        private Image Rect(string name, float x, float y, float w, float h, Color color)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(_content, false);
            img.raycastTarget = false;
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return img;
        }

        private void Glyph(Sprite sprite, float x, float staffBottom, float wUnits, float hUnits, float minX, float minY)
        {
            var img = Rect("Clef", x + minX * Line, staffBottom + minY * Line, wUnits * Line, hUnits * Line, new Color(1f, 1f, 1f, 0.92f));
            img.sprite = sprite;
        }

        private Text Label(string name, int size, Font font)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(_content, false);
            t.font = font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(20f, 18f);
            t.gameObject.SetActive(false);
            return t;
        }
    }
}
