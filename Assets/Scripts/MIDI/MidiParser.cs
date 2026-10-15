using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PianoTeacher3D.MIDI
{
    /// <summary>
    /// MIDI 解析结果。成功时携带标准化音符事件与总时长；失败时携带错误信息。
    /// </summary>
    public sealed class MidiParseResult
    {
        private static readonly TempoChange[] DefaultTempo =
        {
            new TempoChange(0f, TempoChange.DefaultMpq)
        };

        public bool Success { get; }
        public string Error { get; }
        public List<MidiNoteEvent> Notes { get; }
        public float TotalDurationSeconds { get; }
        public ushort Division { get; }
        public int TrackCount { get; }

        /// <summary>
        /// tempo 变化点（秒制，按时间升序，首项必为 t=0）。
        /// 无 tempo 事件时返回单个 120 BPM 默认项，调用方无需判空。
        /// </summary>
        public IReadOnlyList<TempoChange> TempoChanges { get; }

        /// <summary>
        /// 缺少 Note Off 而被解析器按曲目末尾补全的音符数（异常 MIDI 提示）。
        /// 为 0 表示所有音符都有正常结束时间。
        /// </summary>
        public int DanglingNoteCount { get; }

        public MidiParseResult(bool success, string error, List<MidiNoteEvent> notes,
            float totalDuration, ushort division, int trackCount,
            IReadOnlyList<TempoChange> tempoChanges = null, int danglingNoteCount = 0)
        {
            Success = success;
            Error = error;
            Notes = notes ?? new List<MidiNoteEvent>();
            TotalDurationSeconds = totalDuration;
            Division = division;
            TrackCount = trackCount;
            TempoChanges = tempoChanges ?? DefaultTempo;
            DanglingNoteCount = danglingNoteCount < 0 ? 0 : danglingNoteCount;
        }

        public static MidiParseResult Fail(string error)
        {
            return new MidiParseResult(false, error, null, 0f, 0, 0);
        }
    }

    /// <summary>
    /// 纯 C# 的标准 MIDI 文件（SMF）解析器。不依赖 UnityEngine，便于单元测试。
    /// 支持 Format 0/1/2、PPQ 时间基（不支持 SMPTE）、running status、tempo 变化。
    /// 输出：标准化 <see cref="MidiNoteEvent"/> 列表（秒制时间）。
    /// </summary>
    public static class MidiParser
    {
        private const int DefaultTempo = 500000; // 微秒/四分音符 = 120 BPM

        /// <summary>解析内存中的 MIDI 字节数据。</summary>
        public static MidiParseResult Parse(byte[] data)
        {
            if (data == null || data.Length < 14)
                return MidiParseResult.Fail("MIDI 数据为空或长度不足");

            try
            {
                using var ms = new MemoryStream(data);
                using var r = new BinaryReader(ms);

                // ---- MThd 头块 ----
                if (ReadFourCC(r) != "MThd")
                    return MidiParseResult.Fail("缺少 MThd 头块");
                int headerLen = ReadInt32BE(r);
                if (headerLen < 6)
                    return MidiParseResult.Fail("MThd 头块长度非法");
                ushort format = ReadUInt16BE(r);
                ushort trackCount = ReadUInt16BE(r);
                ushort division = ReadUInt16BE(r);
                // 跳过 headerLen - 6 的剩余字节（防御性）
                long skip = headerLen - 6;
                if (skip > 0) r.BaseStream.Seek(skip, SeekOrigin.Current);

                if ((division & 0x8000) != 0)
                    return MidiParseResult.Fail("不支持 SMPTE 时间基，仅支持 PPQ");

                int ppq = division & 0x7FFF;
                if (ppq <= 0)
                    return MidiParseResult.Fail("非法 PPQ 时间基");

                // ---- 逐轨读取，收集事件 ----
                var events = new List<(long tick, bool isOn, int pitch, int velocity, int track, int channel)>();
                var tempoEvents = new List<(long tick, int tempo)>();
                long maxTick = 0;

                for (int t = 0; t < trackCount; t++)
                {
                    if (ReadFourCC(r) != "MTrk")
                        return MidiParseResult.Fail($"轨道 {t} 缺少 MTrk 块");
                    int trackLen = ReadInt32BE(r);
                    long trackEnd = r.BaseStream.Position + trackLen;
                    long tick = 0;
                    byte runningStatus = 0;

                    while (r.BaseStream.Position < trackEnd)
                    {
                        tick += ReadVLQ(r);
                        int status = r.ReadByte();
                        int eventStatus;

                        if (status < 0x80)
                        {
                            // running status：数据字节复用上一个状态
                            if (runningStatus == 0)
                                return MidiParseResult.Fail($"轨道 {t} 出现非法 running status");
                            eventStatus = runningStatus;
                            r.BaseStream.Seek(-1, SeekOrigin.Current);
                        }
                        else
                        {
                            eventStatus = status;
                            if (status < 0xF0)
                                runningStatus = (byte)status;
                            else
                                runningStatus = 0; // meta / sysex 不更新 running status
                        }

                        if (eventStatus == 0xFF)
                        {
                            // meta event
                            int metaType = r.ReadByte();
                            int len = (int)ReadVLQ(r);
                            if (metaType == 0x51 && len == 3)
                            {
                                int b0 = r.ReadByte(), b1 = r.ReadByte(), b2 = r.ReadByte();
                                int tempo = (b0 << 16) | (b1 << 8) | b2;
                                tempoEvents.Add((tick, tempo));
                            }
                            else if (metaType == 0x2F)
                            {
                                break; // End of Track
                            }
                            else
                            {
                                r.BaseStream.Seek(len, SeekOrigin.Current);
                            }
                        }
                        else if (eventStatus == 0xF0 || eventStatus == 0xF7)
                        {
                            int len = (int)ReadVLQ(r);
                            r.BaseStream.Seek(len, SeekOrigin.Current); // sysex，跳过
                        }
                        else
                        {
                            int channel = eventStatus & 0x0F;
                            int high = eventStatus & 0xF0;
                            int data1 = r.ReadByte();
                            int data2 = 0;
                            if (high != 0xC0 && high != 0xD0)
                                data2 = r.ReadByte();

                            if (high == 0x90)
                            {
                                // Note On（velocity==0 视为 Note Off）
                                if (data2 > 0)
                                    events.Add((tick, true, data1, data2, t, channel));
                                else
                                    events.Add((tick, false, data1, 0, t, channel));
                            }
                            else if (high == 0x80)
                            {
                                events.Add((tick, false, data1, 0, t, channel));
                            }
                            // 其余通道消息（CC/弯音/触后等）首版忽略
                        }
                    }

                    r.BaseStream.Position = trackEnd; // 防御性对齐
                    if (tick > maxTick) maxTick = tick;
                }

                // ---- Note On/Off 配对（统一事件流 + 栈，off 关闭最近的同键 on）----
                events.Sort((a, b) => a.tick.CompareTo(b.tick));
                var pending = new Dictionary<(int ch, int pitch), Stack<(long tick, int velocity, int track)>>();
                var rawNotes = new List<(long startTick, long endTick, int pitch, int velocity, int track, int channel)>();

                foreach (var e in events)
                {
                    var key = (e.channel, e.pitch);
                    if (e.isOn)
                    {
                        if (!pending.TryGetValue(key, out var st))
                        {
                            st = new Stack<(long, int, int)>();
                            pending[key] = st;
                        }
                        st.Push((e.tick, e.velocity, e.track));
                    }
                    else
                    {
                        if (pending.TryGetValue(key, out var st) && st.Count > 0)
                        {
                            var on = st.Pop();
                            rawNotes.Add((on.tick, e.tick, e.pitch, on.velocity, on.track, e.channel));
                        }
                        // 悬空 Note Off（无对应 On）忽略
                    }
                }
                // 未关闭的 Note On：以全曲最大 tick 收尾（不再丢弃），并计数以便上报异常
                int danglingCount = 0;
                foreach (var kv in pending)
                {
                    var stack = kv.Value;
                    while (stack.Count > 0)
                    {
                        var on = stack.Pop();
                        danglingCount++;
                        if (maxTick > on.tick)
                            rawNotes.Add((on.tick, maxTick, kv.Key.pitch, on.velocity, on.track, kv.Key.ch));
                    }
                }
                pending.Clear();

                // ---- tick → 秒（依据 tempo map 分段累积）----
                var tempoSorted = tempoEvents.OrderBy(e => e.tick).ToList();

                double TickToSeconds(long tick)
                {
                    double seconds = 0.0;
                    long prevTick = 0;
                    int curTempo = DefaultTempo;
                    foreach (var te in tempoSorted)
                    {
                        if (te.tick >= tick) break;
                        seconds += (te.tick - prevTick) * (double)curTempo / ppq / 1_000_000.0;
                        prevTick = te.tick;
                        curTempo = te.tempo;
                    }
                    seconds += (tick - prevTick) * (double)curTempo / ppq / 1_000_000.0;
                    return seconds;
                }

                var notes = new List<MidiNoteEvent>(rawNotes.Count);
                double maxEndSeconds = 0.0;
                foreach (var raw in rawNotes)
                {
                    float start = (float)TickToSeconds(raw.startTick);
                    float end = (float)TickToSeconds(raw.endTick);
                    float duration = Math.Max(0f, end - start);
                    notes.Add(new MidiNoteEvent(raw.pitch, start, duration, raw.velocity, raw.track, raw.channel));
                    if (end > maxEndSeconds) maxEndSeconds = end;
                }

                notes.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

                // ---- 产出秒制 tempo 变化点（首项保证为 t=0）----
                var tempoChanges = new List<TempoChange>();
                foreach (var te in tempoSorted)
                {
                    float tSec = (float)TickToSeconds(te.tick);
                    // 同一时间点重复出现时，后出现的覆盖前者（异常 MIDI 防御）
                    if (tempoChanges.Count > 0 &&
                        Math.Abs(tempoChanges[tempoChanges.Count - 1].TimeSeconds - tSec) < 1e-4f)
                    {
                        tempoChanges[tempoChanges.Count - 1] = new TempoChange(tSec, te.tempo);
                    }
                    else
                    {
                        tempoChanges.Add(new TempoChange(tSec, te.tempo));
                    }
                }
                if (tempoChanges.Count == 0 || tempoChanges[0].TimeSeconds > 1e-4f)
                    tempoChanges.Insert(0, new TempoChange(0f, DefaultTempo));

                return new MidiParseResult(true, null, notes, (float)maxEndSeconds,
                    division, trackCount, tempoChanges, danglingCount);
            }
            catch (EndOfStreamException)
            {
                return MidiParseResult.Fail("MIDI 文件在读取中意外结束（数据损坏或截断）");
            }
            catch (Exception ex)
            {
                return MidiParseResult.Fail($"MIDI 解析失败：{ex.Message}");
            }
        }

        /// <summary>解析磁盘上的 MIDI 文件（含 IO 异常防护，任何失败都返回 Fail 而非抛出）。</summary>
        public static MidiParseResult ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return MidiParseResult.Fail("文件路径为空");
            if (!File.Exists(path))
                return MidiParseResult.Fail($"文件不存在：{path}");

            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                // 文件被占用 / 磁盘错误 / 路径过长等
                return MidiParseResult.Fail($"文件读取失败（可能被占用或损坏）：{ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                return MidiParseResult.Fail($"无权限读取文件：{path}");
            }
            catch (Exception ex)
            {
                return MidiParseResult.Fail($"读取文件异常：{ex.Message}");
            }

            return Parse(data);
        }

        // ---- 底层读取 ----

        private static string ReadFourCC(BinaryReader r)
        {
            var bytes = r.ReadBytes(4);
            if (bytes.Length < 4) throw new EndOfStreamException();
            return new string(new[] { (char)bytes[0], (char)bytes[1], (char)bytes[2], (char)bytes[3] });
        }

        private static int ReadInt32BE(BinaryReader r)
        {
            var b = r.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        private static ushort ReadUInt16BE(BinaryReader r)
        {
            var b = r.ReadBytes(2);
            if (b.Length < 2) throw new EndOfStreamException();
            return (ushort)((b[0] << 8) | b[1]);
        }

        /// <summary>读取可变长度数量（VLQ，7 位编码）。</summary>
        private static long ReadVLQ(BinaryReader r)
        {
            long value = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = r.ReadByte();
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                    return value;
            }
            throw new InvalidDataException("VLQ 长度非法");
        }
    }
}
