using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>首页专用色板。冷白用于首要动作，乐谱和教学的左右手提示继续使用原有色板。</summary>
    internal static class MenuStagePalette
    {
        public static readonly Color Ink = new Color32(8, 11, 19, 255);
        public static readonly Color Library = new Color32(12, 16, 26, 252);
        public static readonly Color Row = new Color32(23, 31, 49, 255);
        public static readonly Color Text = new Color32(242, 246, 252, 255);
        public static readonly Color Secondary = new Color32(179, 194, 220, 255);
        public static readonly Color Action = new Color32(213, 229, 255, 255);
        public static readonly Color Rule = new Color32(124, 146, 187, 80);
    }

    /// <summary>朝画面右侧收束的琴键带，按固定频率更新高光。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class MenuKeyRibbon : MaskableGraphic
    {
        protected override void Awake()
        {
            base.Awake();
            useLegacyMeshGeneration = false;
        }

        private const int WhiteKeys = 42;
        private const float CycleSeconds = 6.5f;
        private const float UpdatePeriod = 1f / 16f;
        private float _nextUpdate;
        private float _phase;
        private bool _reducedMotion;

        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
            if (reduced) _phase = 0.45f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect area = rectTransform.rect;
            for (int key = WhiteKeys - 1; key >= 0; --key)
            {
                float first = key / (float)WhiteKeys;
                float last = (key + 0.90f) / WhiteKeys;
                float distance = Mathf.Abs(_phase - first);
                float pulse = Mathf.Clamp01(1f - Mathf.Min(distance, 1f - distance) * 6f);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Lerp(168f, 29f, first) + pulse * (_reducedMotion ? 0f : 74f));
                var white = new Color32(158, 197, 254, alpha);
                AddKey(vertices, area, first, last, -0.48f, 0.51f, white);

                int degree = key % 7;
                if (degree == 2 || degree == 6 || key == WhiteKeys - 1) continue;
                var black = new Color32(20, 31, 58, alpha);
                AddKey(vertices, area, (key + 0.67f) / WhiteKeys, (key + 1.12f) / WhiteKeys,
                    -0.48f, 0.11f, black);
            }
        }

        private void Update()
        {
            if (_reducedMotion) return;
            float now = Time.unscaledTime;
            if (now < _nextUpdate) return;
            _nextUpdate = now + UpdatePeriod;
            _phase = Mathf.Repeat(now / CycleSeconds, 1f);
            SetVerticesDirty();
        }

        private static void AddKey(VertexHelper vertices, Rect area, float first, float last,
            float near, float far, Color32 color)
        {
            int start = vertices.currentVertCount;
            vertices.AddVert(Project(area, first, near), color, Vector2.zero);
            vertices.AddVert(Project(area, last, near), color, Vector2.zero);
            vertices.AddVert(Project(area, last, far), color, Vector2.zero);
            vertices.AddVert(Project(area, first, far), color, Vector2.zero);
            vertices.AddTriangle(start, start + 1, start + 2);
            vertices.AddTriangle(start + 2, start + 3, start);
        }

        private static Vector2 Project(Rect area, float time, float edge)
        {
            float angle = 4.50f - time * 8.85f;
            float distance = Mathf.Pow(1f - time, 1.37f);
            float radius = distance * (0.34f + edge * 0.154f);
            float x = 0.74f + Mathf.Cos(angle) * radius;
            float y = 0.33f + time * 0.31f + Mathf.Sin(angle) * radius * 0.94f;
            return new Vector2(area.xMin + area.width * x, area.yMin + area.height * y);
        }
    }

    /// <summary>左侧保留稳定的正文对比度，底部柔和沉入曲库，不用全屏模糊。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class MenuStageScrim : Graphic
    {
        protected override void Awake()
        {
            base.Awake();
            useLegacyMeshGeneration = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect bounds = rectTransform.rect;
            AddStrip(vertices, bounds, 0f, 0.29f, 245, 218, true);
            AddStrip(vertices, bounds, 0.29f, 0.57f, 218, 42, true);
            AddStrip(vertices, bounds, 0.57f, 0.77f, 42, 0, true);
            AddStrip(vertices, bounds, 0f, 0.28f, 244, 0, false);
        }

        private static void AddStrip(VertexHelper mesh, Rect bounds, float a, float b, byte low, byte high,
            bool horizontal)
        {
            Color32 lowColor = MenuStagePalette.Ink;
            Color32 highColor = lowColor;
            lowColor.a = low;
            highColor.a = high;
            float x0 = horizontal ? Mathf.Lerp(bounds.xMin, bounds.xMax, a) : bounds.xMin;
            float x1 = horizontal ? Mathf.Lerp(bounds.xMin, bounds.xMax, b) : bounds.xMax;
            float y0 = horizontal ? bounds.yMin : Mathf.Lerp(bounds.yMin, bounds.yMax, a);
            float y1 = horizontal ? bounds.yMax : Mathf.Lerp(bounds.yMin, bounds.yMax, b);
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x0, y0), lowColor, Vector2.zero);
            mesh.AddVert(new Vector2(x0, y1), horizontal ? lowColor : highColor, Vector2.zero);
            mesh.AddVert(new Vector2(x1, y1), highColor, Vector2.zero);
            mesh.AddVert(new Vector2(x1, y0), horizontal ? highColor : lowColor, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
    }

    /// <summary>按舞台宽高裁切摄影背景，在窄屏时优先保留右侧的三角琴。</summary>
    internal sealed class MenuStageCrop : MonoBehaviour
    {
        public RawImage Image;
        public float SourceAspect;

        private void Start() => Crop();

        private void OnRectTransformDimensionsChange() => Crop();

        private void Crop()
        {
            if (Image == null || SourceAspect <= 0f) return;
            Rect bounds = Image.rectTransform.rect;
            if (bounds.height <= 0f) return;

            float targetAspect = bounds.width / bounds.height;
            float width = Mathf.Min(1f, targetAspect / SourceAspect);
            float height = Mathf.Min(1f, SourceAspect / targetAspect);
            Image.uvRect = new Rect((1f - width) * 0.77f, (1f - height) * 0.56f, width, height);
        }
    }
}
