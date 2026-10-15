using System.IO;
using NUnit.Framework;
using PianoTeacher3D.MIDI;

/// <summary>
/// MidiParser.ParseFile 的异常输入 / 资源加载失败测试（EditMode，方案 8.1 异常场景）：
/// 覆盖空路径、不存在、目录路径、空文件、垃圾内容、文件被占用等情形，
/// 要求一律返回 Fail（含原因）而**不抛异常**。
/// </summary>
public class MidiParserFileTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_FileTests");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* 清理失败不影响测试结论 */ }
    }

    [Test]
    public void ParseFile_NullPath_ReturnsFail()
    {
        var r = MidiParser.ParseFile(null);
        Assert.IsFalse(r.Success);
        Assert.IsFalse(string.IsNullOrEmpty(r.Error));
    }

    [Test]
    public void ParseFile_EmptyPath_ReturnsFail()
    {
        var r = MidiParser.ParseFile("");
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void ParseFile_NonExistentFile_ReturnsFailWithReason()
    {
        var r = MidiParser.ParseFile(Path.Combine(_dir, "no_such_file.mid"));
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Error.Contains("不存在"), $"错误信息应说明原因，实际：{r.Error}");
    }

    [Test]
    public void ParseFile_DirectoryPath_ReturnsFail()
    {
        var r = MidiParser.ParseFile(_dir); // 传目录而非文件
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void ParseFile_EmptyFile_ReturnsFail()
    {
        var path = Path.Combine(_dir, "empty.mid");
        File.WriteAllBytes(path, new byte[0]);
        var r = MidiParser.ParseFile(path);
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void ParseFile_GarbageContent_ReturnsFail()
    {
        var path = Path.Combine(_dir, "garbage.mid");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        var r = MidiParser.ParseFile(path);
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void ParseFile_TruncatedHeaderOnly_ReturnsFail()
    {
        // 只有 MThd 头、缺少 MTrk 轨道块
        var path = Path.Combine(_dir, "headeronly.mid");
        File.WriteAllBytes(path, new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, // "MThd" + len 6
            0x00, 0x00, 0x00, 0x01, 0x01, 0xE0              // format0 / 1 track / PPQ 480
        });
        var r = MidiParser.ParseFile(path);
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void ParseFile_LockedFile_ReturnsFailInsteadOfThrowing()
    {
        var path = Path.Combine(_dir, "locked.mid");
        File.WriteAllBytes(path, new byte[64]);

        // 独占占用文件：ParseFile 必须返回 Fail（IOException 被捕获），而不是抛出异常
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            MidiParseResult r = null;
            Assert.DoesNotThrow(() => r = MidiParser.ParseFile(path));
            Assert.IsFalse(r.Success);
            Assert.IsTrue(r.Error.Contains("读取失败"), $"应报告读取失败，实际：{r.Error}");
        }
    }
}
