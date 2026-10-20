using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Core;
using PianoTeacher3D.Customization;

/// <summary>
/// 照片个性化编排测试（方案 9.2 / 9.4 / 9.6）：
/// 未同意不得提交、重复提交被拒、生成失败保留原外观、预览未确认不得应用、删除后本地文件消失。
/// 全部使用临时目录 + 注入时钟，不联网、不依赖真实文件对话框。
/// </summary>
public class CustomizationServiceTests
{
    private string _root;
    private string _photoPath;
    private float _now;

    private AppearanceStorage _storage;
    private PrivacyConsentController _consent;
    private FakeServiceView _view;
    private CharacterCustomizationService _service;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_Custom_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _photoPath = Path.Combine(_root, "source.png");
        File.WriteAllBytes(_photoPath, CreatePngBytes(128, 128));

        _now = 1f;
        _storage = new AppearanceStorage(Path.Combine(_root, "customization"));
        _consent = new PrivacyConsentController(_storage.Root,
            () => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        _view = new FakeServiceView();
        _service = new CharacterCustomizationService(_storage, _consent,
            new MockImageGenerationProvider(() => _now, 0.1f));
        Assert.IsTrue(_service.BindCharacter(_view, out string bindError), bindError);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Applier?.ReleaseAppliedResources();
        _view?.DestroyHeldTextures();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (Exception)
        {
            // 清理失败不影响断言
        }
    }

    // ==================== 隐私门禁 ====================

    [Test]
    public void Submit_WithoutConsent_IsRejectedAndNothingHappens()
    {
        bool ok = _service.Submit(_photoPath, CharacterStyle.Realistic, out string error);

        Assert.IsFalse(ok, "未同意隐私说明不得提交");
        StringAssert.Contains("同意", error);
        Assert.IsFalse(_service.IsBusy);
        Assert.AreEqual(CustomizationPhase.Idle, _service.Phase);
        Assert.IsFalse(_storage.HasStoredData(_view.CharacterId), "被拒绝时不得写任何文件");
    }

    [Test]
    public void Submit_AfterConsent_Succeeds()
    {
        GrantConsent();

        bool ok = _service.Submit(_photoPath, CharacterStyle.Cartoon, out string error);

        Assert.IsTrue(ok, error);
        Assert.IsTrue(_service.IsBusy);
        Assert.AreEqual(CustomizationPhase.ValidatingPhoto, _service.Phase);
    }

