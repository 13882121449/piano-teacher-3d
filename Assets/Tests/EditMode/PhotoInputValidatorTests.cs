using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using PianoTeacher3D.Customization;

/// <summary>
/// 照片输入校验测试（方案 9.3）：格式白名单、魔数判定、大小/尺寸/像素上限、损坏图、合法图。
/// 全部只在 <see cref="Path.GetTempPath"/> 的临时目录操作，不触碰用户真实照片。
/// </summary>
public class PhotoInputValidatorTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_Photo_" + Guid.NewGuid().ToString("N"));
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
            // 清理失败不影响断言
        }
    }

    // ==================== 扩展名 / 魔数 ====================

    [Test]
    public void ValidateBytes_NonWhitelistedExtension_IsRejected()
    {
        byte[] png = CreatePngBytes(128, 128);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(png, "photo.gif");

        Assert.IsFalse(result.Ok, "非白名单扩展名必须被拒绝");
        Assert.AreEqual(PhotoRejectReason.UnsupportedExtension, result.ReasonCode);
        Assert.IsNotEmpty(result.Reason, "必须说明发生了什么");
        Assert.IsNotEmpty(result.Guidance, "必须说明下一步怎么办");
    }

    [Test]
    public void ValidateBytes_RealPngWithJpegExtension_PassesByMagic()
    {
        byte[] png = CreatePngBytes(96, 96);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(png, "mismatch.jpg");
        try
        {
            Assert.IsTrue(result.Ok, $"真实格式应由魔数判定：{result.Reason}");
            Assert.AreEqual(PhotoFormat.Png, result.Format);
            Assert.AreEqual(96, result.Width);
            Assert.AreEqual(96, result.Height);
            Assert.IsNotNull(result.Texture);
        }
        finally
        {
            Destroy(result.Texture);
        }
    }

    [Test]
    public void ValidateBytes_GarbageWithPngExtension_IsRejectedAsUnsupportedFormat()
    {
        byte[] garbage = new byte[512];
        for (int i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i % 251);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(garbage, "fake.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.UnsupportedFormat, result.ReasonCode);
    }

    [Test]
    public void SniffSignature_RecognisesPngAndJpeg()
    {
        byte[] png = CreatePngBytes(64, 64);
        byte[] jpegHeader = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

        Assert.AreEqual(PhotoFormat.Png, PhotoInputValidator.SniffSignature(png));
        Assert.AreEqual(PhotoFormat.Jpeg, PhotoInputValidator.SniffSignature(jpegHeader));
        Assert.AreEqual(PhotoFormat.Unknown, PhotoInputValidator.SniffSignature(new byte[] { 1, 2, 3 }));
        Assert.AreEqual(PhotoFormat.Unknown, PhotoInputValidator.SniffSignature(null));
    }

    // ==================== 空 / 超大 / 损坏 ====================

    [Test]
    public void ValidateBytes_EmptyFile_IsRejected()
    {
        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(new byte[0], "empty.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.EmptyFile, result.ReasonCode);
    }

    [Test]
    public void ValidateBytes_OverByteLimit_IsRejectedBeforeDecode()
    {
        var bytes = new byte[(int)PhotoInputValidator.MaxBytes + 1];
        bytes[0] = 0x89;
        bytes[1] = 0x50;

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(bytes, "huge.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.TooLarge, result.ReasonCode);
    }

    [Test]
    public void ValidateBytes_CorruptedPng_IsRejectedAsDecodeFailed()
    {
        // 有正确签名但 IHDR 之后全是垃圾：能过魔数，过不了解码
        var broken = new byte[256];
        Array.Copy(CreatePngBytes(64, 64), broken, 8);
        for (int i = 8; i < broken.Length; i++) broken[i] = 0x5A;

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(broken, "broken.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.DecodeFailed, result.ReasonCode);
        Assert.IsNotEmpty(result.Guidance);
    }

    // ==================== 尺寸 / 像素上限（头部预检，不分配纹理） ====================

    [Test]
    public void ValidateBytes_TooLargeDimension_IsRejectedFromHeader()
    {
        byte[] header = CraftPngHeader(5000, 1000);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(header, "wide.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.TooLargeDimension, result.ReasonCode);
        Assert.AreEqual(5000, result.Width);
    }

    [Test]
    public void ValidateBytes_TooManyPixels_IsRejectedFromHeader()
    {
        // 4096×4096 = 16,777,216 像素，单边合法但总像素超过 16,000,000
        byte[] header = CraftPngHeader(4096, 4096);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(header, "dense.png");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.TooManyPixels, result.ReasonCode);
    }

    [Test]
    public void ValidateBytes_TooSmallDimension_IsRejected()
    {
        // 32×32 的真实 PNG：能解码，但短边小于下限
        byte[] small = CreatePngBytes(32, 32);
        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(small, "small.png");
        try
        {
            Assert.IsFalse(result.Ok);
            Assert.AreEqual(PhotoRejectReason.TooSmall, result.ReasonCode);
        }
        finally
        {
            Destroy(result.Texture);
        }
    }

    // ==================== 合法用例 ====================

    [Test]
    public void ValidateBytes_ValidPng_PassesWithDecodedTexture()
    {
        byte[] png = CreatePngBytes(128, 128);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(png, "good.png");
        try
        {
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(128, result.Width);
            Assert.AreEqual(128, result.Height);
            Assert.AreEqual(png.Length, result.Bytes);
            Assert.IsNotNull(result.Texture);
            Assert.IsEmpty(result.Reason);
            Assert.IsEmpty(result.Guidance);
        }
        finally
        {
            Destroy(result.Texture);
        }
    }

    [Test]
    public void ValidateBytes_UpperCaseExtension_IsAccepted()
    {
        byte[] png = CreatePngBytes(80, 80);

        PhotoValidationResult result = PhotoInputValidator.ValidateBytes(png, "UPPER.PNG");
        try
        {
            Assert.IsTrue(result.Ok, result.Reason);
        }
        finally
        {
            Destroy(result.Texture);
        }
    }

    // ==================== 文件路径 / 后台解码 ====================

    [Test]
    public void ValidateFile_MissingFile_GivesReadableReason()
    {
        string missing = Path.Combine(_dir, "not-there.png");

        PhotoValidationResult result = PhotoInputValidator.ValidateFile(missing);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PhotoRejectReason.Unreadable, result.ReasonCode);
        Assert.IsNotEmpty(result.Reason);
        Assert.IsNotEmpty(result.Guidance);
    }

    [Test]
    public void ValidateFile_ValidPngOnDisk_Passes()
    {
        string path = Path.Combine(_dir, "on-disk.png");
        File.WriteAllBytes(path, CreatePngBytes(100, 100));

        PhotoValidationResult result = PhotoInputValidator.ValidateFile(path);
        try
        {
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(100, result.Width);
        }
        finally
        {
            Destroy(result.Texture);
        }
    }

    [Test]
    public void DecodeJob_ReadsOnBackgroundAndDecodesOnPoll()
    {
        string path = Path.Combine(_dir, "job.png");
        File.WriteAllBytes(path, CreatePngBytes(72, 72));

        PhotoDecodeJob job = PhotoDecodeJob.Start(path);
        var watch = Stopwatch.StartNew();
        while (!job.Poll() && watch.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(1);
        }

        try
        {
            Assert.IsTrue(job.IsDone, "后台读图应在 5 秒内完成");
            Assert.IsTrue(job.Result.Ok, job.Result.Reason);
            Assert.AreEqual(72, job.Result.Width);
        }
        finally
        {
            Destroy(job.Result.Texture);
        }
    }

    [Test]
    public void DecodeJob_Cancel_ProducesNoResult()
    {
        string path = Path.Combine(_dir, "cancel.png");
        File.WriteAllBytes(path, CreatePngBytes(72, 72));

        PhotoDecodeJob job = PhotoDecodeJob.Start(path);
        job.Cancel();

        Assert.IsTrue(job.Poll(), "取消后应直接结束");
        Assert.IsTrue(job.IsCancelled);
        Assert.IsNull(job.Result.Texture, "取消后不得产出纹理");
    }

    // ==================== 测试素材 ====================

    private static byte[] CreatePngBytes(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(i % 255), 128, 64, 255);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        byte[] bytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        return bytes;
    }

    /// <summary>只伪造 PNG 签名 + IHDR 宽高，用于验证「分区前预检」在不分配纹理时就能拒绝。</summary>
    private static byte[] CraftPngHeader(int width, int height)
    {
        var bytes = new byte[64];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(signature, bytes, signature.Length);

        bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        bytes[24] = 8;   // bit depth
        bytes[25] = 6;   // RGBA
        return bytes;
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null) Object.DestroyImmediate(texture);
    }
}
