using System;
using System.Collections.Generic;
using PianoTeacher3D.MIDI;
using UnityEngine;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 学生的弹奏输入（2026-11-18）：Windows MIDI 键盘（自动连接第一台设备）+ 电脑键盘（可选）。
    /// <para>
    /// 电脑键盘布局（钢琴软件常见的两排映射）：
    /// 下排 Z X C V B N M = C4 D4 E4 F4 G4 A4 B4，S D G H J = 其间的黑键；
    /// 上排 Q W E R T Y U I = C5 … C6，2 3 5 6 7 = 其间的黑键。
    /// </para>
    /// </summary>
    public sealed class PracticeInput : IDisposable
    {
        private static readonly (KeyCode key, int pitch)[] KeyboardMap =
        {
            (KeyCode.Z, 60), (KeyCode.S, 61), (KeyCode.X, 62), (KeyCode.D, 63), (KeyCode.C, 64), (KeyCode.V, 65),
            (KeyCode.G, 66), (KeyCode.B, 67), (KeyCode.H, 68), (KeyCode.N, 69), (KeyCode.J, 70), (KeyCode.M, 71),
            (KeyCode.Q, 72), (KeyCode.Alpha2, 73), (KeyCode.W, 74), (KeyCode.Alpha3, 75), (KeyCode.E, 76), (KeyCode.R, 77),
            (KeyCode.Alpha5, 78), (KeyCode.T, 79), (KeyCode.Alpha6, 80), (KeyCode.Y, 81), (KeyCode.Alpha7, 82), (KeyCode.U, 83),
            (KeyCode.I, 84)
        };

        private readonly WindowsMidiInput _midi = new WindowsMidiInput();
        private float _nextConnectTry;

        /// <summary>是否接受电脑键盘弹奏。</summary>
        public bool KeyboardEnabled { get; set; }

        /// <summary>已连接的 MIDI 设备名（未连接为空）。</summary>
        public string DeviceName { get; private set; } = string.Empty;

        public bool MidiConnected => _midi.IsConnected;

        /// <summary>按键事件：音高、力度（0 = 抬起）。</summary>
        public event Action<int, int> NoteOn;
        public event Action<int> NoteOff;

        /// <summary>每帧调用：读取 MIDI 队列与电脑键盘。</summary>
        public void Poll()
        {
            if (!_midi.IsConnected && Time.unscaledTime >= _nextConnectTry)
            {
                _nextConnectTry = Time.unscaledTime + 3f;   // 设备热插拔：每 3 s 试一次
                TryConnectFirst();
            }
            while (_midi.TryRead(out var note))
            {
                if (note.IsNoteOn && note.Velocity > 0) NoteOn?.Invoke(note.Pitch, note.Velocity);
                else NoteOff?.Invoke(note.Pitch);
            }

            if (!KeyboardEnabled) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            foreach (var (key, pitch) in KeyboardMap)
            {
                int p = shift ? pitch - 12 : pitch;   // 按住 Shift 降一个八度，左手练习用
                if (Input.GetKeyDown(key)) NoteOn?.Invoke(p, 90);
                if (Input.GetKeyUp(key)) NoteOff?.Invoke(p);
            }
        }

        private void TryConnectFirst()
        {
            IReadOnlyList<WindowsMidiInput.Device> devices;
            try { devices = WindowsMidiInput.GetDevices(); }
            catch (Exception) { return; }
            if (devices == null || devices.Count == 0) return;
            if (_midi.TryConnect(devices[0].Id, out _)) DeviceName = devices[0].Name;
        }

        public void Dispose()
        {
            _midi.Dispose();
            DeviceName = string.Empty;
        }
    }
}