    [Test]
    public void Submit_TwiceWhileBusy_SecondIsRejected()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));

        bool ok = _service.Submit(_photoPath, CharacterStyle.Cartoon, out string error);

        Assert.IsFalse(ok, "同一时刻只允许一个在飞任务");
        Assert.IsNotEmpty(error);
    }

    [Test]
    public void Submit_WhenProviderUnavailable_IsRejectedAndKeepsAppearance()
    {
        GrantConsent();
        _service.SetProvider(new UnavailableProvider());

        bool ok = _service.Submit(_photoPath, CharacterStyle.Realistic, out string error);

        Assert.IsFalse(ok);
        Assert.IsNotEmpty(error);
        Assert.IsFalse(_service.IsBusy);
        Assert.AreEqual(0, _view.ApplyCallCount);
    }

    // ==================== 预览 → 应用 ====================

    [Test]
    public void Pump_ToPreviewReady_DoesNotTouchCharacter()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));

        PumpUntilIdle();

        Assert.AreEqual(CustomizationPhase.PreviewReady, _service.Phase);
        Assert.IsTrue(_service.HasPreview, "应产生预览纹理");
        Assert.AreEqual(0, _view.ApplyCallCount, "预览阶段绝不能改动角色外观");
        Assert.IsTrue(_storage.HasStoredData(_view.CharacterId), "原图应已落盘");
        Assert.IsEmpty(_service.LastErrorReason);
    }

    [Test]
    public void ApplyPreview_WithoutPreview_IsRejected()
    {
        bool ok = _service.ApplyPreview(AppearanceSlot.Thumbnail, out string error);

        Assert.IsFalse(ok);
        Assert.IsNotEmpty(error);
        Assert.AreEqual(0, _view.ApplyCallCount);
    }

    [Test]
    public void ApplyPreview_AfterPreview_AppliesAndRecordsUndoPoint()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();

        bool ok = _service.ApplyPreview(AppearanceSlot.Thumbnail, out string error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(1, _view.ApplyCallCount);
        Assert.IsNotNull(_view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.AreEqual(CustomizationPhase.Applied, _service.Phase);
        Assert.IsTrue(_service.CanUndo, "应用后必须记录可撤销点");
        Assert.IsFalse(_service.HasPreview, "应用后预览所有权已转交");
        Assert.IsTrue(File.Exists(Path.Combine(_storage.GetCharacterDirectory(_view.CharacterId),
            AppearanceStorage.ResultFileName)), "生成结果应已落盘");
    }

    [Test]
    public void ApplyPreview_UnsupportedSlot_IsRejected()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();

        bool ok = _service.ApplyPreview(AppearanceSlot.BodyTint, out string error);

        Assert.IsFalse(ok);
        StringAssert.Contains("不支持", error);
        Assert.AreEqual(0, _view.ApplyCallCount);
        Assert.IsTrue(_service.HasPreview, "应用失败时预览应保留，便于重试");
    }

    [Test]
    public void RestoreDefault_AfterApply_ReturnsToDefaultAppearance()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));

        Assert.IsTrue(_service.RestoreDefault(AppearanceSlot.Thumbnail, out string error), error);

        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsFalse(_service.Applier.HasApplied(AppearanceSlot.Thumbnail));
    }

    // ==================== 失败不影响外观 ====================

    [Test]
    public void GenerationFailure_KeepsAppearanceAndReportsReason()
    {
        GrantConsent();
        _service.SetProvider(new FailingProvider(() => _now));
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));

        PumpUntilIdle();

        Assert.AreEqual(CustomizationPhase.Idle, _service.Phase);
        Assert.AreEqual(0, _view.ApplyCallCount, "生成失败不得改动外观");
        Assert.IsFalse(_service.HasPreview);
        Assert.IsNotEmpty(_service.LastErrorReason);
        Assert.IsNotEmpty(_service.LastErrorGuidance);
    }

    [Test]
    public void Cancel_WhileGenerating_StopsWithoutResult()
    {
        GrantConsent();
        _service.SetProvider(new MockImageGenerationProvider(() => _now, 10f));
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));

        // 推进到「读图完成、开始生成」
        var watch = Stopwatch.StartNew();
        while (_service.Phase != CustomizationPhase.Generating && watch.ElapsedMilliseconds < 4000)
        {
            _now += 0.01f;
            _service.Tick(_now);
            Thread.Sleep(2);
        }

        Assert.AreEqual(CustomizationPhase.Generating, _service.Phase);
        Assert.IsTrue(_service.Cancel(out string cancelError), cancelError);
        Assert.IsFalse(_service.IsBusy);
        Assert.IsFalse(_service.HasPreview);
        Assert.AreEqual(0, _view.ApplyCallCount);
    }

    // ==================== 删除 ====================

    [Test]
    public void DeletePhotoAndResult_RemovesLocalFilesAndRestoresDefault()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));

        string characterDirectory = _storage.GetCharacterDirectory(_view.CharacterId);
        Assert.IsTrue(Directory.Exists(characterDirectory), "前置条件：应已创建角色目录");

        bool ok = _service.DeletePhotoAndResult(out string error);

        Assert.IsTrue(ok, error);
        Assert.IsFalse(Directory.Exists(characterDirectory), "删除后角色目录应消失");
        Assert.IsFalse(_storage.HasStoredData(_view.CharacterId));
        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail), "删除照片后应恢复默认外观");
        Assert.IsFalse(_service.HasPreview);
        Assert.IsFalse(_service.HasSourcePhoto);
    }

    [Test]
    public void DeletePhotoAndResult_WhenNothingStored_StillSucceeds()
    {
        GrantConsent();
        _service.Submit(_photoPath, CharacterStyle.Realistic, out _);
        PumpUntilIdle();

        Assert.IsTrue(_service.DeletePhotoAndResult(out string error), error);
        Assert.IsTrue(_service.DeletePhotoAndResult(out error), "重复删除应幂等");
    }

    // ==================== 恢复上次保存的外观 ====================

    [Test]
    public void TryRestoreStoredAppearance_NewSession_RestoresAppliedSlot()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out string applyError), applyError);

        // 模拟重新打开应用：新 service + 同角色的新视图实例
        var view2 = new FakeServiceView();
        var service2 = new CharacterCustomizationService(_storage, _consent,
            new MockImageGenerationProvider(() => _now, 0.1f));
        Assert.IsTrue(service2.BindCharacter(view2, out string bindError), bindError);

        Assert.IsTrue(service2.TryRestoreStoredAppearance(out string restoreError), restoreError);

        Assert.IsNotNull(view2.GetApplied(AppearanceSlot.Thumbnail), "重启后应能恢复已保存的缩略图");
        Assert.AreEqual(CustomizationPhase.Applied, service2.Phase);
        Assert.IsTrue(service2.CanUndo, "恢复应记录为可撤销点");

        view2.DestroyHeldTextures();
    }

    [Test]
    public void TryRestoreStoredAppearance_WithoutSavedData_IsRejected()
    {
        Assert.IsFalse(_service.TryRestoreStoredAppearance(out string error));
        StringAssert.Contains("还没有保存", error);
        Assert.AreEqual(0, _view.ApplyCallCount);
    }

    [Test]
    public void TryRestoreStoredAppearance_IsIdempotent()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));

        Assert.IsTrue(_service.TryRestoreStoredAppearance(out string error), error);
        Assert.AreEqual(1, _view.ApplyCallCount, "已应用同一槽位时不应重复 apply");
    }

    // ==================== 角色切换（视图实例会重建） ====================

    [Test]
    public void BindCharacter_SameCharacterNewInstance_MigratesAppliedAppearance()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));
        Texture2D applied = _view.GetApplied(AppearanceSlot.Thumbnail);
        Assert.IsNotNull(applied);

        var newView = new FakeServiceView();   // 同一角色 ID、新实例（内置包装重建）

        Assert.IsTrue(_service.BindCharacter(newView, out string migrateWarning), migrateWarning);

        Assert.AreSame(newView, _service.View);
        Assert.AreSame(applied, newView.GetApplied(AppearanceSlot.Thumbnail), "同一角色换实例后外观必须保留");
        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail), "旧实例必须已解引用");
        Assert.IsTrue(applied != null, "迁移不得销毁纹理");

        _view = newView;
    }

    [Test]
    public void BindCharacter_OtherCharacter_ReleasesOldAppearanceTextures()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));
        Texture2D applied = _view.GetApplied(AppearanceSlot.Thumbnail);
        GeneratedAppearanceApplier oldApplier = _service.Applier;

        var other = new FakeServiceView("other-char");

        Assert.IsTrue(_service.BindCharacter(other, out _), "换角色本身应成功");

        Assert.IsTrue(applied == null, "换到别的角色后，旧角色的纹理必须被回收（不能指望视图 Dispose）");
        Assert.IsNull(other.GetApplied(AppearanceSlot.Thumbnail));
        Assert.AreNotSame(oldApplier, _service.Applier, "换角色必须换成新应用器（旧的已释放）");
        Assert.IsFalse(_service.CanUndo, "换角色后撤销历史必须清空");

        _view = other;
    }

    [Test]
    public void BindCharacter_Null_UnbindsAndReleasesTextures()
    {
        GrantConsent();
        Assert.IsTrue(_service.Submit(_photoPath, CharacterStyle.Realistic, out _));
        PumpUntilIdle();
        Assert.IsTrue(_service.ApplyPreview(AppearanceSlot.Thumbnail, out _));
        Texture2D applied = _view.GetApplied(AppearanceSlot.Thumbnail);

        Assert.IsFalse(_service.BindCharacter(null, out string error));
        Assert.IsNotEmpty(error);

        Assert.IsNull(_service.View);
        Assert.IsNull(_service.Applier);
        Assert.IsTrue(applied == null, "解绑时必须回收自有纹理");
    }

    // ==================== 隐私同意持久化 ====================

    [Test]
    public void Consent_IsPersistedWithTimestamp()
    {
        GrantConsent();

        var reloaded = new PrivacyConsentController(_storage.Root,
            () => new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(reloaded.IsGranted, "同意状态应跨实例持久化");
        Assert.AreEqual(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), reloaded.GrantedAtUtc);
        Assert.IsTrue(File.Exists(reloaded.ConsentFilePath));
    }

    [Test]
    public void Consent_TextCoversFiveRequiredPoints()
    {
        string text = PrivacyConsentController.ComposeConsentText();

        StringAssert.Contains("用途", text);
        StringAssert.Contains("处理方", text);
        StringAssert.Contains("是否保存", text);
        StringAssert.Contains("保存时长", text);
        StringAssert.Contains("删除方式", text);
    }

    // ==================== 助手 ====================

    private void GrantConsent()
    {
        Assert.IsTrue(_consent.TryGrant(out string error), error);
    }

    private void PumpUntilIdle(int maxMilliseconds = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (_service.IsBusy && watch.ElapsedMilliseconds < maxMilliseconds)
        {
            _now += 0.05f;
            _service.Tick(_now);
            Thread.Sleep(2);
        }

        Assert.IsFalse(_service.IsBusy, "流程应在超时前结束");
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(i % 200), 80, 160, 255);

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        byte[] bytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        return bytes;
    }

    // ==================== 测试替身 ====================

    private sealed class FakeServiceView : ICharacterView
    {
        private readonly HashSet<AppearanceSlot> _supported = new HashSet<AppearanceSlot>
        {
            AppearanceSlot.Thumbnail,
            AppearanceSlot.FaceTexture
        };

        private readonly Dictionary<AppearanceSlot, Texture2D> _applied =
            new Dictionary<AppearanceSlot, Texture2D>();

        private readonly string _characterId;

        public FakeServiceView(string characterId = "fake-char")
        {
            _characterId = characterId;
        }

        public int ApplyCallCount { get; private set; }

        public string CharacterId => _characterId;

        public CharacterStyle Style => CharacterStyle.Custom;

        public Transform RootTransform => null;

        public Animator Animator => null;

        public bool IsActivated => true;

        public bool Supports(AppearanceSlot slot) => _supported.Contains(slot);

        public CharacterValidation ValidateRig() => CharacterValidation.Pass;

        public void OnCharacterActivated() { }

        public void OnCharacterDeactivated() { }

        public bool ApplyAppearance(AppearanceSlot slot, Texture2D texture, out string error)
        {
            ApplyCallCount++;
            if (texture == null)
            {
                error = "纹理为空。";
                return false;
            }
            _applied[slot] = texture;
            error = null;
            return true;
        }

        public bool ClearAppearance(AppearanceSlot slot, out string error)
        {
            _applied.Remove(slot);
            error = null;
            return true;
        }

        public Texture2D GetApplied(AppearanceSlot slot)
            => _applied.TryGetValue(slot, out var texture) ? texture : null;

        public void DisposeCharacterResources() => _applied.Clear();

        public void DestroyHeldTextures()
        {
            foreach (var pair in _applied)
            {
                if (pair.Value != null) Object.DestroyImmediate(pair.Value);
            }
            _applied.Clear();
        }
    }

    private sealed class UnavailableProvider : IImageGenerationProvider
    {
        public string ProviderId => "unavailable";

        public bool IsAvailable => false;

        public ImageGenerationJob StartJob(GenerationRequest request)
            => throw new InvalidOperationException("不可用的提供方不应被调用");
    }

    private sealed class FailingProvider : IImageGenerationProvider
    {
        private readonly Func<float> _clock;

        public FailingProvider(Func<float> clock)
        {
            _clock = clock;
        }

        public string ProviderId => "failing";

        public bool IsAvailable => true;

        public ImageGenerationJob StartJob(GenerationRequest request)
            => ImageGenerationJob.Create(request, ProviderId, _clock,
                (req, startedAt, now) => ImageAttemptSnapshot.Failed(
                    ImageGenerationErrorCategory.ContentRejected,
                    "照片里没有可识别的人脸。",
                    "请换一张正面清晰的照片后重试。"));
    }
}

