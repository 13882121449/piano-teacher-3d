using System.Collections.Generic;
using UnityEngine;

namespace PianoTeacher3D.Piano
{
    /// <summary>
    /// 音频模块（方案 3.3 数据流第 5 步）：将音符事件转为声音。
    /// MVP 采用程序化波形合成（基频 + 2/3/4 次谐波 + 指数衰减），
    /// 零外部音频资产，符合「遵守资源许可证」原则；后续可替换为合法音色采样。
    /// </summary>
    public class PianoAudioSynth : MonoBehaviour
    {
        private readonly Dictionary<int, AudioClip> _clips = new();
        private AudioSource _source;

        public void Initialize()
        {
            if (_source != null) return; // 幂等：避免重复 AddComponent
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;

            for (int pitch = PianoKeyMap.MinPitch; pitch <= PianoKeyMap.MaxPitch; pitch++)
            {
                _clips[pitch] = CreatePianoClip(pitch, 2.5f);
            }
        }

        /// <summary>Note On 时合成发声，音量由 velocity（力度）决定。</summary>
        public void OnNoteOn(MIDI.MidiNoteEvent e)
        {
            if (_clips.TryGetValue(e.Pitch, out var clip))
            {
                float volume = Mathf.Clamp01(e.Velocity / 127f);
                _source.PlayOneShot(clip, volume);
            }
        }

        private static AudioClip CreatePianoClip(int pitch, float duration)
        {
            float freq = 440f * Mathf.Pow(2f, (pitch - 69) / 12f);
            const int sampleRate = 44100;
            int samples = Mathf.Max(1, (int)(sampleRate * duration));
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Exp(-2.5f * t);
                float v = Mathf.Sin(2f * Mathf.PI * freq * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * 2f * freq * t)
                        + 0.25f * Mathf.Sin(2f * Mathf.PI * 3f * freq * t)
                        + 0.12f * Mathf.Sin(2f * Mathf.PI * 4f * freq * t);
                data[i] = v * env * 0.25f;
            }

            var clip = AudioClip.Create($"piano_{pitch}", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
