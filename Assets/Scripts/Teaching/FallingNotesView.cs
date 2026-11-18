using System.Collections.Generic;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 下落音符（2026-11-18）：未来 <see cref="LookAhead"/> 秒内的音符化成发光圆角条，从琴键后方竖直落向对应的键，
    /// 条长 = 时值，落到键面那一刻正好该按下；左手青色、右手金色，条底标指法数字。
    /// <para>
    /// 所有条合成一个动态网格（每帧重建，几十个四边形），一次绘制；指法数字是钢琴下一个世界空间画布里的
    /// 对象池文字。条放在白键后端与挂弦横梁之间的竖直面上（本地 z = −0.081），竖琴的弦在它后面。
    /// </para>
    /// </summary>
    public sealed class FallingNotesView : MonoBehaviour
    {
        public const float LookAhead = 3.0f;
        private const float RisePerSecond = 0.24f;   // 每秒对应的条长（米）
        private const float PlaneZ = -0.081f;
        private const int LabelPool = 48;

        private static readonly Color LeftColor = new Color(0.30f, 0.86f, 1.0f, 1f);
        private static readonly Color RightColor = new Color(1.0f, 0.76f, 0.30f, 1f);

        private PianoKeyMap _keys;
        private readonly Dictionary<int, float> _keyX = new Dictionary<int, float>();
        private Mesh _mesh;
        private Material _material;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Vector2> _uv2 = new List<Vector2>();
        private readonly List<int> _t = new List<int>();
        private readonly List<Text> _labels = new List<Text>();
        private Transform _labelRoot;

        private IReadOnlyList<MidiNoteEvent> _notes;
        private int[] _fingers;
        private int _cursor;
        private float _lastTime = float.NegativeInfinity;

        /// <summary>当前帧画出的条数（诊断 / 测试用）。</summary>
        public int VisibleBars { get; private set; }

        public void Initialize(PianoKeyMap keys)
        {
            _keys = keys;
            transform.SetParent(keys.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            foreach (var kv in keys.KeyRoots)
            {
                var mesh = kv.Value != null ? kv.Value.Find("Mesh") : null;
                var renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
                if (renderer == null) continue;
                _keyX[kv.Key] = keys.transform.InverseTransformPoint(renderer.bounds.center).x;
            }

            _mesh = new Mesh { name = "FallingNotes" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            var shader = Shader.Find("PianoTeacher3D/FallingNote");
            _material = shader != null ? new Material(shader) { name = "下落音符" } : null;
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            BuildLabels();
        }

        /// <summary>换曲 / 换练习手后重新载入音符与指法。</summary>
        public void SetNotes(IReadOnlyList<MidiNoteEvent> notes, int[] fingers)
        {
            _notes = notes;
            _fingers = fingers;
            _cursor = 0;
            _lastTime = float.NegativeInfinity;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }

        /// <summary>按乐曲时间刷新（由教学总控每帧调用）。</summary>
        public void Refresh(float now)
        {
            _v.Clear(); _c.Clear(); _uv.Clear(); _uv2.Clear(); _t.Clear();
            int label = 0;
            VisibleBars = 0;
            if (_notes != null && _keys != null)
            {
                if (now < _lastTime - 0.001f) _cursor = 0;   // 往回跳转
                _lastTime = now;
                // 游标：跳过早已结束的音（最长音不超过 8 s 的假设下回看 8 s）
                while (_cursor < _notes.Count && _notes[_cursor].StartTime < now - 8f) _cursor++;

                float keyTop = _keys.KeySurfaceY;
                for (int i = _cursor; i < _notes.Count; i++)
                {
                    var n = _notes[i];
                    if (n.StartTime > now + LookAhead) break;
                    if (n.EndTime <= now) continue;
                    if (!_keyX.TryGetValue(n.Pitch, out float x)) continue;

                    bool black = PianoKeyMap.IsBlackKey(n.Pitch);
                    float width = black ? 0.011f : 0.017f;
                    float bottom = keyTop + Mathf.Max(0f, n.StartTime - now) * RisePerSecond;
                    float top = keyTop + Mathf.Min(n.EndTime - now, LookAhead) * RisePerSecond;
                    if (top - bottom < 0.004f) top = bottom + 0.004f;
                    float landing = n.StartTime <= now ? 1f : Mathf.Clamp01(1f - (n.StartTime - now) / 0.25f);
                    Color color = n.Hand == NoteHand.Left ? LeftColor : RightColor;
                    // 远处的条略淡，读起来有景深
                    color.a = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((n.StartTime - now) / LookAhead));
                    AddBar(x, width, bottom, top, color, landing);
                    VisibleBars++;

                    if (label < _labels.Count && _fingers != null && i < _fingers.Length && _fingers[i] > 0 && n.StartTime >= now - 0.05f)
                    {
                        var text = _labels[label++];
                        text.text = ScoreFingering.Label(_fingers[i]);
                        text.color = new Color(1f, 1f, 1f, color.a);
                        // 文字层绕 Y 转了 180°（正面朝演奏者 +z），本地 x / z 取反
                        text.rectTransform.localPosition = new Vector3(-x, bottom + 0.016f, -(PlaneZ + 0.004f)) / 0.001f;
                        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
                    }
                }
            }
            for (int i = label; i < _labels.Count; i++)
                if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetUVs(0, _uv);
            _mesh.SetUVs(1, _uv2);
            _mesh.SetTriangles(_t, 0);
            _mesh.bounds = new Bounds(new Vector3(_keys != null ? _keys.TotalWidth * 0.5f : 0f, 1.2f, PlaneZ), new Vector3(3f, 3f, 0.2f));
        }

        private void AddBar(float x, float width, float bottom, float top, Color color, float landing)
        {
            int i = _v.Count;
            float hw = width * 0.5f;
            _v.Add(new Vector3(x - hw, bottom, PlaneZ));
            _v.Add(new Vector3(x + hw, bottom, PlaneZ));
            _v.Add(new Vector3(x + hw, top, PlaneZ));
            _v.Add(new Vector3(x - hw, top, PlaneZ));
            float aspect = (top - bottom) / width;
            for (int k = 0; k < 4; k++)
            {
                _c.Add(color);
                _uv2.Add(new Vector2(aspect, landing));
            }
            _uv.Add(new Vector2(0f, 0f));
            _uv.Add(new Vector2(1f, 0f));
            _uv.Add(new Vector2(1f, 1f));
            _uv.Add(new Vector2(0f, 1f));
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
            _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
        }

        private void BuildLabels()
        {
            var go = new GameObject("FingerNumbers", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            _labelRoot = go.transform;
            _labelRoot.localScale = Vector3.one * 0.001f;
            _labelRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var font = PianoTeacher3D.UI.UITheme.FontBold;
            for (int i = 0; i < LabelPool; i++)
            {
                var t = new GameObject("Finger", typeof(RectTransform)).AddComponent<Text>();
                t.transform.SetParent(_labelRoot, false);
                t.font = font;
                t.fontSize = 22;
                t.alignment = TextAnchor.MiddleCenter;
                t.raycastTarget = false;
                t.rectTransform.sizeDelta = new Vector2(30f, 30f);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
                t.gameObject.SetActive(false);
                _labels.Add(t);
            }
        }

        private void OnDisable()
        {
            foreach (var l in _labels) if (l != null) l.gameObject.SetActive(false);
            if (_mesh != null) _mesh.Clear();
        }
    }
}