/// <summary>
/// 外观落盘与清理测试（方案 9.6）：固定文件名、原子写的临时文件、删除覆盖范围。
/// </summary>
public class CustomizationStorageTests
{
    private string _root;
    private AppearanceStorage _storage;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "PianoTeacher3D_Storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _storage = new AppearanceStorage(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (Exception)
        {
            // 清理失败不影响断言
        }
    }

    [Test]
    public void SaveResult_UsesFixedNameAndLeavesNoTempFiles()
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(new Color32[128 * 128]);
            texture.Apply(false, false);

            Assert.IsTrue(_storage.TrySaveResult("fake-char", texture, out string error), error);

            string dir = _storage.GetCharacterDirectory("fake-char");
            Assert.IsTrue(File.Exists(Path.Combine(dir, AppearanceStorage.ResultFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(dir, AppearanceStorage.ResultFileName
                + AtomicFileWrite.TempSuffix)), "成功后不得残留 .tmp");
            Assert.IsFalse(File.Exists(Path.Combine(dir, AppearanceStorage.ResultFileName
                + AppearanceStorage.StagingSuffix)), "成功后不得残留 .stage");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void CleanupIncomplete_RemovesLeftoverTempAndStagingFiles()
    {
        string dir = _storage.GetCharacterDirectory("fake-char");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "result.png" + AtomicFileWrite.TempSuffix), "half");
        File.WriteAllText(Path.Combine(dir, "original.photo" + AppearanceStorage.StagingSuffix), "half");
        File.WriteAllText(Path.Combine(dir, AppearanceStorage.ResultFileName), "real");

        int cleaned = _storage.CleanupIncomplete();

        Assert.AreEqual(2, cleaned);
        Assert.IsTrue(File.Exists(Path.Combine(dir, AppearanceStorage.ResultFileName)), "有效文件不得被清理");
    }

    [Test]
    public void DeleteOne_RemovesEveryFixedFileAndDirectory()
    {
        string dir = _storage.GetCharacterDirectory("fake-char");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, AppearanceStorage.OriginalFileName), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(dir, AppearanceStorage.ResultFileName), "x");
        File.WriteAllText(Path.Combine(dir, AppearanceStorage.ThumbnailFileName), "x");
        File.WriteAllText(Path.Combine(dir, AppearanceStorage.ManifestFileName), "{}");

        Assert.IsTrue(_storage.DeleteOne("fake-char", out string error), error);

        Assert.IsFalse(Directory.Exists(dir), "删除后目录应清空并移除");
        Assert.IsFalse(_storage.HasStoredData("fake-char"));
    }

    [Test]
    public void TryLoadResult_RoundTripsSavedResult()
    {
        var texture = new Texture2D(96, 96, TextureFormat.RGBA32, false);
        try
        {
            var pixels = new Color32[96 * 96];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 100, 50, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Assert.IsTrue(_storage.TrySaveResult("fake-char", texture, out string saveError), saveError);
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        Assert.IsTrue(_storage.TryLoadResult("fake-char", out Texture2D loaded, out string loadError), loadError);
        try
        {
            Assert.AreEqual(96, loaded.width);
            Assert.AreEqual(96, loaded.height);
        }
        finally
        {
            Object.DestroyImmediate(loaded);
        }
    }

    [Test]
    public void TryLoadResult_WhenMissing_ReturnsReadableReason()
    {
        Assert.IsFalse(_storage.TryLoadResult("fake-char", out Texture2D loaded, out string error));
        Assert.IsNull(loaded);
        StringAssert.Contains("还没有保存", error);
    }

    [Test]
    public void InvalidCharacterId_IsRejected()
    {
        Assert.IsNull(_storage.GetCharacterDirectory("../escape"));
        Assert.IsFalse(_storage.TrySaveResult("../escape", null, out string error));
        Assert.IsNotEmpty(error);
    }
}
