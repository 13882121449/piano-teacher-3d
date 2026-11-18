using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 海底氛围（2026-11-18，参考图二）：
    /// ① 舞台外圈海床冒出的气泡流；② 琴弦被拨响时沿弦升起的小气泡；③ 缓慢下沉的海雪；
    /// ④ 程序合成的水下环境声（低频涌动 + 零星气泡声 + 远处鲸鸣）；⑤ 覆盖整个场景的混响区，
    /// 给钢琴加一层音乐厅式的尾音；⑥ 对外提供 <see cref="MusicEnergy"/>，鱼群 / 水母 / 鲸鱼据此律动。
    /// <para>粒子形状由 <c>PianoTeacher3D/OceanParticle</c> 着色器按 UV 计算，不需要贴图。</para>
    /// </summary>
    public sealed class OceanAmbience : MonoBehaviour
    {
        /// <summary>音乐能量 0–1：按下的键越多、起音越密越高；起音快、回落约 1.2 s。</summary>
        public static float MusicEnergy { get; private set; }

        /// <summary>环境声音量（0–1）。</summary>
        public static float AmbienceVolume = 0.22f;

        private ParticleSystem _vents;
        private ParticleSystem _noteBubbles;
        private ParticleSystem _snow;
        private Material _bubbleMaterial;
        private Material _snowMaterial;
        private AudioSource _ambience;
        private AudioClip _clip;
        private PianoKeyAnimator _keys;
        private Transform _piano;
        private int _lastPressed;
        private float _onsets;

        /// <summary>本帧累计发出的弦上气泡数（诊断 / 测试用）。</summary>
        public int NoteBubbleCount { get; private set; }

        private void Start()
        {
            var shader = Shader.Find("PianoTeacher3D/OceanParticle");
            if (shader != null)
            {
                _bubbleMaterial = new Material(shader) { name = "海底气泡" };
                _bubbleMaterial.SetColor("_Tint", new Color(0.74f, 0.95f, 1f));
                _bubbleMaterial.SetFloat("_Mode", 0f);
                _bubbleMaterial.SetFloat("_Intensity", 1.15f);
                _snowMaterial = new Material(shader) { name = "海雪" };
                _snowMaterial.SetColor("_Tint", new Color(0.62f, 0.82f, 0.80f));
                _snowMaterial.SetFloat("_Mode", 1f);
                _snowMaterial.SetFloat("_Intensity", 0.75f);
                BuildParticles();
            }
            BuildAudio();
            HarpStringVibration.StringStruck += OnStringStruck;
            SetVisible(_visible);
        }

        /// <summary>随环境开关整体启停：粒子节点与环境声一起隐藏 / 恢复。</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.GetComponent<ParticleSystem>() != null) child.gameObject.SetActive(visible);
            }
            if (_ambience != null) _ambience.mute = !visible;
        }

        private bool _visible = true;

        private void OnDestroy()
        {
            HarpStringVibration.StringStruck -= OnStringStruck;
            MusicEnergy = 0f;
            Release(_bubbleMaterial);
            Release(_snowMaterial);
            Release(_clip);
        }

        private void Update()
        {
            if (_keys == null) _keys = FindFirstObjectByType<PianoKeyAnimator>();
            if (_piano == null)
            {
                var map = FindFirstObjectByType<PianoKeyMap>();
                if (map != null)
                {
                    _piano = map.transform;
                    PlaceAroundPiano();
                }
            }

            int pressed = _keys != null ? _keys.PressedKeyCount : 0;
            if (pressed > _lastPressed) _onsets += pressed - _lastPressed;
            _lastPressed = pressed;
            float dt = Time.deltaTime;
            _onsets *= Mathf.Exp(-dt / 0.8f);
            float target = Mathf.Clamp01(pressed * 0.10f + _onsets * 0.09f);
            float tau = target > MusicEnergy ? 0.12f : 1.2f;
            MusicEnergy = Mathf.Lerp(MusicEnergy, target, 1f - Mathf.Exp(-dt / tau));

            if (_ambience != null) _ambience.volume = AmbienceVolume;
            if (_vents != null)
            {
                var emission = _vents.emission;
                emission.rateOverTime = 5f + MusicEnergy * 9f;
            }
        }

        private void OnStringStruck(Vector3 bottom, Vector3 top, float strength)
        {
            if (_noteBubbles == null) return;
            int count = 2 + Mathf.RoundToInt(strength * 3f);
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = Vector3.Lerp(bottom, top, Random.Range(0.02f, 0.30f)) + new Vector3(Random.Range(-0.004f, 0.004f), 0f, Random.Range(0.004f, 0.016f));
                p.velocity = new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(0.22f, 0.40f), Random.Range(-0.01f, 0.03f));
                p.startSize = Random.Range(0.006f, 0.015f);
                p.startLifetime = Random.Range(2.0f, 3.2f);
                p.startColor = new Color(1f, 1f, 1f, Random.Range(0.6f, 0.95f));
                _noteBubbles.Emit(p, 1);
            }
            NoteBubbleCount += count;
        }

        // ==================== 粒子 ====================

        private void BuildParticles()
        {
            _vents = CreateSystem("BubbleVents", _bubbleMaterial, 260);
            {
                var main = _vents.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.04f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.042f);
                main.gravityModifier = -0.015f;
                var shape = _vents.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 3.6f;
                shape.radiusThickness = 0.35f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var emission = _vents.emission;
                emission.rateOverTime = 5f;
                // 圆环形状只在环面内给初速，上升速度由 Velocity over Lifetime 提供（世界 +Y）
                var velocity = _vents.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(0.24f, 0.42f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                AddWobble(_vents, 0.12f);
                FadeInOut(_vents);
            }

            _noteBubbles = CreateSystem("NoteBubbles", _bubbleMaterial, 400);
            {
                var main = _noteBubbles.main;
                main.gravityModifier = -0.01f;
                var emission = _noteBubbles.emission;
                emission.rateOverTime = 0f;
                AddWobble(_noteBubbles, 0.05f);
                FadeInOut(_noteBubbles);
            }

            _snow = CreateSystem("MarineSnow", _snowMaterial, 420);
            {
                var main = _snow.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.0f, 0.02f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.018f);
                main.gravityModifier = 0.0025f;
                main.prewarm = true;
                var shape = _snow.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(14f, 6f, 14f);
                var emission = _snow.emission;
                emission.rateOverTime = 26f;
                AddWobble(_snow, 0.04f);
                FadeInOut(_snow);
            }
        }

        /// <summary>找到钢琴后把气泡环与海雪盒放到舞台周围（钢琴还没建好时先放在本节点）。</summary>
        private void PlaceAroundPiano()
        {
            Vector3 center = _piano.TransformPoint(new Vector3(0.62f, 0f, 0.2f));
            if (_vents != null) _vents.transform.position = center + Vector3.down * 0.25f;
            if (_snow != null) _snow.transform.position = center + Vector3.up * 2.6f;
        }

        private ParticleSystem CreateSystem(string name, Material material, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = true;
            main.loop = true;
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 10f;
            ps.Play();
            return ps;
        }

        private static void AddWobble(ParticleSystem ps, float strength)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.25f;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        private static void FadeInOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
        }

        // ==================== 声音 ====================

        private void BuildAudio()
        {
            _clip = SynthesizeAmbience(44100, 12f, 20261118);
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.clip = _clip;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f;
            _ambience.volume = AmbienceVolume;
            _ambience.reverbZoneMix = 0.3f;
            _ambience.Play();

            var zone = gameObject.AddComponent<AudioReverbZone>();
            zone.minDistance = 40f;
            zone.maxDistance = 80f;
            zone.reverbPreset = AudioReverbPreset.Concerthall;
            // 湿声略收、高频略暗：音乐厅的尾音带一点水下的柔和感
            zone.room = -1300;
            zone.roomHF = -900;
        }

        /// <summary>
        /// 合成可无缝循环的水下环境声：低通褐噪声（涌动）+ 慢速起伏 + 随机气泡啁啾 + 一声远处鲸鸣。
        /// 首尾 0.5 s 交叉淡化，循环无接缝。
        /// </summary>
        internal static AudioClip SynthesizeAmbience(int rate, float seconds, int seed)
        {
            int n = Mathf.RoundToInt(rate * seconds);
            int fade = rate / 2;
            var data = new float[n + fade];
            var random = new System.Random(seed);
            double brown = 0, low = 0;
            for (int i = 0; i < data.Length; i++)
            {
                double white = random.NextDouble() * 2.0 - 1.0;
                brown = (brown + white * 0.02) * 0.998;
                low += (brown - low) * 0.02;
                float t = i / (float)rate;
                float swell = 0.62f + 0.38f * Mathf.Sin(t * Mathf.PI * 2f / 6f);
                data[i] = (float)low * 3.2f * swell;
            }

            int bubbles = Mathf.RoundToInt(seconds * 1.3f);
            for (int b = 0; b < bubbles; b++)
            {
                int start = random.Next(0, n);
                float f0 = 480f + (float)random.NextDouble() * 620f;
                float amp = 0.05f + (float)random.NextDouble() * 0.09f;
                int len = rate / 12;
                double phase = 0;
                for (int k = 0; k < len && start + k < data.Length; k++)
                {
                    float tt = k / (float)rate;
                    float f = f0 * (1f + 0.6f * tt / 0.04f);
                    phase += 2.0 * Mathf.PI * f / rate;
                    data[start + k] += amp * Mathf.Exp(-tt / 0.022f) * (float)System.Math.Sin(phase);
                }
            }

            {
                int start = (int)(rate * 3.5f);
                int len = (int)(rate * 1.6f);
                double phase = 0;
                for (int k = 0; k < len; k++)
                {
                    float tt = k / (float)rate;
                    float f = Mathf.Lerp(190f, 118f, tt / 1.6f) * (1f + 0.012f * Mathf.Sin(tt * 2f * Mathf.PI * 5f));
                    phase += 2.0 * Mathf.PI * f / rate;
                    float env = Mathf.Sin(Mathf.Clamp01(tt / 1.6f) * Mathf.PI);
                    data[start + k] += 0.045f * env * (float)(System.Math.Sin(phase) + 0.35 * System.Math.Sin(phase * 2.0));
                }
            }

            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = data[i];
                if (i < fade)
                {
                    float w = i / (float)fade;
                    v = data[i] * w + data[n + i] * (1f - w);
                }
                loop[i] = v;
            }
            float peak = 1e-4f;
            foreach (float v in loop) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = 0.5f / peak;
            for (int i = 0; i < n; i++) loop[i] *= gain;

            var clip = AudioClip.Create("OceanAmbience", n, 1, rate, false);
            clip.SetData(loop, 0);
            return clip;
        }

        private static void Release(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
