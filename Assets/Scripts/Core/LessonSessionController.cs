using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;
using PianoTeacher3D.Avatar;
using PianoTeacher3D.CameraControl;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 课程会话控制器（方案 4.2）：维护课程状态（当前曲目、播放状态、速度），
    /// 并作为场景装配门面，在运行时程序化搭建钢琴/人物/摄像机/UI 并完成模块连线。
    /// 播放状态与时间的唯一来源是 <see cref="LessonTimeline"/>。
    /// </summary>
    public class LessonSessionController : MonoBehaviour
    {
        private LessonTimeline _timeline;
        private MidiEventScheduler _scheduler;
        private PianoKeyMap _keyMap;
        private PianoKeyAnimator _keyAnimator;
        private PianoAudioSynth _audio;
        private AvatarManager _avatar;
        private HandIKController _handIK;
        private FingerPoseController _fingerPose;
        private CameraModeController _camera;

        private MidiParseResult _currentSong;
        private PracticeHand _practiceHand = PracticeHand.Both;
        private float _lastTime; // 用于检测 Loop 跳转导致的时间回退

        private PianoKeyHighlighter _highlighter;
        private Metronome _metronome;
        private SongAnalysis _analysis = new SongAnalysis();
        private readonly List<MidiNoteEvent> _upcomingBuffer = new();
        private float _highlightLookAhead = 0.6f;

        public bool HasSong => _currentSong != null && _currentSong.Success;
        public string CurrentSongName { get; private set; } = "";
        public PlaybackState PlaybackState => _timeline != null ? _timeline.State : PlaybackState.Stopped;
        public float PlaybackSpeed => _timeline != null ? _timeline.PlaybackSpeed : 1f;
        public CameraMode CameraMode => _camera != null ? _camera.Mode : CameraMode.Full;
        public PracticeHand PracticeHand => _practiceHand;
        public bool IsLooping => _timeline != null && _timeline.IsLooping;
        public int CurrentAvatarIndex => _avatar != null ? _avatar.CurrentIndex : 0;

        /// <summary>当前琴键高亮模式。</summary>
        public HighlightMode KeyHighlight => _highlighter != null ? _highlighter.Mode : HighlightMode.Off;

        /// <summary>节拍器是否开启。</summary>
        public bool MetronomeEnabled => _metronome != null && _metronome.Enabled;

        /// <summary>每小节拍数（默认 4/4）。</summary>
        public int BeatsPerBar => _metronome != null ? _metronome.BeatsPerBar : 4;

        /// <summary>当前曲目分析结果（未加载曲目时为空分析，非 null）。</summary>
        public SongAnalysis Analysis => _analysis;

        /// <summary>琴键预提示提前量（秒），clamp 到 [0.05, 3]。</summary>
        public float HighlightLookAhead
        {
            get => _highlightLookAhead;
            set => _highlightLookAhead = Mathf.Clamp(value, 0.05f, 3f);
        }

        // 供 UI 订阅
        public event System.Action<string> OnError;
        public event System.Action OnSongLoaded;
        public event System.Action<PlaybackState> OnPlaybackStateChanged;
        public event System.Action<float, float> OnProgressChanged; // current, total
        public event System.Action<int> OnAvatarChanged;
        public event System.Action<CameraMode> OnCameraModeChanged;
        public event System.Action<PracticeHand> OnPracticeHandChanged;
        public event System.Action<HighlightMode> OnHighlightModeChanged;
        public event System.Action<bool> OnMetronomeToggled;
        public event System.Action<int> OnMetronomeBeat;          // 参数：小节内拍序号（0 = 强拍）
        public event System.Action<SongAnalysis> OnAnalysisReady;

        private void Awake()
        {
            BuildScene();
        }

        private void Start()
        {
            LoadFirstSong();
        }

        private void Update()
        {
            if (_timeline == null || _scheduler == null) return;

            float current = _timeline.CurrentTime;
            if (current < _lastTime - 0.001f)
            {
                // Loop 跳转导致时间回退：清空后重建 seek 点处的按键/手部状态
                ResetPlaybackState();
                _scheduler.SeekTo(current);
            }
            else
            {
                _scheduler.Tick(current);
            }
            _lastTime = current;

            UpdateHighlightUpcoming(current);
            _metronome?.Tick(current);

            OnProgressChanged?.Invoke(current, _timeline.TotalDuration);
        }

        /// <summary>
        /// 每帧把预提示窗口 [当前时间, 当前时间 + 提前量) 内的音符推送给高亮器。
        /// 分手练习时只提示当前练习手，保持与琴键动画/手部动作一致。
        /// </summary>
        private void UpdateHighlightUpcoming(float current)
        {
            if (_highlighter == null) return;

            // 停止状态：不显示任何琴键提示（Stop 语义为完全复位，避免"已停止但仍亮着"的误导）
            if (_timeline == null || _timeline.State == PlaybackState.Stopped)
            {
                if (_highlighter.HighlightedKeyCount > 0 || _highlighter.UpcomingNoteCount > 0)
                    _highlighter.ClearAll();
                return;
            }

            var mode = _highlighter.Mode;
            if (mode != HighlightMode.Upcoming && mode != HighlightMode.Both) return;

            _scheduler.CollectNotesInRange(current, current + _highlightLookAhead, _upcomingBuffer);

            if (_practiceHand != PracticeHand.Both)
            {
                bool wantLeft = _practiceHand == PracticeHand.LeftOnly;
                for (int i = _upcomingBuffer.Count - 1; i >= 0; i--)
                {
                    if (PianoNoteFilter.IsLeftHand(_upcomingBuffer[i].Pitch) != wantLeft)
                        _upcomingBuffer.RemoveAt(i);
                }
            }

            _highlighter.RefreshUpcoming(_upcomingBuffer);
        }

        // ==================== 场景装配 ====================

        private void BuildScene()
        {
            // 1. 统一时间轴
            _timeline = gameObject.AddComponent<LessonTimeline>();
            _timeline.OnStateChanged += s => OnPlaybackStateChanged?.Invoke(s);

            // 2. MIDI 事件调度器
            _scheduler = gameObject.AddComponent<MidiEventScheduler>();

            // 3. 钢琴（真实比例 88 键 + 立式琴体，居中于 X）
            var piano = new GameObject("Piano");
            piano.transform.SetParent(transform, false);
            _keyMap = piano.AddComponent<PianoKeyMap>();
            _keyMap.BuildKeys();
            piano.transform.localPosition = new Vector3(-_keyMap.TotalWidth * 0.5f, 0f, 0f);

            // 3.1 地面（深色地板，界定场景空间）
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.03f, 0.2f);
            floor.transform.localScale = new Vector3(6f, 0.06f, 6f);
            Destroy(floor.GetComponent<Collider>());
            var floorShader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (floorShader != null)
            {
                floor.GetComponent<Renderer>().sharedMaterial =
                    new Material(floorShader) { color = new Color(0.105f, 0.115f, 0.135f) };
            }

            // 4. 琴键动画 + 音频
            _keyAnimator = gameObject.AddComponent<PianoKeyAnimator>();
            _keyAnimator.Initialize(_keyMap);
            _audio = gameObject.AddComponent<PianoAudioSynth>();
            _audio.Initialize();

            // 4.1 琴键高亮提示（方案 2.2）：与下压动画解耦，只改颜色
            _highlighter = gameObject.AddComponent<PianoKeyHighlighter>();
            _highlighter.Initialize(_keyMap);

            // 4.2 节拍器（方案 2.2）：由统一时间轴驱动，不自建计时器
            _metronome = gameObject.AddComponent<Metronome>();
            _metronome.Initialize();
            _metronome.OnBeat += beat => OnMetronomeBeat?.Invoke(beat);

            // 5. 人物（3 预设角色，坐于键盘前方 +Z 侧，面向 -Z 朝钢琴）
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.transform.SetParent(transform, false);
            avatarRoot.transform.localPosition = new Vector3(0f, 0f, 0.42f);
            avatarRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _avatar = avatarRoot.AddComponent<AvatarManager>();
            _avatar.BuildAvatars();

            // 6. 手部 IK + 手指姿态
            _handIK = gameObject.AddComponent<HandIKController>();
            _handIK.Initialize(_avatar, _keyMap);
            _fingerPose = gameObject.AddComponent<FingerPoseController>();
            _fingerPose.Initialize(_avatar.LeftHand, _avatar.RightHand);

            // 7. 摄像机控制 + 环境氛围
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PianoTeacher3D.UI.UITheme.SceneBg;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.47f);
            _camera = gameObject.AddComponent<CameraModeController>();
            _camera.Initialize(cam, _avatar);

            // 8. 灯光（主方向光 + 键盘补光）
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Directional Light");
                light = lightGo.AddComponent<Light>();
            }
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.90f); // 微暖，配合暖金主题
            light.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            // 键盘上方点光：提升琴键与手部的可读性
            var keyLightGo = new GameObject("KeyLight");
            keyLightGo.transform.SetParent(transform, false);
            var keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Point;
            keyLight.range = 2.4f;
            keyLight.intensity = 1.1f;
            keyLight.color = new Color(1f, 0.97f, 0.92f);
            keyLightGo.transform.localPosition = new Vector3(0f, 1.65f, 0.35f);

            // 9. UI
            var controlsUI = gameObject.AddComponent<PianoTeacher3D.UI.LessonControlsUI>();
            controlsUI.Initialize(this);
            var avatarUI = gameObject.AddComponent<PianoTeacher3D.UI.AvatarSelectionUI>();
            avatarUI.Initialize(this);

            // 10. 事件连线（琴键/音频/IK 订阅音符事件）
            _scheduler.OnNoteOn += _keyAnimator.OnNoteOn;
            _scheduler.OnNoteOn += _audio.OnNoteOn;
            _scheduler.OnNoteOn += _handIK.OnNoteOn;
            _scheduler.OnNoteOn += _fingerPose.OnNoteOn;
            _scheduler.OnNoteOn += _highlighter.OnNoteOn;
            _scheduler.OnNoteOff += _keyAnimator.OnNoteOff;
            _scheduler.OnNoteOff += _handIK.OnNoteOff;
            _scheduler.OnNoteOff += _fingerPose.OnNoteOff;
            _scheduler.OnNoteOff += _highlighter.OnNoteOff;
        }

        // ==================== 曲目加载 ====================

        private void LoadFirstSong()
        {
            // 主菜单传入的选中曲目优先
            string selected = PianoTeacher3D.UI.MainMenuUI.SelectedSongPath;
            if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
            {
                LoadSong(selected);
                return;
            }

            string dir = Path.Combine(Application.streamingAssetsPath, "Songs");
            if (!Directory.Exists(dir))
            {
                OnError?.Invoke("未找到曲目目录：StreamingAssets/Songs");
                return;
            }
            var files = Directory.GetFiles(dir, "*.mid");
            if (files.Length == 0)
            {
                OnError?.Invoke("StreamingAssets/Songs 下没有 .mid 曲目");
                return;
            }
            LoadSong(files[0]);
        }

        /// <summary>加载指定 MIDI 曲目（解析失败走 OnError，不抛异常）。</summary>
        public void LoadSong(string path)
        {
            var result = MidiParser.ParseFile(path);
            if (!result.Success)
            {
                OnError?.Invoke($"MIDI 解析失败：{result.Error}");
                return;
            }
            if (result.Notes.Count == 0)
            {
                OnError?.Invoke("曲目不含音符事件");
                return;
            }

            _currentSong = result;
            CurrentSongName = Path.GetFileNameWithoutExtension(path);
            _timeline.SetDuration(result.TotalDurationSeconds);
            _timeline.ClearLoop();
            _practiceHand = PracticeHand.Both;
            _lastTime = 0f;
            _scheduler.Load(result.Notes);

            // 节拍器拍点表 + 曲目分析（方案 2.2 节拍器 / 练习反馈）
            _metronome?.Load(result.TempoChanges, result.TotalDurationSeconds);
            _analysis = SongAnalyzer.Analyze(result.Notes, result.TempoChanges, result.TotalDurationSeconds);

            ResetPlayback();
            OnSongLoaded?.Invoke();
            OnAnalysisReady?.Invoke(_analysis);
        }

        // ==================== 播放控制（转发到统一时间轴） ====================

        public void PlayPause()
        {
            if (_timeline == null) return;
            if (_timeline.State == PlaybackState.Playing) { _timeline.Pause(); return; }
            if (!RequireSong("播放")) return;
            _timeline.Play();
        }

        public void Play()
        {
            if (!RequireSong("播放")) return;
            _timeline?.Play();
        }

        public void Pause() => _timeline?.Pause();

        public void Stop()
        {
            _timeline?.Stop();
            ResetPlaybackState();
        }

        public void Restart()
        {
            if (!RequireSong("重播")) return;
            _timeline?.Restart();
            _scheduler.SeekTo(0f);
            ResetPlaybackState();
        }

        public void Seek(float seconds)
        {
            if (_timeline == null || _scheduler == null) return;
            _timeline.Seek(seconds);
            ResetPlaybackState();
            _scheduler.SeekTo(seconds);
        }

        public void SeekNormalized(float t01)
        {
            if (_timeline == null) return;
            Seek(Mathf.Clamp01(t01) * _timeline.TotalDuration);
        }

        public void SetSpeed(float speed)
        {
            if (_timeline != null) _timeline.PlaybackSpeed = Mathf.Max(0.1f, speed);
        }

        public void CycleSpeed()
        {
            float[] speeds = { 0.5f, 1f, 1.5f };
            if (_timeline == null) return;
            int i = System.Array.IndexOf(speeds, _timeline.PlaybackSpeed);
            SetSpeed(speeds[(i + 1) % speeds.Length]);
        }

        // ==================== 视角 / 角色切换（只改表现层） ====================

        public void CycleCameraMode()
        {
            if (_camera == null) return;
            _camera.CycleMode();
            OnCameraModeChanged?.Invoke(_camera.Mode);
        }

        public void SetCameraMode(CameraMode mode)
        {
            if (_camera == null) return;
            _camera.SetMode(mode);
            OnCameraModeChanged?.Invoke(mode);
        }

        public void ResetCamera() => _camera?.ResetView();

        public void CycleAvatar()
        {
            if (_avatar == null) return;
            _avatar.SwitchNext();
            _fingerPose.Initialize(_avatar.LeftHand, _avatar.RightHand);
            OnAvatarChanged?.Invoke(_avatar.CurrentIndex);
        }

        public void SwitchAvatar(int index)
        {
            if (_avatar == null) return;
            _avatar.SwitchAvatar(index);
            _fingerPose.Initialize(_avatar.LeftHand, _avatar.RightHand);
            OnAvatarChanged?.Invoke(_avatar.CurrentIndex);
        }

        // ==================== 练习模式 / 循环（方案 2.2） ====================

        public void CyclePracticeHand()
        {
            SetPracticeHand((PracticeHand)(((int)_practiceHand + 1) % 3));
        }

        /// <summary>切换练习模式（双手 / 仅左手 / 仅右手），保持播放进度连续。</summary>
        public void SetPracticeHand(PracticeHand hand)
        {
            if (_practiceHand == hand) return;
            _practiceHand = hand;
            ReloadFilteredNotes();
            OnPracticeHandChanged?.Invoke(hand);
        }

        /// <summary>整曲循环开关（A-B 片段循环可用 SetLoop 精确设置）。</summary>
        public void ToggleLoop()
        {
            if (_timeline == null) return;
            if (!RequireSong("循环")) return;
            if (_timeline.IsLooping) _timeline.ClearLoop();
            else _timeline.SetLoop(0f, _timeline.TotalDuration);
        }

        public void SetLoop(float start, float end) => _timeline?.SetLoop(start, end);

        private void ReloadFilteredNotes()
        {
            if (_currentSong == null || _timeline == null || _scheduler == null) return;
            float current = _timeline.CurrentTime;
            var filtered = PianoNoteFilter.FilterByHand(_currentSong.Notes, _practiceHand);
            if (filtered.Count == 0 && _practiceHand != PracticeHand.Both)
            {
                string handName = _practiceHand == PracticeHand.LeftOnly ? "左手" : "右手";
                OnError?.Invoke($"{handName}在本曲目中没有音符（该手将为静默）");
            }
            _scheduler.Load(filtered);
            ResetPlaybackState();
            _scheduler.SeekTo(current);
        }

        /// <summary>统一守卫：未加载曲目时给出用户可见提示并返回 false（避免"点了没反应"）。</summary>
        private bool RequireSong(string action)
        {
            if (HasSong) return true;
            OnError?.Invoke($"{action}失败：尚未加载曲目");
            return false;
        }

        // ==================== 内部 ====================

        private void ResetPlayback()
        {
            _timeline?.Stop();
            ResetPlaybackState();
        }

        private void ResetPlaybackState()
        {
            _keyAnimator?.ReleaseAll();
            _handIK?.ReleaseAll();
            _fingerPose?.ReleaseAll();
            _highlighter?.ClearAll();
            _metronome?.Reset(_timeline != null ? _timeline.CurrentTime : 0f);
        }

        // ==================== 琴键高亮 / 节拍器（方案 2.2） ====================

        /// <summary>循环切换高亮模式：关闭 → 预提示+发声 → 仅发声 → 仅预提示 → 关闭。</summary>
        public void CycleHighlightMode()
        {
            if (_highlighter == null) return;
            HighlightMode next = _highlighter.Mode switch
            {
                HighlightMode.Off => HighlightMode.Both,
                HighlightMode.Both => HighlightMode.Active,
                HighlightMode.Active => HighlightMode.Upcoming,
                _ => HighlightMode.Off
            };
            SetHighlightMode(next);
        }

        /// <summary>设置高亮模式（关闭时彻底清除颜色，不留残留）。</summary>
        public void SetHighlightMode(HighlightMode mode)
        {
            if (_highlighter == null) return;
            _highlighter.Mode = mode;
            if (mode == HighlightMode.Off)
            {
                _highlighter.ClearAll();
                _upcomingBuffer.Clear();
            }
            OnHighlightModeChanged?.Invoke(mode);
        }

        /// <summary>节拍器开关（未加载曲目时给出提示）。</summary>
        public void ToggleMetronome()
        {
            if (_metronome == null) return;
            if (!_metronome.Enabled && !RequireSong("节拍器")) return;
            _metronome.Enabled = !_metronome.Enabled;
            OnMetronomeToggled?.Invoke(_metronome.Enabled);
        }

        /// <summary>设置每小节拍数（4 = 4/4 拍）。</summary>
        public void SetBeatsPerBar(int beatsPerBar)
        {
            if (_metronome != null) _metronome.BeatsPerBar = beatsPerBar;
        }
    }
}
