using UnityEngine;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Avatar
{
    /// <summary>
    /// 指节与琴键的穿模检测（2026-11-18 全面优化 P4）。
    /// <para>
    /// 原有指标只看指尖（压键深度）与手掌（掌心高度），中节、近节和拇指侧面压进相邻琴键
    /// （典型是拇指弹 C 时侧面陷进升 C 黑键）不会被计入。这里把每根手指的指根、各关节和节段中点
    /// 当作带皮肤半径的球，和附近琴键的包围盒求距离：<c>余量 = 球心到盒的距离 − 皮肤半径</c>，
    /// 负值即穿模深度。包围盒在琴键根节点的局部空间里计算，按下时的琴键转角自然计入，
    /// 黑键高出白键的侧壁也在盒内。
    /// </para>
    /// <para>
    /// 该手指正在按的键只对末节与指尖排除——指腹本来就要贴住键面。
    /// 无托管分配：琴键盒只在 <see cref="Build"/> 时算一次，逐帧只做数组遍历和向量运算。
    /// </para>
    /// </summary>
    public sealed class KeyClearanceProbe
    {
        /// <summary>拇指皮肤半径（米，成人；按手指长度缩放）。</summary>
        public const float ThumbSkinRadius = 0.0095f;

        /// <summary>其余四指皮肤半径（米，成人；按手指长度缩放）。</summary>
        public const float FingerSkinRadius = 0.0075f;

        /// <summary>允许的穿模容差（米）：低于 −2 mm 记为穿模。</summary>
        public const float PenetrationTolerance = 0.002f;

        private const float AdultIndexLength = 0.085f;
        private const int KeyCount = PianoKeyMap.MaxPitch - PianoKeyMap.MinPitch + 1;

        private readonly Transform[] _roots = new Transform[KeyCount];
        private readonly Vector3[] _min = new Vector3[KeyCount];
        private readonly Vector3[] _max = new Vector3[KeyCount];
        private readonly float[] _worldMinX = new float[KeyCount];
        private readonly float[] _worldMaxX = new float[KeyCount];
        private bool _built;

        /// <summary>是否已有琴键数据。</summary>
        public bool IsBuilt => _built;

        /// <summary>按琴键静止姿态建立包围盒（键根局部空间）与世界 X 范围（粗筛用）。</summary>
        public void Build(PianoKeyMap keyMap)
        {
            _built = false;
            if (keyMap == null) return;
            var corner = new Vector3[8];
            for (int i = 0; i < KeyCount; i++)
            {
                _roots[i] = null;
                if (!keyMap.TryGetKey(i + PianoKeyMap.MinPitch, out var root) || root == null) continue;

                bool any = false;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    string n = filter.gameObject.name;
                    if (filter.sharedMesh == null || (n != "Mesh" && n != "SideWall" && n != "BaseSupport")) continue;
                    var b = filter.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        corner[c] = new Vector3(
                            (c & 1) == 0 ? b.min.x : b.max.x,
                            (c & 2) == 0 ? b.min.y : b.max.y,
                            (c & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 local = root.InverseTransformPoint(filter.transform.TransformPoint(corner[c]));
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                        any = true;
                    }
                }
                if (!any) continue;

                _roots[i] = root;
                _min[i] = min;
                _max[i] = max;
                float wMin = float.PositiveInfinity, wMax = float.NegativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var p = new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y,
                        (c & 4) == 0 ? min.z : max.z);
                    float x = root.TransformPoint(p).x;
                    wMin = Mathf.Min(wMin, x);
                    wMax = Mathf.Max(wMax, x);
                }
                _worldMinX[i] = wMin;
                _worldMaxX[i] = wMax;
            }
            _built = true;
        }

        /// <summary>
        /// 一只手的最小余量（米，负 = 穿模深度）；无法测量时返回 +∞。
        /// <paramref name="playingOnly"/> 为 true 时只看已分配音符的演奏手指（休息手指由手指控制器自己抬开）。
        /// </summary>
        public float HandClearance(IFingerJointRig rig, FingerAssignmentService assignment, bool leftHand,
            bool playingOnly = false)
        {
            if (!_built || rig == null) return float.PositiveInfinity;
            float lowest = float.PositiveInfinity;
            float scale = HandScale(rig);
            for (int finger = 0; finger < 5; finger++)
            {
                int own = assignment != null ? assignment.GetPitch(leftHand, finger) : -1;
                if (playingOnly && own < 0) continue;
                lowest = Mathf.Min(lowest, FingerClearance(rig, finger, own, scale));
            }
            return lowest;
        }

        /// <summary>
        /// 末节中点：沿中节方向从末节关节延伸半个末节长。只用骨骼位置——导入角色的「指尖」是贴在指腹表面、
        /// 按接触点摆放的标记，不随末节骨刚性转动，用它取中点会把采样点压低几毫米并且不随抬指变化。
        /// </summary>
        private static Vector3 DistalMid(Vector3 pip, Vector3 dip, Transform tip)
        {
            Vector3 dir = dip - pip;
            float middle = dir.magnitude;
            if (middle < 1e-5f) return dip;
            float distalLength = tip != null ? Mathf.Min(Vector3.Distance(dip, tip.position), middle * 1.1f) : middle * 0.8f;
            return dip + dir / middle * (distalLength * 0.5f);
        }

        /// <summary>手的尺度：取食指长度相对成人的比例（儿童手更细），所有手指共用。</summary>
        public float HandScale(IFingerJointRig rig)
        {
            if (rig == null) return 1f;
            float indexLength = rig.GetFingerLength(1);
            return indexLength > 0.01f ? Mathf.Clamp(indexLength / AdultIndexLength, 0.7f, 1.2f) : 1f;
        }

        /// <summary>单根手指的最小余量（米）。<paramref name="own"/> 为该指正在按的键（末节与指尖不与它比较），−1 表示无。</summary>
        public float FingerClearance(IFingerJointRig rig, int finger, int own, float scale)
        {
            if (!_built || rig == null) return float.PositiveInfinity;
            if (!rig.TryGetJoints(finger, out var proximal, out var intermediate, out var distal)) return float.PositiveInfinity;
            if (proximal == null || intermediate == null || distal == null) return float.PositiveInfinity;
            var tip = rig.GetFingertip(finger);
            float r = (finger == 0 ? ThumbSkinRadius : FingerSkinRadius) * scale;

            Vector3 mcp = proximal.position, pip = intermediate.position, dip = distal.position;
            float lowest = PointClearance(mcp, r, -1);
            lowest = Mathf.Min(lowest, PointClearance((mcp + pip) * 0.5f, r, -1));
            lowest = Mathf.Min(lowest, PointClearance(pip, r, -1));
            lowest = Mathf.Min(lowest, PointClearance((pip + dip) * 0.5f, r, own));
            lowest = Mathf.Min(lowest, PointClearance(dip, r, own));
            lowest = Mathf.Min(lowest, PointClearance(DistalMid(pip, dip, tip), r * 0.85f, own));
            return lowest;
        }

        /// <summary>
        /// 诊断用：找出一只手最深的穿模点（手指、采样点序号 0～5、琴键音高、余量、点在键局部空间的坐标）。
        /// 会分配字符串，只在测试 / 诊断中调用。
        /// </summary>
        public string DescribeWorst(IFingerJointRig rig, FingerAssignmentService assignment, bool leftHand)
        {
            if (!_built || rig == null) return "无数据";
            float indexLength = rig.GetFingerLength(1);
            float scale = indexLength > 0.01f ? Mathf.Clamp(indexLength / AdultIndexLength, 0.7f, 1.2f) : 1f;
            float worst = float.PositiveInfinity;
            string text = "无穿模";
            for (int finger = 0; finger < 5; finger++)
            {
                if (!rig.TryGetJoints(finger, out var proximal, out var intermediate, out var distal)) continue;
                if (proximal == null || intermediate == null || distal == null) continue;
                var tip = rig.GetFingertip(finger);
                int own = assignment != null ? assignment.GetPitch(leftHand, finger) : -1;
                float r = (finger == 0 ? ThumbSkinRadius : FingerSkinRadius) * scale;
                Vector3 mcp = proximal.position, pip = intermediate.position, dip = distal.position;
                var points = new[] { mcp, (mcp + pip) * 0.5f, pip, (pip + dip) * 0.5f, dip, DistalMid(pip, dip, tip) };
                for (int k = 0; k < points.Length; k++)
                {
                    int exclude = k >= 3 ? own : -1;
                    float radius = k == 5 ? r * 0.85f : r;
                    for (int i = 0; i < KeyCount; i++)
                    {
                        var root = _roots[i];
                        if (root == null || i + PianoKeyMap.MinPitch == exclude) continue;
                        float c = PointClearance(points[k], radius, -1, i);
                        if (c < worst)
                        {
                            worst = c;
                            Vector3 local = root.InverseTransformPoint(points[k]);
                            text = $"指{finger} 点{k} 键{i + PianoKeyMap.MinPitch}{(PianoKeyMap.IsBlackKey(i + PianoKeyMap.MinPitch) ? "黑" : "白")} " +
                                   $"余量 {c * 1000f:0.0} mm 局部 ({local.x * 1000f:0},{local.y * 1000f:0},{local.z * 1000f:0}) " +
                                   $"盒 y[{_min[i].y * 1000f:0},{_max[i].y * 1000f:0}] z[{_min[i].z * 1000f:0},{_max[i].z * 1000f:0}] 自有键 {own}";
                        }
                    }
                }
            }
            return text;
        }

        /// <summary>单个皮肤球与附近所有琴键（排除 <paramref name="excludePitch"/>）的最小余量。</summary>
        public float PointClearance(Vector3 point, float radius, int excludePitch) => PointClearance(point, radius, excludePitch, -1);

        private float PointClearance(Vector3 point, float radius, int excludePitch, int onlyIndex)
        {
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < KeyCount; i++)
            {
                if (onlyIndex >= 0 && i != onlyIndex) continue;
                var root = _roots[i];
                if (root == null || i + PianoKeyMap.MinPitch == excludePitch) continue;
                if (point.x < _worldMinX[i] - radius - 0.002f || point.x > _worldMaxX[i] + radius + 0.002f) continue;

                Vector3 local = root.InverseTransformPoint(point);
                Vector3 min = _min[i], max = _max[i];
                Vector3 clamped = Vector3.Max(min, Vector3.Min(local, max));
                Vector3 delta = local - clamped;
                float clearance;
                if (delta.sqrMagnitude > 1e-12f)
                {
                    clearance = delta.magnitude - radius;
                }
                else
                {
                    // 球心已在盒内：余量 = −(半径 + 到最近表面的距离)
                    float inside = Mathf.Min(
                        Mathf.Min(local.x - min.x, max.x - local.x),
                        Mathf.Min(Mathf.Min(local.y - min.y, max.y - local.y), Mathf.Min(local.z - min.z, max.z - local.z)));
                    clearance = -(radius + inside);
                }
                if (clearance < lowest) lowest = clearance;
            }
            return lowest;
        }
    }
}
