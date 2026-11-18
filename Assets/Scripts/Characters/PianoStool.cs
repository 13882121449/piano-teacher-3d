using PianoTeacher3D.Piano;
using UnityEngine;
using Paths = PianoTeacher3D.Piano.SweepMeshBuilder.Paths;
using Profiles = PianoTeacher3D.Piano.SweepMeshBuilder.Profiles;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 琴凳（2026-11-18 与竖琴钢琴同款）：深海蓝漆凳框 + 雾蓝软垫 + 四条小涡卷腿。
    /// 随坐姿落座（凳面顶 = 髋高 − 髋凳差），腿长按凳面高度重新生成，脚落在舞台地面。
    /// 挂在角色根下但保留世界尺寸；网格与临时材质登记在角色资源作用域里一起销毁，共享材质资产不登记。
    /// </summary>
    public sealed class PianoStool
    {
        private const float FrameHeight = 0.045f;
        private const float CushionHeight = 0.034f;
        private const float SeatWidth = 0.44f;
        private const float SeatDepth = 0.31f;

        private readonly CharacterResourceScope _scope;
        private readonly Material _frame;
        private readonly Material _cushion;
        private readonly MeshFilter _legs;

        public PianoStool(Transform characterRoot, CharacterResourceScope scope)
        {
            _scope = scope;
            Root = new GameObject("StoolRoot").transform;
            Root.SetParent(characterRoot, true);
            _frame = PianoSharedMaterials.Get("HarpLacquer") ?? CreateMaterial("StoolFrame", new Color(0.05f, 0.16f, 0.30f), 0.8f, 0.5f);
            _cushion = PianoSharedMaterials.Get("HarpCushion") ?? CreateMaterial("StoolCushion", new Color(0.30f, 0.42f, 0.52f), 0.2f, 0f);

            var seat = new SweepMeshBuilder();
            seat.RoundedBox(new Vector3(0f, -(CushionHeight + FrameHeight * 0.5f), 0f), new Vector3(SeatWidth, FrameHeight, SeatDepth), 0.016f);
            seat.Sweep(new System.Collections.Generic.List<Vector3>
                {
                    new Vector3(-SeatWidth * 0.5f - 0.004f, -(CushionHeight + FrameHeight) + 0.006f, SeatDepth * 0.5f + 0.004f),
                    new Vector3(SeatWidth * 0.5f + 0.004f, -(CushionHeight + FrameHeight) + 0.006f, SeatDepth * 0.5f + 0.004f)
                }, Profiles.RoundedRect(0.012f, 0.012f, 0.0055f, 3), null, Vector3.forward);
            AddPart("StoolFrame", seat.ToMesh("StoolFrame"), _frame);

            var cushion = new SweepMeshBuilder();
            cushion.RoundedBox(new Vector3(0f, -CushionHeight * 0.5f, 0f), new Vector3(SeatWidth * 0.97f, CushionHeight, SeatDepth * 0.96f), 0.016f);
            AddPart("StoolCushion", cushion.ToMesh("StoolCushion"), _cushion);

            _legs = AddPart("StoolLegs", null, _frame).GetComponent<MeshFilter>();
        }

        public Transform Root { get; }

        /// <summary>凳面顶部的世界高度（上次落座后）。</summary>
        public float SeatTop { get; private set; }

        public void FitToPose(SittingPoseApplier pose)
        {
            if (pose == null || Root == null) return;
            Vector3 hip = pose.LastHipPosition;
            SeatTop = hip.y - pose.Settings.HipAboveSeat;
            Root.SetPositionAndRotation(new Vector3(hip.x, SeatTop, hip.z), Quaternion.identity);
            RebuildLegs();
        }

        /// <summary>按当前凳面高度生成四条小涡卷腿：从凳框底角向外鼓出，脚在地面卷成小螺旋。</summary>
        private void RebuildLegs()
        {
            float top = -(CushionHeight + FrameHeight);
            float height = SeatTop + top;   // 凳框底面到地面
            if (height < 0.05f) return;
            var legs = new SweepMeshBuilder();
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 origin = new Vector3(sx * SeatWidth * 0.40f, 0f, sz * SeatDepth * 0.36f);
                    Vector3 outward = new Vector3(sx * 0.5f, 0f, sz).normalized;
                    Vector3 P(float u, float v) => origin + outward * u + Vector3.up * (v - SeatTop);
                    const float r0 = 0.032f, k = 0.16f, start = Mathf.PI * 0.75f;
                    // 螺旋中心高度：最低点（约 270°）减去截面半厚正好落在地面上
                    Vector2 o = new Vector2(0.040f, 0.0302f);
                    Vector2 e = o + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * r0;
                    Vector2 tangent = new Vector2(-Mathf.Sin(start) - k * Mathf.Cos(start),
                        Mathf.Cos(start) - k * Mathf.Sin(start)).normalized;
                    var upper = Paths.Bezier(new[]
                    {
                        P(0f, height), P(0.07f, height * 0.72f), P(e.x - tangent.x * 0.06f, e.y - tangent.y * 0.06f), P(e.x, e.y)
                    }, 0.006f);
                    var foot = Paths.Spiral(P(o.x, o.y), outward, Vector3.up, start, Mathf.PI * 1.6f, r0, k, 0.004f);
                    var path = new System.Collections.Generic.List<Vector3>(upper);
                    for (int i = 1; i < foot.Count; i++) path.Add(foot[i]);
                    legs.Sweep(path, Profiles.RoundedRect(0.026f, 0.028f, 0.010f, 3), u => Mathf.Lerp(1f, 0.55f, u),
                        Vector3.Cross(outward, Vector3.up));
                }
            }
            var old = _legs.sharedMesh;
            var mesh = legs.ToMesh("StoolLegs");
            _scope?.Own(mesh);
            _legs.sharedMesh = mesh;
            if (old != null)
            {
                if (Application.isPlaying) Object.Destroy(old);
                else Object.DestroyImmediate(old);
            }
        }

        private Transform AddPart(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            var filter = go.AddComponent<MeshFilter>();
            if (mesh != null)
            {
                _scope?.Own(mesh);
                filter.sharedMesh = mesh;
            }
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private Material CreateMaterial(string name, Color color, float smoothness, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            _scope?.Own(m);
            return m;
        }
    }
}
