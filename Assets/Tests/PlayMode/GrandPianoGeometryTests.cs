using System.Collections;
using System.Linq;
using NUnit.Framework;
using PianoTeacher3D.Piano;
using UnityEngine;
using UnityEngine.TestTools;

namespace PianoTeacher3D.Tests
{
    public class GrandPianoGeometryTests
    {
        [UnityTest]
        public IEnumerator Grand_PreservesKeyboardAndProvidesMovingPedalContact()
        {
            var piano = new GameObject("GrandPianoTest");
            // 本用例验证保留的低轮廓三角琴外壳；2026-11-18 起默认外壳是竖琴钢琴（见 Harp_* 用例）
            bool harp = PianoKeyMap.UseHarpCase;
            PianoKeyMap.UseHarpCase = false;
            try
            {
                var keys = piano.AddComponent<PianoKeyMap>();
                keys.BuildKeys();
                Assert.AreEqual(88, keys.KeyRoots.Count);
                Assert.That(keys.TotalWidth, Is.EqualTo(1.248f).Within(0.001f));
                for (int pitch = PianoKeyMap.MinPitch; pitch <= PianoKeyMap.MaxPitch; pitch++)
                {
                    Assert.IsTrue(keys.TryGetKey(pitch, out var key), $"缺少音高 {pitch} 的独立按键");
                    Assert.That(key.localPosition.y, Is.EqualTo(keys.KeybedHeight).Within(0.0001f));
                }

                var caseRoot = piano.transform.Find("PianoCase");
                Assert.IsNotNull(caseRoot.Find("GrandRim"), "三角琴琴壳应呈连续弧线");
                var lid = caseRoot.Find("Lid");
                Assert.IsNotNull(lid);
                Assert.Greater(lid.GetComponent<Renderer>().bounds.size.y, 0.35f, "琴盖应打开");
                Assert.Less(lid.GetComponent<Renderer>().bounds.max.y, 1.47f, "打开的琴盖应保持低轮廓");
                var lidMesh = lid.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(System.Array.Exists(lidMesh.vertices,
                    vertex => vertex.y > 0.001f && vertex.y < lidMesh.bounds.max.y - 0.001f), "琴盖边缘应有实体圆角");
                Assert.IsNotNull(caseRoot.Find("CastFrame"), "琴弦上方需要有实体铸框");
                var leg = caseRoot.Find("Leg_L").GetComponent<MeshFilter>().sharedMesh;
                Assert.That(LegHalfWidth(leg, 0.2f), Is.LessThan(LegHalfWidth(leg, 0.8f) * 0.8f));
                Assert.IsNotNull(caseRoot.Find("Leg_Tail"));
                Assert.IsNull(caseRoot.Find("LowerCase"), "键盘下方应留有演奏者膝部空间");

                Transform hinge = caseRoot.Find("Pedal_R");
                Assert.IsNotNull(hinge);
                var contact = hinge.Find("Contact");
                Assert.IsNotNull(contact, "延音踏板需要一个随铰链运动的鞋掌接触点");
                Assert.Less(hinge.localPosition.x, keys.TotalWidth * 0.5f, "角色面朝 -Z，右脚在 -X 侧");
                float initialHeight = contact.position.y;
                hinge.localRotation = Quaternion.Euler(9f, 0f, 0f);
                Assert.Greater(initialHeight - contact.position.y, 0.012f, "正向旋转应下压接触点");

                keys.TryGetKey(60, out var middleC);
                keys.SetUnderwaterAppearance(true, legacy: true);
                var arch = caseRoot.Find("UnderwaterPiano");
                Assert.IsNotNull(arch);
                Assert.IsTrue(arch.gameObject.activeSelf);
                Assert.IsFalse(lid.gameObject.activeSelf);
                Assert.IsFalse(caseRoot.Find("Leg_L_Caster").gameObject.activeSelf);
                Assert.IsNotNull(arch.Find("TidalVerticalStrings"));
                Assert.AreSame(contact, keys.RightPedalContact);
                Assert.AreSame(middleC, keys.KeyRoots[60]);
                keys.SetUnderwaterAppearance(false);
                Assert.IsTrue(lid.gameObject.activeSelf);
                Assert.IsFalse(arch.gameObject.activeSelf);

                var locations = keys.KeyRoots.ToDictionary(pair => pair.Key,
                    pair => (root: pair.Value, position: pair.Value.localPosition,
                        rotation: pair.Value.localRotation));
                var pedalPosition = contact.position;
                keys.SetUnderwaterAppearance(true);
                var cinematic = caseRoot.Find("CinematicPiano");
                Assert.IsNotNull(cinematic, "展示应加载独立琴壳");
                Assert.IsTrue(cinematic.gameObject.activeSelf);
                Assert.IsFalse(arch.gameObject.activeSelf);
                Assert.IsFalse(caseRoot.Find("GrandRim").gameObject.activeSelf);
                var shell = cinematic.Find("TideShell");
                Assert.IsNotNull(shell, "舞台琴用完整的渐弯琴壳围住琴尾");
                var shellBounds = shell.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.That(shellBounds.max.y - keys.KeySurfaceY, Is.InRange(0.08f, 0.17f),
                    "琴壳需要保留可见的体积，同时不遮住演奏侧的手");
                Assert.Less(shellBounds.max.z, -0.095f, "上壳只能从键盘后方开始");
                Assert.Greater(shellBounds.size.z, 1.20f, "深琴尾使侧面保留三角琴比例");
                var keyframe = cinematic.Find("Keyframe").GetComponent<Renderer>().bounds;
                Assert.LessOrEqual(keyframe.max.y, keys.KeybedHeight, "键盘托不能盖住可按动的琴键");
                Assert.IsNotNull(cinematic.Find("TideInlay"), "琴壳侧沿应保留一条细镶边");
                var legs = cinematic.Find("TaperedLegs").GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.That(legs.min.y, Is.InRange(0f, 0.015f), "琴腿应落到舞台地面");
                Assert.IsNull(cinematic.Find("ArchFrame"), "新的低轮廓琴体不应生成高架弧梁");
                foreach (var kv in locations)
                {
                    var keyRoot = keys.KeyRoots[kv.Key];
                    Assert.AreSame(kv.Value.root, keyRoot);
                    Assert.AreEqual(kv.Value.position, keyRoot.localPosition);
                    Assert.AreEqual(kv.Value.rotation, keyRoot.localRotation);
                }
                Assert.AreEqual(pedalPosition, contact.position);
                Assert.AreSame(contact, keys.RightPedalContact);
                keys.SetUnderwaterAppearance(true, legacy: true);
                Assert.IsTrue(arch.gameObject.activeSelf);
                Assert.IsFalse(cinematic.gameObject.activeSelf);
                Assert.IsTrue(caseRoot.Find("GrandRim").gameObject.activeSelf);
                keys.SetUnderwaterAppearance(false);
                Assert.IsTrue(lid.gameObject.activeSelf);
                Assert.IsFalse(cinematic.gameObject.activeSelf);
            }
            finally
            {
                PianoKeyMap.UseHarpCase = harp;
                Object.Destroy(piano);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Harp_KeepsKeyboardStringsPerKeyAndLegsOnFloor()
        {
            var piano = new GameObject("HarpPianoTest");
            bool harp = PianoKeyMap.UseHarpCase;
            PianoKeyMap.UseHarpCase = true;
            try
            {
                var keys = piano.AddComponent<PianoKeyMap>();
                keys.BuildKeys();
                var before = keys.KeyRoots.ToDictionary(p => p.Key, p => p.Value.localPosition);
                keys.SetUnderwaterAppearance(true);
                var shell = piano.transform.Find("PianoCase/CinematicPiano");
                Assert.IsNotNull(shell);
                var body = shell.Find("HarpBody").GetComponent<MeshFilter>().sharedMesh.bounds;
                var strings = shell.Find("HarpStrings");
                Assert.IsNotNull(strings, "竖琴钢琴应有外露的琴弦");
                Assert.IsNotNull(strings.GetComponent<HarpStringVibration>(), "琴弦应能随按键振动");
                var uvs = new System.Collections.Generic.List<Vector2>();
                strings.GetComponent<MeshFilter>().sharedMesh.GetUVs(0, uvs);
                Assert.AreEqual(PianoKeyMap.KeyCount, uvs.Select(uv => Mathf.RoundToInt(uv.x)).Distinct().Count(), "88 根弦，每键一根");
                Assert.That(body.min.y, Is.InRange(-0.003f, 0.012f), "涡卷腿应落到舞台地面");
                Assert.Greater(body.max.y, 1.6f, "立柱与琴颈要高出演奏者头部");
                Assert.Less(shell.Find("HarpStrings").GetComponent<MeshFilter>().sharedMesh.bounds.max.z, -0.08f,
                    "琴弦在键盘后方，不挡手");
                foreach (var kv in before)
                    Assert.AreEqual(kv.Value, keys.KeyRoots[kv.Key].localPosition, "换琴壳不能移动琴键");
                Assert.IsNotNull(keys.RightPedalContact, "踏板接触点保留");
            }
            finally
            {
                PianoKeyMap.UseHarpCase = harp;
                Object.Destroy(piano);
            }
            yield return null;
        }

        private static float LegHalfWidth(Mesh mesh, float heightFraction)
        {
            float nearY = Mathf.Lerp(mesh.bounds.min.y, mesh.bounds.max.y, heightFraction);
            float tolerance = mesh.bounds.size.y * 0.23f;
            float width = 0f;
            foreach (var vertex in mesh.vertices)
                if (Mathf.Abs(vertex.y - nearY) < tolerance) width = Mathf.Max(width, Mathf.Abs(vertex.x));
            Assert.Greater(width, 0f, "琴腿剖面应有有效顶点");
            return width;
        }

        [UnityTest]
        public IEnumerator UnderwaterAnimals_MoveIndependentlyAndFollowEnvironmentVisibility()
        {
            var root = new GameObject("StageAnimationTest");
            try
            {
                var stage = root.AddComponent<PianoSceneEnvironment>();
                stage.Build(null, Core.QualityProfile.For(Core.QualityTier.Standard));
                var habitat = root.transform.Find("SceneEnvironment/UnderwaterHabitat");
                Assert.IsNotNull(habitat);
                var whale = habitat.Find("HumpbackWhale");
                var near = habitat.Find("FishShoal1");
                var far = habitat.Find("FishShoal2");
                Assert.IsNotNull(habitat.Find("SunlightVolume"));
                Assert.IsNotNull(habitat.Find("CoralGarden"));
                Assert.Greater(whale.GetComponent<MeshFilter>().sharedMesh.bounds.size.z, 2f);
                Assert.Greater(Vector3.Distance(near.position, far.position), 3f);
                Vector3 start = whale.position;
                Vector3 nearStart = near.position;
                Vector3 farStart = far.position;
                yield return new WaitForSecondsRealtime(0.40f);
                Assert.Greater(Vector3.Distance(whale.position, start), 0.005f);
                Assert.Greater(Vector3.Distance(near.position - nearStart, far.position - farStart), 0.01f);
                stage.SetEnvironmentVisible(false);
                Assert.IsFalse(habitat.gameObject.activeInHierarchy);
                stage.SetEnvironmentVisible(true);
                Assert.IsTrue(habitat.gameObject.activeInHierarchy);
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
    }
}
