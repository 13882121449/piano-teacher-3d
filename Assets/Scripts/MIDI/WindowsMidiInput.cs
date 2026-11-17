using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PianoTeacher3D.MIDI
{
    internal sealed class WindowsMidiInput : IDisposable
    {
        internal readonly struct Device
        {
            public readonly int Id;
            public readonly string Name;
            public Device(int id, string name) { Id = id; Name = name; }
        }

        internal readonly struct Note
        {
            public readonly int Pitch;
            public readonly int Velocity;
            public readonly int Channel;
            public readonly double TimestampSeconds;
            public readonly bool IsNoteOn;
            public Note(int pitch, int velocity, int channel, double time, bool on)
            {
                Pitch = pitch;
                Velocity = velocity;
                Channel = channel;
                TimestampSeconds = time;
                IsNoteOn = on;
            }
        }

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<Note> _notes = new();
        internal static double NowSeconds => Clock.Elapsed.TotalSeconds;

        internal static bool TryDecode(uint packed, double time, out Note note)
        {
            int status = (int)(packed & 0xff);
            int kind = status & 0xf0;
            int pitch = (int)((packed >> 8) & 0xff);
            int velocity = (int)((packed >> 16) & 0xff);
            if ((kind != 0x80 && kind != 0x90) || pitch > 127 || velocity > 127)
            {
                note = default;
                return false;
            }
            note = new Note(pitch, velocity, status & 15, time, kind == 0x90 && velocity != 0);
            return true;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceCaps
        {
            public ushort Manufacturer, Product;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint Support;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void MidiCallback(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr milliseconds);
        [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint midiInGetDevCapsW(UIntPtr id, out DeviceCaps caps, uint size);
        [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle, uint id, MidiCallback callback, UIntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);

        private static readonly MidiCallback Callback = Receive;
        private GCHandle _callbackTarget;
        private IntPtr _handle;
        private volatile bool _accepting;
        private double _epoch;

        public bool IsConnected => _handle != IntPtr.Zero && _accepting;

        public static IReadOnlyList<Device> GetDevices()
        {
            var devices = new List<Device>();
            uint count = midiInGetNumDevs();
            for (uint i = 0; i < count; i++)
                if (midiInGetDevCapsW(new UIntPtr(i), out var caps, (uint)Marshal.SizeOf<DeviceCaps>()) == 0)
                    devices.Add(new Device((int)i, caps.Name));
            return devices;
        }

        public bool TryConnect(int deviceId, out string error)
        {
            Disconnect();
            if (_handle != IntPtr.Zero)
            {
                error = "上一个 MIDI 设备尚未关闭";
                return false;
            }
            _callbackTarget = GCHandle.Alloc(this);
            uint result = midiInOpen(out var handle, unchecked((uint)deviceId), Callback,
                (UIntPtr)(ulong)GCHandle.ToIntPtr(_callbackTarget).ToInt64(), 0x30000);
            if (result == 0)
            {
                _handle = handle;
                _epoch = NowSeconds;
                _accepting = true;
                result = midiInStart(_handle);
                if (result == 0)
                {
                    error = null;
                    return true;
                }
                Disconnect();
            }
            else _callbackTarget.Free();
            error = result == 4 ? "MIDI 设备正在被其他程序使用" : $"无法连接 MIDI 设备（{result}）";
            return false;
        }

        private static void Receive(IntPtr _, uint message, UIntPtr instance, UIntPtr data, UIntPtr milliseconds)
        {
            const uint Data = 0x3C3;
            if (message != Data) return;
            var target = (WindowsMidiInput)GCHandle.FromIntPtr(new IntPtr((long)instance.ToUInt64())).Target;
            if (target._accepting &&
                TryDecode(data.ToUInt32(), target._epoch + milliseconds.ToUInt32() / 1000.0, out var note))
                target._notes.Enqueue(note);
        }

        public void Disconnect()
        {
            _accepting = false;
            if (_handle == IntPtr.Zero) return;
            midiInStop(_handle);
            // 等设备关闭后再释放回调上下文。
            if (midiInClose(_handle) == 0)
            {
                _handle = IntPtr.Zero;
                _callbackTarget.Free();
            }
            while (_notes.TryDequeue(out _)) { }
        }
#else
        public bool IsConnected => false;
        public static IReadOnlyList<Device> GetDevices() => Array.Empty<Device>();
        public bool TryConnect(int deviceId, out string error)
        {
            error = "当前系统无法使用 Windows MIDI 输入。";
            return false;
        }
        public void Disconnect() { }
#endif
        public bool TryRead(out Note note) => _notes.TryDequeue(out note);
        public void Dispose() => Disconnect();
    }
}
