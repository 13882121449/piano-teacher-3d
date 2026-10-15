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

        public bool HasSong => _currentSong != null && _currentSong.Success;
        public string CurrentSongName { get; private set; } = "";
        public PlaybackState PlaybackState => _timeline != null ? _timeline.State : PlaybackState.Stopped;
        public float PlaybackSpeed => _timeline != null ? _timeline.PlaybackSpeed : 1f;
        public CameraMode CameraMode => _camera != null ? _camera.Mode : CameraMode.Full;
        public PracticeHand PracticeHand => _practiceHand;
        public bool IsLooping => _timeline != null && _timeline.IsLooping;

        // 供 UI 订阅
        public event System.Action<string> OnError;
        public event System.Action OnSongLoaded;
        public event System.Action<PlaybackState> OnPlaybackStateChanged;
        public event System.Action<float, float> OnProgressChanged; // current, total
        public event System.Action<int> OnAvatarChanged;
        public event System.Action<CameraMode> OnCameraModeChanged;
        public event System.Action<PracticeHand> OnPracticeHandChanged;

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
            OnProgressChanged?.Invoke(current, _timeline.TotalDuration);
        }

        // ==================== 场景装配 ====================

        private void BuildScene()
        {
            // 1. 统一时间轴
            _timeline = gameObject.AddComponent<LessonTimeline>();
            _timeline.OnStateChanged += s => OnPlaybackStateChanged?.Invoke(s);

            // 2. MIDI 事件调度器
            _scheduler = gameObject.AddComponent<MidiEventScheduler>();

            // 3. 钢琴（程序化 88 键，居中）
            var piano = new GameObject("Piano");
            piano.transform.SetParent(transform, false);
            piano.transform.localPosition = new Vector3(-26f, 0f, 0f); // 52 白键宽 52，居中
            _keyMap = piano.AddComponent<PianoKeyMap>();
            _keyMap.BuildKeys();

            // 4. 琴键动画 + 音频
            _keyAnimator = gameObject.AddComponent<PianoKeyAnimator>();
            _keyAnimator.Initialize(_keyMap);
            _audio = gameObject.AddComponent<PianoAudioSynth>();
            _audio.Initialize();

            // 5. 人物（3 预设角色，坐于钢琴前 -Z 侧）
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.transform.SetParent(transform, false);
            avatarRoot.transform.localPosition = new Vector3(0f, 0f, -7f);
            _avatar = avatarRoot.AddComponent<AvatarManager>();
            _avatar.BuildAvatars();

            // 6. 手部 IK + 手指姿态
            _handIK = gameObject.AddComponent<HandIKController>();
            _handIK.Initialize(_avatar, _keyMap);
            _fingerPose = gameObject.AddComponent<FingerPoseController>();
            _fingerPose.Initialize(_avatar.LeftHand, _avatar.RightHand);

            // 7. 摄像机控制
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _camera = gameObject.AddComponent<CameraModeController>();
            _camera.Initialize(cam, _avatar);

            // 8. 灯光（确保存在并设置为主方向光）
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Directional Light");
                light = lightGo.AddComponent<Light>();
            }
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

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
            _scheduler.OnNoteOff += _keyAnimator.OnNoteOff;
            _scheduler.OnNoteOff += _handIK.OnNoteOff;
            _scheduler.OnNoteOff += _fingerPose.OnNoteOff;
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
            ResetPlayback();
            OnSongLoaded?.Invoke();
        }

        // ==================== 播放控制（转发到统一时间轴） ====================

        public void PlayPause()
        {
            if (_timeline == null) return;
            if (_timeline.State == PlaybackState.Playing) _timeline.Pause();
            else _timeline.Play();
        }

        public void Play()
        {
            if (!HasSong) { OnError?.Invoke("尚未加载曲目"); return; }
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
            if (!HasSong) return;
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
            if (_timeline.IsLooping) _timeline.ClearLoop();
            else _timeline.SetLoop(0f, _timeline.TotalDuration);
        }

        public void SetLoop(float start, float end) => _timeline?.SetLoop(start, end);

        private void ReloadFilteredNotes()
        {
            if (_currentSong == null || _timeline == null || _scheduler == null) return;
            float current = _timeline.CurrentTime;
            var filtered = PianoNoteFilter.FilterByHand(_currentSong.Notes, _practiceHand);
            _scheduler.Load(filtered);
            ResetPlaybackState();
            _scheduler.SeekTo(current);
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
        }
    }
}
