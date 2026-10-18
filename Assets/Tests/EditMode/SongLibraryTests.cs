using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;

/// <summary>
/// 曲库与用户偏好测试（EditMode，对应方案 6.4「曲目导入与曲库」、8.4「支持删除与清除缓存」、
/// 9.2「用户偏好与产品默认配置分开保存」、11.4「文件路径含中文/空格」）。
/// <para>
/// 安全说明：<see cref="SongLibraryService"/> 的用户目录固定在 <c>Application.persistentDataPath</c>，
/// 因此 <see cref="SetUp"/> 会先备份真实文件，<see cref="TearDown"/> 结束时原样还原，
/// 避免测试污染开发机上已有的曲库与偏好。
/// </para>
/// </summary>
public class SongLibraryTests
{
    private string _tempRoot;
    private string _productDir;
    private string _userSongsDir;
    private string _libraryPath;
    private string _prefsPath;

    private readonly Dictionary<string, byte[]> _backupFiles = new Dictionary<string, byte[]>();

    [SetUp]
    public void SetUp()
    {
        _productDir = Path.Combine(Application.persistentDataPath, "PianoTeacher3D");
        _userSongsDir = Path.Combine(_productDir, "Songs");
        _libraryPath = Path.Combine(_productDir, "library.json");
        _prefsPath = Path.Combine(_productDir, "user-prefs.json");

        BackupFile(_libraryPath);
        BackupFile(_prefsPath);
        try
        {
            if (Directory.Exists(_userSongsDir))
            {
                foreach (var file in Directory.GetFiles(_userSongsDir)) BackupFile(file);
            }
        }
        catch (Exception) { /* 备份失败时不阻断测试 */ }

        _tempRoot = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_Library");
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            // 1) 清掉测试产生的产物（先删后还原，未备份过的路径保持不存在）
            DeleteAllFilesIn(_userSongsDir);
            DeleteFileSafe(_libraryPath);
            DeleteFileSafe(_prefsPath);

            // 2) 还原开发机上原有的文件
            foreach (var pair in _backupFiles)
            {
                string dir = Path.GetDirectoryName(pair.Key);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(pair.Key, pair.Value);
            }

            // 3) 清掉临时目录
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
        }
        catch (Exception) { /* 清理失败不影响测试结论 */ }
    }

    // ==================== 校验归类（方案 6.4 / 8.2） ====================

    [Test]
    public void Validate_MissingFile_ReturnsMissingWithChooseFileRecovery()
    {
        var check = MidiFileValidator.Validate(Path.Combine(_tempRoot, "not_here.mid"));

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.Missing, check.Reason);
        Assert.AreEqual(ErrorCode.FileNotFound, check.ToError().Code);
        Assert.AreEqual(RecoveryAction.ChooseFile, check.ToError().Recovery);
        Assert.IsFalse(string.IsNullOrEmpty(check.UserMessage), "必须有面向用户的文案");
    }

    [Test]
    public void Validate_NonMidiExtension_ReturnsBadExtension()
    {
        string path = Path.Combine(_tempRoot, "song.txt");
        File.WriteAllBytes(path, BuildValidMidi());

        var check = MidiFileValidator.Validate(path);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.BadExtension, check.Reason);
        Assert.AreEqual(ErrorCode.UnsupportedExtension, check.ToError().Code);
    }

    [Test]
    public void Validate_OverSizeLimit_ReturnsTooLarge()
    {
        string path = Path.Combine(_tempRoot, "big.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        // 上限设成 4 字节，任何 MIDI 都会超限
        var check = MidiFileValidator.Validate(path, maxBytes: 4);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.TooLarge, check.Reason);
        Assert.AreEqual(ErrorCode.FileTooLarge, check.ToError().Code);
    }

    [Test]
    public void Validate_NoNotes_ReturnsNoNotes()
    {
        string path = Path.Combine(_tempRoot, "empty_notes.mid");
        File.WriteAllBytes(path, BuildMidiWithoutNotes());

        var check = MidiFileValidator.Validate(path);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.NoNotes, check.Reason);
        Assert.AreEqual(ErrorCode.MidiNoNotes, check.ToError().Code);
    }

    [Test]
    public void Validate_ZeroDuration_ReturnsZeroDuration()
    {
        string path = Path.Combine(_tempRoot, "zero.mid");
        // 按下后仅 1 tick（≈1ms，远小于 10ms 阈值）就松开 → 视为时长为零
        File.WriteAllBytes(path, BuildMidi(
            NoteOn(0, 60, 100, 0),
            NoteOff(0, 60, 1)));

        var check = MidiFileValidator.Validate(path);

        Assert.IsFalse(check.Ok);
        Assert.AreEqual(ImportRejection.ZeroDuration, check.Reason);
        Assert.AreEqual(ErrorCode.MidiZeroDuration, check.ToError().Code);
    }

    [Test]
    public void Validate_ValidMidi_ReturnsOk()
    {
        string path = Path.Combine(_tempRoot, "ok.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        var check = MidiFileValidator.Validate(path);

        Assert.IsTrue(check.Ok, check.UserMessage);
        Assert.IsNotNull(check.Parse);
        Assert.AreEqual(1, check.Parse.Notes.Count);
    }

    // ==================== 导入事务 ====================

    [Test]
    public void Import_ValidFile_CopiesIntoUserDirectoryAndRecordsRecent()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string path = Path.Combine(_tempRoot, "导入示例.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        var result = service.Import(path);

        Assert.IsTrue(result.Ok, result.Check.UserMessage);
        Assert.IsNotNull(result.Meta);
        Assert.IsTrue(File.Exists(result.Meta.SourcePath), "导入后的副本必须真实存在");
        Assert.IsTrue(
            result.Meta.SourcePath.StartsWith(service.UserSongDirectory, StringComparison.OrdinalIgnoreCase),
            "副本必须落在用户数据目录，而不是 StreamingAssets");
        Assert.IsTrue(service.IsUserImported(result.Meta));

        Assert.AreEqual(1, service.RecentSongs.Count);
        Assert.AreEqual(result.Meta.Id, service.RecentSongs[0].Id);
        Assert.AreNotEqual(0, result.Meta.NoteCount);
    }

    [Test]
    public void Import_SameFileTwice_DeduplicatesRecentEntry()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string path = Path.Combine(_tempRoot, "dup.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        var first = service.Import(path);
        var second = service.Import(path);

        Assert.IsTrue(first.Ok);
        Assert.IsTrue(second.Ok);
        Assert.AreEqual(first.Meta.Id, second.Meta.Id, "同一源路径应得到相同的稳定 ID");
        Assert.AreEqual(1, service.RecentSongs.Count, "重复导入不应产生重复的最近曲目");
    }

    [Test]
    public void RecordPlayed_CapsRecentAtTwentyAndMovesToFront()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        for (int i = 0; i < 25; i++)
        {
            service.RecordPlayed(new SongMetadata
            {
                Id = "id" + i,
                Title = "song" + i,
                SourcePath = Path.Combine(_tempRoot, "s" + i + ".mid")
            });
        }

        Assert.AreEqual(SongLibraryService.MaxRecentSongs, service.RecentSongs.Count, "最近曲目上限 20");
        Assert.AreEqual("id24", service.RecentSongs[0].Id, "最新播放的应在最前");

        // 再次记录已存在的曲目 → 移到最前，数量不变
        service.RecordPlayed(new SongMetadata { Id = "id5", Title = "song5", SourcePath = "x" });
        Assert.AreEqual(SongLibraryService.MaxRecentSongs, service.RecentSongs.Count);
        Assert.AreEqual("id5", service.RecentSongs[0].Id);
    }

    // ==================== 路径失效 / 删除 / 清理 ====================

    [Test]
    public void TryResolve_AfterFileDeleted_ReturnsProblemInsteadOfThrowing()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string path = Path.Combine(_tempRoot, "gone.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        var result = service.Import(path);
        Assert.IsTrue(result.Ok);

        File.Delete(result.Meta.SourcePath); // 模拟用户手动删除

        string resolvedPath = null, problem = null;
        bool ok = true;
        Assert.DoesNotThrow(() => ok = service.TryResolve(result.Meta, out resolvedPath, out problem));

        Assert.IsFalse(ok);
        Assert.IsNull(resolvedPath);
        Assert.IsFalse(string.IsNullOrEmpty(problem), "路径失效必须给出可操作提示");
        Assert.IsTrue(problem.Contains("文件已不在原位置"), $"提示应说明路径失效，实际：{problem}");
        Assert.AreEqual(1, service.RecentSongs.Count, "失效项不应被自动删除（由 UI 决定）");
    }

    [Test]
    public void TryResolveRecent_UnknownId_ReturnsProblem()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        SongMetadata meta = null;
        string resolvedPath = null, problem = null;
        bool ok = true;
        Assert.DoesNotThrow(() => ok = service.TryResolveRecent("不存在", out meta, out resolvedPath, out problem));

        Assert.IsFalse(ok);
        Assert.IsNull(meta);
        Assert.IsFalse(string.IsNullOrEmpty(problem));
    }

    [Test]
    public void Forget_DeletesOnlyUserCopy_AndNeverFilesOutsideUserDirectory()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string path = Path.Combine(_tempRoot, "forgetme.mid");
        File.WriteAllBytes(path, BuildValidMidi());
        var imported = service.Import(path);
        Assert.IsTrue(imported.Ok);

        service.Forget(imported.Meta.Id);
        Assert.IsFalse(File.Exists(imported.Meta.SourcePath), "Forget 应删除用户目录内的副本");
        Assert.AreEqual(0, service.RecentSongs.Count);
        Assert.IsTrue(File.Exists(path), "源文件（用户目录之外）不得被删除");

        // 目录穿越防护：SourcePath 位于用户目录之外时，Forget 不得删除它
        string external = Path.Combine(_tempRoot, "external.mid");
        File.WriteAllBytes(external, BuildValidMidi());
        service.RecordPlayed(new SongMetadata { Id = "evil", Title = "evil", SourcePath = external });

        service.Forget("evil");
        Assert.IsTrue(File.Exists(external), "位于用户目录之外的文件绝不能被删除");

        // 再用「..」构造的 ID 尝试一次
        service.RecordPlayed(new SongMetadata { Id = "evil2", Title = "evil2", SourcePath = external });
        service.Forget("..\\..\\evil2");
        Assert.IsTrue(File.Exists(external), "用 .. 构造的 ID 也不能穿透用户目录保护");
    }

    [Test]
    public void Forget_BuiltInSong_IsNeverDeleted()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        // 用内置示例曲路径构造一条记录（模拟打开示例曲）
        string builtIn = Path.Combine(service.BuiltInSongDirectory, "demo.mid");
        if (!File.Exists(builtIn)) return; // 当前环境没有内置示例曲时不适用本用例

        service.RecordPlayed(new SongMetadata { Id = "builtin", Title = "demo", SourcePath = builtIn });
        service.Forget("builtin");

        Assert.IsTrue(File.Exists(builtIn), "内置示例曲（StreamingAssets）绝不能被删除");
    }

    [Test]
    public void ClearCache_RemovesUserCopiesAndRecentList()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string path = Path.Combine(_tempRoot, "cache.mid");
        File.WriteAllBytes(path, BuildValidMidi());
        var imported = service.Import(path);
        Assert.IsTrue(imported.Ok);
        Assert.IsTrue(File.Exists(imported.Meta.SourcePath));

        service.ClearCache();

        Assert.AreEqual(0, service.RecentSongs.Count);
        Assert.IsFalse(File.Exists(imported.Meta.SourcePath), "ClearCache 应清除用户导入的副本");
        Assert.IsTrue(File.Exists(path), "ClearCache 不得删除用户原始文件");
    }

    // ==================== 中文 / 空格路径（方案 11.4） ====================

    [Test]
    public void Import_PathWithChineseAndSpaces_Works()
    {
        var service = new SongLibraryService();
        service.ClearCache();

        string subDir = Path.Combine(_tempRoot, "我的 钢琴 曲目");
        Directory.CreateDirectory(subDir);
        string path = Path.Combine(subDir, "小星星 练习曲.mid");
        File.WriteAllBytes(path, BuildValidMidi());

        var result = service.Import(path);

        Assert.IsTrue(result.Ok, result.Check.UserMessage);
        Assert.AreEqual("小星星 练习曲", result.Meta.Title);
        Assert.IsTrue(File.Exists(result.Meta.SourcePath));
        Assert.IsTrue(result.Meta.SourcePath.StartsWith(service.UserSongDirectory, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, service.RecentSongs.Count);
    }

    [Test]
    public void IsUserImported_FalseForExternalPath()
    {
        var service = new SongLibraryService();
        var external = new SongMetadata { Id = "x", SourcePath = Path.Combine(_tempRoot, "outside.mid") };

        Assert.IsFalse(service.IsUserImported(external));
        Assert.IsFalse(service.IsUserImported(null));
    }

    // ==================== 用户偏好（方案 9.2 / 8.4） ====================

    [Test]
    public void UserPreferences_CorruptJson_FallsBackToDefaultsWithoutOverwritingFile()
    {
        Directory.CreateDirectory(_productDir);

        // 用例 1：JSON 根不是对象（结构损坏）→ JsonUtility 会解析失败
        const string wrongShape = "[]";
        File.WriteAllText(_prefsPath, wrongShape, new UTF8Encoding(false));

        var prefs = new UserPreferences();
        Assert.DoesNotThrow(prefs.Load);
        Assert.AreEqual(UserPreferences.Defaults.Speed, prefs.Speed, 1e-4f, "损坏时应回落默认速度");
        Assert.AreEqual(UserPreferences.Defaults.BeatsPerBar, prefs.BeatsPerBar);
        Assert.IsFalse(string.IsNullOrEmpty(prefs.LoadDiagnostic), "容错时应保留可诊断信息");
        Assert.AreEqual(wrongShape, File.ReadAllText(_prefsPath), "解析失败时不得覆盖用户原文件");

        // 用例 2：彻底损坏的文本 → 同样不得抛异常、不得覆盖原文件
        const string malformed = "{ \"Speed\": ";
        File.WriteAllText(_prefsPath, malformed, new UTF8Encoding(false));

        var prefs2 = new UserPreferences();
        Assert.DoesNotThrow(prefs2.Load);
        Assert.IsTrue(
            prefs2.Speed >= UserPreferences.Defaults.SpeedMin && prefs2.Speed <= UserPreferences.Defaults.SpeedMax,
            "无论解析与否，速度都必须落在合法范围内");
        Assert.AreEqual(malformed, File.ReadAllText(_prefsPath), "解析失败时不得覆盖用户原文件");
    }

    [Test]
    public void UserPreferences_OutOfRangeValues_AreClampedOnLoad()
    {
        Directory.CreateDirectory(_productDir);
        File.WriteAllText(_prefsPath,
            "{\"LastSongId\":\"keep\",\"LastCameraMode\":42,\"Speed\":99.0,\"PracticeHand\":7," +
            "\"HighlightMode\":9,\"AvatarIndex\":9,\"MetronomeEnabled\":true,\"BeatsPerBar\":0," +
            "\"HighlightLookAhead\":-5.0,\"PracticeToolsCollapsed\":true,\"QualityTier\":5," +
            "\"RecentSongIds\":[\"a\"]}",
            new UTF8Encoding(false));

        var prefs = new UserPreferences();
        prefs.Load();

        Assert.AreEqual(4f, prefs.Speed, 1e-4f, "速度应 clamp 到 0.1–4");
        Assert.AreEqual(1, prefs.BeatsPerBar, "BeatsPerBar 应 clamp 到 1–16");
        Assert.AreEqual(0.05f, prefs.HighlightLookAhead, 1e-4f, "预提示提前量应 clamp 到 0.05–3");
        Assert.AreEqual(2, prefs.AvatarIndex, "AvatarIndex 应 clamp 到 0–2");
        Assert.AreEqual(UserPreferences.Defaults.LastCameraMode, prefs.LastCameraMode, "越界枚举值应回落默认");
        Assert.AreEqual(UserPreferences.Defaults.PracticeHand, prefs.PracticeHand, "越界枚举值应回落默认");
        Assert.AreEqual(UserPreferences.Defaults.HighlightMode, prefs.HighlightMode, "越界枚举值应回落默认");
        Assert.AreEqual(UserPreferences.Defaults.QualityTier, prefs.QualityTier, "越界枚举值应回落默认");
        Assert.AreEqual("keep", prefs.LastSongId, "合法字段应保留");
        Assert.IsTrue(prefs.MetronomeEnabled);
        Assert.AreEqual(1, prefs.RecentSongIds.Count);
    }

    [Test]
    public void UserPreferences_SaveThenLoad_RoundTrips()
    {
        var saved = new UserPreferences
        {
            LastSongId = "abc123",
            LastCameraMode = 2,
            Speed = 1.5f,
            PracticeHand = 1,
            HighlightMode = 2,
            AvatarIndex = 1,
            MetronomeEnabled = true,
            BeatsPerBar = 3,
            HighlightLookAhead = 1.2f,
            PracticeToolsCollapsed = true,
            QualityTier = 0
        };
        saved.RecentSongIds.Add("abc123");
        saved.Save();

        var loaded = new UserPreferences();
        loaded.Load();

        Assert.AreEqual("abc123", loaded.LastSongId);
        Assert.AreEqual(2, loaded.LastCameraMode);
        Assert.AreEqual(1.5f, loaded.Speed, 1e-4f);
        Assert.AreEqual(1, loaded.PracticeHand);
        Assert.AreEqual(2, loaded.HighlightMode);
        Assert.AreEqual(1, loaded.AvatarIndex);
        Assert.IsTrue(loaded.MetronomeEnabled);
        Assert.AreEqual(3, loaded.BeatsPerBar);
        Assert.AreEqual(1.2f, loaded.HighlightLookAhead, 1e-4f);
        Assert.IsTrue(loaded.PracticeToolsCollapsed);
        Assert.AreEqual(0, loaded.QualityTier);
        Assert.AreEqual(1, loaded.RecentSongIds.Count);
        Assert.AreEqual("abc123", loaded.RecentSongIds[0]);
    }

    [Test]
    public void UserPreferences_ResetToDefaults_RestoresProductDefaults()
    {
        var prefs = new UserPreferences
        {
            Speed = 3f,
            BeatsPerBar = 7,
            HighlightLookAhead = 2f,
            AvatarIndex = 2
        };
        prefs.RecentSongIds.Add("x");
        prefs.ResetToDefaults();

        Assert.AreEqual(UserPreferences.Defaults.Speed, prefs.Speed, 1e-4f);
        Assert.AreEqual(UserPreferences.Defaults.BeatsPerBar, prefs.BeatsPerBar);
        Assert.AreEqual(UserPreferences.Defaults.HighlightLookAhead, prefs.HighlightLookAhead, 1e-4f);
        Assert.AreEqual(UserPreferences.Defaults.AvatarIndex, prefs.AvatarIndex);
        Assert.AreEqual(0, prefs.RecentSongIds.Count);
    }

    // ==================== 助手 ====================

    private void BackupFile(string path)
    {
        try
        {
            if (File.Exists(path)) _backupFiles[path] = File.ReadAllBytes(path);
        }
        catch (Exception) { /* 忽略备份失败 */ }
    }

    private static void DeleteFileSafe(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { /* 忽略 */ }
    }

    private static void DeleteAllFilesIn(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir)) File.Delete(file);
        }
        catch (Exception) { /* 忽略 */ }
    }

    // ---- MIDI 字节构造（与本文件用途一致的最小集合） ----

    private static byte[] BuildValidMidi() => BuildMidi(NoteOn(0, 60, 100, 0), NoteOff(0, 60, 480));

    private static byte[] BuildMidiWithoutNotes() => BuildMidi();

    private static byte[] BuildMidi(params byte[][] events)
    {
        var track = new List<byte>();
        track.AddRange(VLQ(0));
        track.AddRange(new byte[]
        {
            0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 // tempo 500000（120 BPM）
        });
        for (int i = 0; i < events.Length; i++) track.AddRange(events[i]);
        track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 }); // End of Track

        var bytes = new List<byte>();
        bytes.AddRange(Ascii("MThd"));
        bytes.AddRange(U32(6));
        bytes.AddRange(U16(0));   // format 0
        bytes.AddRange(U16(1));   // 1 track
        bytes.AddRange(U16(480)); // PPQ
        bytes.AddRange(Ascii("MTrk"));
        bytes.AddRange(U32((uint)track.Count));
        bytes.AddRange(track);
        return bytes.ToArray();
    }

    private static byte[] NoteOn(int channel, int pitch, int velocity, int deltaTicks)
        => Concat(VLQ(deltaTicks), new byte[] { (byte)(0x90 | channel), (byte)pitch, (byte)velocity });

    private static byte[] NoteOff(int channel, int pitch, int deltaTicks)
        => Concat(VLQ(deltaTicks), new byte[] { (byte)(0x80 | channel), (byte)pitch, 0x00 });

    private static byte[] Concat(params byte[][] parts)
    {
        var list = new List<byte>();
        for (int i = 0; i < parts.Length; i++) list.AddRange(parts[i]);
        return list.ToArray();
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] U16(ushort v) => new[] { (byte)(v >> 8), (byte)v };

    private static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static byte[] VLQ(int value)
    {
        var bytes = new List<byte>();
        int buffer = value & 0x7F;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= (value & 0x7F) | 0x80;
        }
        while (true)
        {
            bytes.Add((byte)(buffer & 0xFF));
            if ((buffer & 0x80) != 0) buffer >>= 8;
            else break;
        }
        return bytes.ToArray();
    }
}
