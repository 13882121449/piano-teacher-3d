using UnityEngine;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 统一演奏时间轴（方案 4.3）：唯一权威的播放时间来源。
    /// 音乐、琴键、人物动作、UI 进度都由它驱动，禁止其他模块各自启动独立计时器。
    /// </summary>
    public class LessonTimeline : MonoBehaviour
    {
        public PlaybackState State { get; private set; } = PlaybackState.Stopped;

        /// <summary>当前播放时间（秒）。</summary>
        public float CurrentTime { get; private set; }

        /// <summary>曲目总时长（秒），由曲目加载时设置。</summary>
        public float TotalDuration { get; private set; }

        private float _playbackSpeed = 1f;

        /// <summary>播放速度倍率（1 = 原速，0.5 = 半速）。clamp 到 [0.1, +∞)，防止负值导致时间倒退。</summary>
        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set => _playbackSpeed = Mathf.Max(0.1f, value);
        }

        public bool IsLooping { get; set; }

        public event System.Action<PlaybackState> OnStateChanged;
        public event System.Action<float> OnTimeChanged;

        private void Update()
        {
            if (State != PlaybackState.Playing) return;

            CurrentTime += Time.deltaTime * PlaybackSpeed;

            if (TotalDuration > 0f && CurrentTime >= TotalDuration)
            {
                if (IsLooping)
                {
                    CurrentTime = 0f;
                }
                else
                {
                    CurrentTime = TotalDuration;
                    ChangeState(PlaybackState.Paused); // 播完停在结尾，不归零
                }
            }

            OnTimeChanged?.Invoke(CurrentTime);
        }

        public void SetDuration(float seconds)
        {
            TotalDuration = Mathf.Max(0f, seconds);
        }

        public void Play()
        {
            if (State == PlaybackState.Playing) return;
            if (TotalDuration > 0f && CurrentTime >= TotalDuration)
            {
                CurrentTime = 0f; // 播完后再次播放 = 重新开始
            }
            ChangeState(PlaybackState.Playing);
        }

        public void Pause()
        {
            if (State != PlaybackState.Playing) return;
            ChangeState(PlaybackState.Paused);
        }

        public void Stop()
        {
            CurrentTime = 0f;
            ChangeState(PlaybackState.Stopped);
            OnTimeChanged?.Invoke(CurrentTime);
        }

        public void Seek(float seconds)
        {
            CurrentTime = Mathf.Clamp(seconds, 0f, TotalDuration);
            OnTimeChanged?.Invoke(CurrentTime);
        }

        public void Restart()
        {
            CurrentTime = 0f;
            ChangeState(PlaybackState.Playing);
            OnTimeChanged?.Invoke(CurrentTime);
        }

        private void ChangeState(PlaybackState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(State);
        }
    }
}
