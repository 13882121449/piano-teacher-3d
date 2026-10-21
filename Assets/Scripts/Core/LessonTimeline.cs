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

        /// <summary>A-B 循环起点（秒）。IsLooping 且 LoopEnd &gt; LoopStart 时生效。</summary>
        public float LoopStart { get; private set; }

        /// <summary>A-B 循环终点（秒）。</summary>
        public float LoopEnd { get; private set; }

        public event System.Action<PlaybackState> OnStateChanged;
        public event System.Action<float> OnTimeChanged;

        /// <summary>上次采样到的音频时钟（秒）；&lt; 0 表示需要重新对齐（P1-7）。</summary>
        private double _lastDspTime = -1.0;

        private void Update()
        {
            if (State != PlaybackState.Playing) return;

            // 游戏级暂停（timeScale=0）时时间轴也必须冻结：dspTime 不认 timeScale，
            // 所以这里显式挡住，保持"暂停时时间不走"的既有语义。
            if (Time.timeScale <= 0f) return;

            CurrentTime += SampleDelta() * PlaybackSpeed;

            if (TotalDuration > 0f && CurrentTime >= TotalDuration)
            {
                if (IsLooping)
                {
                    CurrentTime = LoopStart;
                }
                else
                {
                    CurrentTime = TotalDuration;
                    ChangeState(PlaybackState.Paused); // 播完停在结尾，不归零
                }
            }
            else if (IsLooping && LoopEnd > LoopStart && CurrentTime >= LoopEnd)
            {
                CurrentTime = LoopStart; // A-B 循环跳回
            }

            OnTimeChanged?.Invoke(CurrentTime);
        }

        /// <summary>
        /// 取本次推进的时间增量（P1-7）：优先用音频时钟 <c>AudioSettings.dspTime</c> 的差值，
        /// 让时间轴与 <c>PianoAudioSynth</c> 的 DSP 时钟同源，避免帧抖动被积分放大成音画漂移；
        /// 没有音频设备（CI / 静音播放）时 <c>dspTime</c> 不前进，自动退化为帧时钟。
        /// </summary>
        private float SampleDelta()
        {
            double now = AudioSettings.dspTime;
            float delta = Time.deltaTime;
            if (now > 0.0 && _lastDspTime >= 0.0 && now > _lastDspTime)
            {
                delta = (float)(now - _lastDspTime);
            }
            _lastDspTime = now > 0.0 ? now : -1.0;

            // 上限 0.25 s：暂停/卡顿后的第一帧不把整段停顿一次性补进时间轴
            return Mathf.Clamp(delta, 0f, 0.25f);
        }

        /// <summary>重新对齐音频时钟：播放、跳转、停止、重播后都必须调用，否则会补进一整段停顿。</summary>
        private void ResetClock()
        {
            _lastDspTime = -1.0;
        }

        public void SetDuration(float seconds)
        {
            TotalDuration = Mathf.Max(0f, seconds);
        }

        /// <summary>设置 A-B 循环区间；区间无效（end &lt;= start）时自动关闭循环。</summary>
        public void SetLoop(float start, float end)
        {
            float max = Mathf.Max(0f, TotalDuration);
            LoopStart = Mathf.Clamp(start, 0f, max);
            LoopEnd = Mathf.Clamp(end, LoopStart, max);
            IsLooping = LoopEnd > LoopStart;
        }

        /// <summary>关闭循环并复位区间。</summary>
        public void ClearLoop()
        {
            LoopStart = 0f;
            LoopEnd = 0f;
            IsLooping = false;
        }

        public void Play()
        {
            if (State == PlaybackState.Playing) return;
            if (TotalDuration > 0f && CurrentTime >= TotalDuration)
            {
                CurrentTime = IsLooping ? LoopStart : 0f; // 播完后再次播放 = 从循环起点重新开始
            }
            ResetClock(); // 从暂停恢复时重新对齐音频时钟，避免把停顿补进来
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
            ResetClock();
            ChangeState(PlaybackState.Stopped);
            OnTimeChanged?.Invoke(CurrentTime);
        }

        public void Seek(float seconds)
        {
            CurrentTime = Mathf.Clamp(seconds, 0f, TotalDuration);
            ResetClock(); // 跳转后以新时间点为基准，避免下一帧把跳转距离当成播放进度
            OnTimeChanged?.Invoke(CurrentTime);
        }

        public void Restart()
        {
            CurrentTime = 0f;
            ResetClock();
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
