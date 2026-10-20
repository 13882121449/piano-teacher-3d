using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;

/// <summary>
/// 生成任务状态机测试（方案 9.4 / P1-4）：成功、取消幂等、超时、Network 重试上限、
/// InvalidInput 不重试、取消后不再回调、结果二次校验。
/// 全部用注入时钟推进，零帧、零等待、零网络。
/// </summary>
public class CustomizationImageJobTests
{
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _now = 0f;
    }

    private Func<float> Clock => () => _now;

    // ==================== 成功 / 事件 ====================

    [Test]
    public void Tick_StepCompletes_MovesToSucceededAndFiresOnce()
    {
        Texture2D resultTexture = CreateTexture(128, 128);
        try
        {
            int completedCount = 0;
            var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
                (request, startedAt, now) => now - startedAt >= 1f
                    ? ImageAttemptSnapshot.Succeeded(resultTexture)
                    : ImageAttemptSnapshot.Running(0.5f));
            job.Completed += _ => completedCount++;

            job.Tick(0f);
            Assert.AreEqual(ImageGenerationState.Running, job.State);
            Assert.AreEqual(0, completedCount);

            _now = 1f;
            job.Tick(_now);

            Assert.AreEqual(ImageGenerationState.Succeeded, job.State);
            Assert.AreSame(resultTexture, job.Result);
            Assert.AreEqual(1, completedCount, "结束通知只能触发一次");

            job.Tick(2f);
            Assert.AreEqual(1, completedCount, "终态后继续 Tick 不得重复回调");
        }
        finally
        {
            Destroy(resultTexture);
        }
    }

    [Test]
    public void Tick_SucceededWithUndersizedTexture_IsTreatedAsDecodeFailure()
    {
        Texture2D tiny = CreateTexture(32, 32);
        try
        {
            var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
                (request, startedAt, now) => ImageAttemptSnapshot.Succeeded(tiny));

            job.Tick(0f);

            Assert.AreEqual(ImageGenerationState.Failed, job.State);
            Assert.AreEqual(ImageGenerationErrorCategory.Decode, job.ErrorCategory);
            Assert.IsNull(job.Result);
            Assert.IsNotEmpty(job.ErrorMessage);
            Assert.IsNotEmpty(job.ErrorGuidance);
        }
        finally
        {
            Destroy(tiny);
        }
    }

    [Test]
    public void Tick_SucceededWithoutTexture_IsTreatedAsDecodeFailure()
    {
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => ImageAttemptSnapshot.Succeeded(null));

        job.Tick(0f);

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(ImageGenerationErrorCategory.Decode, job.ErrorCategory);
    }

    // ==================== 取消 ====================

    [Test]
    public void Cancel_IsIdempotent_AndNeverProducesResultOrCallback()
    {
        bool completed = false;
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => ImageAttemptSnapshot.Running(0.3f));
        job.Completed += _ => completed = true;

        job.Tick(0f);
        job.Cancel();
        job.Cancel();

        Assert.AreEqual(ImageGenerationState.Canceled, job.State);
        Assert.IsNull(job.Result);
        Assert.IsFalse(completed, "取消不得回调 UI");

        _now = 100f;
        job.Tick(_now);

        Assert.AreEqual(ImageGenerationState.Canceled, job.State, "取消后状态不得再变化");
        Assert.IsNull(job.Result);
        Assert.IsFalse(completed);
    }

    [Test]
    public void Cancel_AfterSuccess_DoesNotChangeTerminalState()
    {
        Texture2D texture = CreateTexture(128, 128);
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => ImageAttemptSnapshot.Succeeded(texture));

        try
        {
            job.Tick(0f);
            job.Cancel();

            Assert.AreEqual(ImageGenerationState.Succeeded, job.State);
            Assert.AreSame(texture, job.Result);
        }
        finally
        {
            Destroy(texture);
        }
    }

    // ==================== 超时 ====================

    [Test]
    public void Tick_PastDeadline_BecomesTimedOutAndDoesNotRetry()
    {
        int attempts = 0;
        var job = ImageGenerationJob.Create(NewRequest(5f), "test", Clock,
            (request, startedAt, now) =>
            {
                attempts++;
                return ImageAttemptSnapshot.Running(0.1f);
            });

        job.Tick(0f);
        _now = 6f;
        job.Tick(_now);

        Assert.AreEqual(ImageGenerationState.TimedOut, job.State);
        Assert.AreEqual(ImageGenerationErrorCategory.Timeout, job.ErrorCategory);
        Assert.AreEqual(0, job.RetryCount, "超时不得自动重试");
        Assert.AreEqual(1, attempts, "超时后不得再调用提供方");
    }

    // ==================== 重试边界 ====================

    [Test]
    public void Tick_NetworkFailure_RetriesAtMostTwice()
    {
        int attempts = 0;
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) =>
            {
                attempts++;
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.Network,
                    "网络中断", "请检查网络后重试。");
            });

        job.Tick(0f);           // 第 1 次失败 → 重试 1
        _now = 1f;
        job.Tick(_now);         // 第 2 次失败 → 重试 2
        _now = 2f;
        job.Tick(_now);         // 第 3 次失败 → 放弃

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(ImageGenerationJob.MaxRetries, job.RetryCount);
        Assert.AreEqual(3, attempts, "首次 + 2 次重试 = 3 次尝试");
        Assert.AreEqual(ImageGenerationErrorCategory.Network, job.ErrorCategory);
        Assert.IsNotEmpty(job.ErrorMessage);
        Assert.IsNotEmpty(job.ErrorGuidance);
    }

    [Test]
    public void Tick_ServerFailure_IsRetryable()
    {
        int attempts = 0;
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) =>
            {
                attempts++;
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.Server,
                    "服务端错误", "请稍后重试。");
            });

        job.Tick(0f);

        Assert.AreEqual(1, job.RetryCount, "服务端错误应自动重试");
        Assert.AreEqual(ImageGenerationState.Running, job.State);
        Assert.AreEqual(1, attempts, "同一帧内只推进一次尝试");
    }

    [Test]
    public void Tick_InvalidInput_IsNotRetried()
    {
        int attempts = 0;
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) =>
            {
                attempts++;
                return ImageAttemptSnapshot.Failed(ImageGenerationErrorCategory.InvalidInput,
                    "源图不可用", "请换一张照片。");
            });

        job.Tick(0f);
        _now = 1f;
        job.Tick(_now);

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(0, job.RetryCount, "InvalidInput 不重试");
        Assert.AreEqual(1, attempts);
    }

    [Test]
    public void Tick_ContentRejected_IsNotRetried()
    {
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => ImageAttemptSnapshot.Failed(
                ImageGenerationErrorCategory.ContentRejected, "照片不合规", "请换一张照片。"));

        job.Tick(0f);

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(0, job.RetryCount);
    }

    [Test]
    public void Tick_DecodeFailure_IsNotRetried()
    {
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => ImageAttemptSnapshot.Failed(
                ImageGenerationErrorCategory.Decode, "结果解码失败", "请重新生成。"));

        job.Tick(0f);

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(0, job.RetryCount);
    }

    [Test]
    public void IsRetryable_MatchesSpec()
    {
        Assert.IsTrue(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.Network));
        Assert.IsTrue(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.Server));
        Assert.IsFalse(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.InvalidInput));
        Assert.IsFalse(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.ContentRejected));
        Assert.IsFalse(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.Decode));
        Assert.IsFalse(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.Timeout));
        Assert.IsFalse(ImageGenerationJob.IsRetryable(ImageGenerationErrorCategory.Canceled));
    }

    [Test]
    public void Tick_ProviderThrows_IsCategorizedAsServer()
    {
        var job = ImageGenerationJob.Create(NewRequest(30f), "test", Clock,
            (request, startedAt, now) => throw new InvalidOperationException("boom"));

        job.Tick(0f);
        _now = 1f;
        job.Tick(_now);
        _now = 2f;
        job.Tick(_now);

        Assert.AreEqual(ImageGenerationState.Failed, job.State);
        Assert.AreEqual(ImageGenerationErrorCategory.Server, job.ErrorCategory);
        Assert.AreEqual(ImageGenerationJob.MaxRetries, job.RetryCount);
    }

    // ==================== 结果校验器 ====================

    [Test]
    public void ResultValidator_AcceptsNormalTexture_AndRejectsOthers()
    {
        Texture2D ok = CreateTexture(256, 256);
        Texture2D small = CreateTexture(16, 16);
        try
        {
            Assert.IsTrue(GenerationResultValidator.TryValidate(ok, out _, out _));

            Assert.IsFalse(GenerationResultValidator.TryValidate(null, out string reason, out string guidance));
            Assert.IsNotEmpty(reason);
            Assert.IsNotEmpty(guidance);

            Assert.IsFalse(GenerationResultValidator.TryValidate(small, out _, out _));
        }
        finally
        {
            Destroy(ok);
            Destroy(small);
        }
    }

    // ==================== 素材 ====================

    private static GenerationRequest NewRequest(float timeout)
        => GenerationRequest.Create(null, CharacterStyle.Realistic, "req-test", timeout);

    private static Texture2D CreateTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(40, 60, 80, 255);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null) Object.DestroyImmediate(texture);
    }
}
