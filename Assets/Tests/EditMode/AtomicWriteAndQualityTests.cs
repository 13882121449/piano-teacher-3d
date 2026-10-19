using System;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Core;

/// <summary>
/// 原子写入与画质 Auto/覆盖优先级测试（EditMode）。
/// 对应优化方案 P1-10「曲库和偏好保存的事务性与错误恢复」与 P1-07「统一画质推荐与用户偏好优先级」。
/// 全部用例只操作系统临时目录，不触碰用户真实曲库 / 偏好文件。
/// </summary>
public class AtomicWriteAndQualityTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_Atomic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch (Exception)
        {
            // 清理失败不影响断言结果
        }
    }

    // ==================== 原子写入 ====================

    [Test]
    public void WriteAllText_NewFile_WritesContentAndLeavesNoTemp()
    {
        string path = Path.Combine(_dir, "library.json");

        bool ok = AtomicFileWrite.TryWriteAllText(path, "{\"v\":1}", out string error);

        Assert.IsTrue(ok, $"首次写入应成功：{error}");
        Assert.AreEqual("{\"v\":1}", File.ReadAllText(path));
        Assert.IsFalse(File.Exists(path + AtomicFileWrite.TempSuffix), "成功后不得残留 .tmp");
    }

    [Test]
    public void WriteAllText_ExistingFile_ReplacesAndKeepsBackup()
    {
        string path = Path.Combine(_dir, "library.json");
        File.WriteAllText(path, "old");

        bool ok = AtomicFileWrite.TryWriteAllText(path, "new", out string error);

        Assert.IsTrue(ok, $"覆盖写入应成功：{error}");
        Assert.AreEqual("new", File.ReadAllText(path));
        Assert.IsTrue(File.Exists(path + AtomicFileWrite.BackupSuffix), "覆盖前应保留最近一次有效备份");
        Assert.AreEqual("old", File.ReadAllText(path + AtomicFileWrite.BackupSuffix));
        Assert.IsFalse(File.Exists(path + AtomicFileWrite.TempSuffix));
    }

    [Test]
    public void WriteAllText_EmptyPath_FailsWithoutThrowing()
    {
        bool ok = true;
        Assert.DoesNotThrow(() => ok = AtomicFileWrite.TryWriteAllText(null, "x", out _));
        Assert.IsFalse(ok);
    }

    // ==================== 原子复制（导入曲目） ====================

    [Test]
    public void CopyFile_OverwritesExistingDestination()
    {
        string source = Path.Combine(_dir, "a.mid");
        string destination = Path.Combine(_dir, "copy", "a.mid");
        File.WriteAllText(source, "MThd-data");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.WriteAllText(destination, "stale");

        bool ok = AtomicFileWrite.TryCopyFile(source, destination, out string error);

        Assert.IsTrue(ok, $"原子复制应成功：{error}");
        Assert.AreEqual("MThd-data", File.ReadAllText(destination));
        Assert.IsFalse(File.Exists(destination + AtomicFileWrite.TempSuffix));
    }

    [Test]
    public void CopyFile_MissingSource_FailsAndLeavesNoArtifacts()
    {
        string source = Path.Combine(_dir, "missing.mid");
        string destination = Path.Combine(_dir, "missing_copy.mid");

        bool ok = true;
        Assert.DoesNotThrow(() => ok = AtomicFileWrite.TryCopyFile(source, destination, out _));

        Assert.IsFalse(ok);
        Assert.IsFalse(File.Exists(destination), "失败不得产生目标文件");
        Assert.IsFalse(File.Exists(destination + AtomicFileWrite.TempSuffix), "失败必须清理临时文件");
    }

    // ==================== 画质 Auto / 用户覆盖 ====================

    [Test]
    public void ResolveQualityTier_WithoutOverride_UsesDeviceRecommendation()
    {
        var prefs = new UserPreferences();

        Assert.IsFalse(prefs.QualityTierUserOverride, "默认必须是 Auto（首次启动不盲目应用 High）");
        Assert.AreEqual(QualityProfile.DetectRecommended(), prefs.ResolveQualityTier());
    }

    [Test]
    public void ResolveQualityTier_WithOverride_UsesSavedTier()
    {
        var prefs = new UserPreferences();

        prefs.SetQualityTierOverride(QualityTier.High);
        Assert.IsTrue(prefs.QualityTierUserOverride);
        Assert.AreEqual(QualityTier.High, prefs.ResolveQualityTier());

        prefs.SetQualityTierOverride(QualityTier.Standard);
        Assert.AreEqual(QualityTier.Standard, prefs.ResolveQualityTier());
    }

    [Test]
    public void UseAutomaticQuality_ClearsOverride()
    {
        var prefs = new UserPreferences();
        prefs.SetQualityTierOverride(QualityTier.High);

        prefs.UseAutomaticQuality();

        Assert.IsFalse(prefs.QualityTierUserOverride);
        Assert.AreEqual(QualityProfile.DetectRecommended(), prefs.ResolveQualityTier(), "恢复自动后应回到设备建议");
    }
}
