using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>用两个缓慢错相的轨道驱动舞台远景。</summary>
    internal sealed class StageRibbonMotion : MonoBehaviour
    {
        private Transform _near;
        private Transform _far;
        private float _startTime;

        internal void Bind(Transform near, Transform far)
        {
            _near = near;
            _far = far;
            _startTime = Time.unscaledTime;
            ApplyPose(0f);
        }

        private void Update() => ApplyPose(Time.unscaledTime - _startTime);

        private void ApplyPose(float time)
        {
            _near.localRotation = Quaternion.Euler(0f, 19f * Mathf.Sin(time * 0.43f), 0f);
            _far.localRotation = Quaternion.Euler(0f, -24f * Mathf.Sin(time * 0.36f + 0.35f), 0f);
            _near.localPosition = Vector3.up * (0.09f * Mathf.Sin(time * 0.72f));
            _far.localPosition = Vector3.up * (0.13f * Mathf.Sin(time * 0.53f + 1.1f));
        }

        internal static Mesh CreateArc(float radius, float height, float width, float start, float sweep)
        {
            const int segments = 64;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = (start + sweep * t) * Mathf.Deg2Rad;
                float y = height + 0.25f * Mathf.Sin(t * Mathf.PI * 2.0f - 1.12f) + t * 0.22f;
                float x = radius * Mathf.Sin(angle);
                float z = radius * Mathf.Cos(angle);
                int v = i * 2;
                vertices[v] = new Vector3(x, y - width * 0.5f, z);
                vertices[v + 1] = new Vector3(x, y + width * 0.5f, z);
                uv[v] = new Vector2(t, 0f);
                uv[v + 1] = new Vector2(t, 1f);
                if (i == segments) continue;
                int k = i * 6;
                triangles[k] = v;
                triangles[k + 1] = v + 1;
                triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 2;
                triangles[k + 4] = v + 1;
                triangles[k + 5] = v + 3;
            }
            var mesh = new Mesh { name = "StageRibbonArc", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
