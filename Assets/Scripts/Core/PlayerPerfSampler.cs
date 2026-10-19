using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using PianoTeacher3D.CameraControl;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 真实 Windows Player 的性能采样器（优化方案 P0-01 / §7-1 / §7-4）。
    /// <para>
    /// <b>为什么必须在 Player 里采样</b>：Editor 批处理（<c>-batchmode -nographics</c>）没有渲染负载，
    /// 帧时间只能说明逻辑层没被卡住，<b>不能</b>用来承诺 60 FPS。本采样器跟随 Development Build
    /// 一起运行真实图形管线，并把结果写成 JSON 供 <c>Tools/verify.py --player</c> 归档。
    /// </para>
    /// <para>
    /// <b>默认不生效</b>：只有命令行带 <c>-pt3dPerf &lt;秒数&gt;</c> 时才创建采样对象，
    /// 正常启动完全不受影响（零 Update、零分配）。
    /// </para>
    /// 用法：
    /// <code>
    /// PianoTeacher3D.exe -pt3dPerf 30 -pt3dPerfScenario play -pt3dPerfOut D:\perf\player.json
    /// </code>
    /// 场景口径：<c>play</c> = 加载示例曲后普通播放；<c>switch</c> = 播放中每 2 s 切换视角/角色。
    /// </summary>
    public static class PlayerPerfSampler
    {
        private const string SwitchArg = "-pt3dPerf";
        private const string ScenarioArg = "-pt3dPerfScenario";
        private const string OutputArg = "-pt3dPerfOut";

        /// <summary>预热时长（秒）：跳过首帧着色器编译、JIT 与场景装配。</summary>
        public const float WarmupSeconds = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (!TryReadSeconds(out float seconds)) return;

            string scenario = ReadArg(ScenarioArg) ?? "play";
            string output = ReadArg(OutputArg);
            if (string.IsNullOrEmpty(output))
                output = Path.Combine(Application.persistentDataPath, "perf_player.json");

            var go = new GameObject("PT3D_PerfSampler");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var sampler = go.AddComponent<Sampler>();
            sampler.Begin(seconds, scenario, output);
        }

        private static bool TryReadSeconds(out float seconds)
        {
            seconds = 0f;
            string raw = ReadArg(SwitchArg);
            if (raw == null) return false;
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out seconds)) return false;
            if (seconds < 1f) seconds = 1f;
            return true;
        }

        private static string ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length) return args[i + 1];
                return "";
            }
            return null;
        }

        /// <summary>采样结果（字段名与 <c>Tools/verify.py</c> 的解析一一对应）。</summary>
        [Serializable]
        public sealed class Result
        {
            public string unityVersion;
            public string platform;
            public string graphicsDevice;
            public string graphicsApi;
            public string operatingSystem;
            public string processor;
            public int processorCount;
            public int systemMemoryMb;
            public int screenWidth;
            public int screenHeight;
            public int vSyncCount;
            public int targetFrameRate;
            public string qualityTier;
            public string scenario;
            public float warmupSeconds;
            public float sampleSeconds;
            public int frameCount;
            public float meanMs;
            public float p50Ms;
            public float p95Ms;
            public float p99Ms;
            public float maxMs;
            public float avgFps;
            public float low1Fps;
            public float gcManagedMbStart;
            public float gcManagedMbEnd;
            public float gcAllocMb;
            public float monoUsedMb;
            public float totalAllocatedMb;
            public int meshCount;
            public int materialCount;
            public int audioClipCount;
            public int gameObjectCount;
            public string note;
        }

        private sealed class Sampler : MonoBehaviour
        {
            private float _seconds;
            private string _scenario;
            private string _output;

            private readonly List<float> _frames = new List<float>(8192);
            private long _gcStart;
            private bool _sampling;
            private int _nextSwitchFrame;
            private int _switchCount;
            private LessonSessionController _session;

            public void Begin(float seconds, string scenario, string output)
            {
                _seconds = seconds;
                _scenario = string.IsNullOrEmpty(scenario) ? "play" : scenario;
                _output = output;
                StartCoroutine(Run());
            }

            private System.Collections.IEnumerator Run()
            {
                // ---- 预热：等场景装配、首帧着色器与音频初始化稳定 ----
                yield return new WaitForSecondsRealtime(WarmupSeconds);

                _session = FindFirstObjectByType<LessonSessionController>();
                if (_session != null)
                {
                    if (_session.HasSong) _session.Play();
                    _session.SetCameraMode(CameraMode.Full);
                }

                _gcStart = GC.GetTotalMemory(false);
                _sampling = true;

                float end = Time.realtimeSinceStartup + _seconds;
                _nextSwitchFrame = 120;   // 约 2 s 后开始第一次切换

                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    if (!_sampling) continue;

                    _frames.Add(Time.unscaledDeltaTime);

                    if (string.Equals(_scenario, "switch", StringComparison.OrdinalIgnoreCase)
                        && _session != null && _frames.Count >= _nextSwitchFrame)
                    {
                        _nextSwitchFrame = _frames.Count + 120;
                        _switchCount++;
                        if (_switchCount % 2 == 0) _session.CycleAvatar();
                        else _session.CycleCameraMode();
                    }
                }

                _sampling = false;
                Write();
                Application.Quit();
            }

            private void Write()
            {
                var result = BuildResult();
                string json = JsonUtility.ToJson(result, true);

                try
                {
                    string dir = Path.GetDirectoryName(_output);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_output, json, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[PT3D][Perf] 写入采样结果失败：" + ex.GetType().Name);
                }

                Debug.Log("[PT3D][Perf] " + json);
            }

            private Result BuildResult()
            {
                var r = new Result
                {
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    graphicsDevice = SystemInfo.graphicsDeviceName,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    operatingSystem = SystemInfo.operatingSystem,
                    processor = SystemInfo.processorType,
                    processorCount = SystemInfo.processorCount,
                    systemMemoryMb = SystemInfo.systemMemorySize,
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    vSyncCount = QualitySettings.vSyncCount,
                    targetFrameRate = Application.targetFrameRate,
                    qualityTier = _session != null ? _session.CurrentQualityTier.ToString() : "Unknown",
                    scenario = _scenario,
                    warmupSeconds = WarmupSeconds,
                    sampleSeconds = _seconds,
                    frameCount = _frames.Count,
                    note = "Development Build + 真实图形设备；Editor 批处理数据不可与之混用。"
                };

                if (_frames.Count > 0)
                {
                    var sorted = new List<float>(_frames);
                    sorted.Sort();

                    double sum = 0;
                    for (int i = 0; i < sorted.Count; i++) sum += sorted[i];

                    r.meanMs = (float)(sum / sorted.Count * 1000.0);
                    r.p50Ms = Percentile(sorted, 0.50f) * 1000f;
                    r.p95Ms = Percentile(sorted, 0.95f) * 1000f;
                    r.p99Ms = Percentile(sorted, 0.99f) * 1000f;
                    r.maxMs = sorted[sorted.Count - 1] * 1000f;
                    r.avgFps = r.meanMs > 0f ? 1000f / r.meanMs : 0f;

                    int slowCount = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * 0.01f));
                    double slowSum = 0;
                    for (int i = sorted.Count - slowCount; i < sorted.Count; i++) slowSum += sorted[i];
                    r.low1Fps = slowSum > 0 ? (float)(slowCount / slowSum) : 0f;
                }

                r.gcManagedMbStart = _gcStart / (1024f * 1024f);
                long gcEnd = GC.GetTotalMemory(false);
                r.gcManagedMbEnd = gcEnd / (1024f * 1024f);
                r.gcAllocMb = r.gcManagedMbEnd - r.gcManagedMbStart;
                r.monoUsedMb = Profiler.GetMonoUsedSizeLong() / (1024f * 1024f);
                r.totalAllocatedMb = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

                r.meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                r.materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
                r.audioClipCount = Resources.FindObjectsOfTypeAll<AudioClip>().Length;
                r.gameObjectCount = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
                return r;
            }

            private static float Percentile(List<float> sorted, float p)
            {
                if (sorted.Count == 0) return 0f;
                int index = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
                return sorted[index];
            }
        }
    }
}
