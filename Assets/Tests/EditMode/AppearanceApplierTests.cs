using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;

/// <summary>
/// 外观应用器测试（方案 9.5 / 11.2 / P1-5）：支持槽位应用、不支持槽位被拒、
/// 应用失败回滚到旧纹理、撤销、恢复默认，以及「视图仍在引用的纹理不得被销毁」。
/// 用 fake <see cref="ICharacterView"/>，不依赖场景与真实角色资源。
/// </summary>
public class AppearanceApplierTests
{
    private FakeAppearanceView _view;
    private GeneratedAppearanceApplier _applier;

    [SetUp]
    public void SetUp()
    {
        _view = new FakeAppearanceView();
        _applier = new GeneratedAppearanceApplier(_view);
    }

    [TearDown]
    public void TearDown()
    {
        _applier?.ReleaseAppliedResources();
        _view?.DestroyHeldTextures();
    }

    // ==================== 应用 ====================

    [Test]
    public void TryApply_SupportedSlot_AppliesAndRecordsUndoPoint()
    {
        Texture2D texture = CreateTexture("A");

        bool ok = _applier.TryApply(AppearanceSlot.Thumbnail, texture, out string error);

        Assert.IsTrue(ok, error);
        Assert.AreSame(texture, _view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsTrue(_applier.HasApplied(AppearanceSlot.Thumbnail));
        Assert.IsTrue(_applier.CanUndo);
        Assert.AreEqual(1, _applier.UndoDepth);
    }

    [Test]
    public void TryApply_UnsupportedSlot_IsRejectedWithReadableReason()
    {
        Texture2D texture = CreateTexture("A");

        bool ok = _applier.TryApply(AppearanceSlot.BodyTint, texture, out string error);

        Assert.IsFalse(ok, "不支持的槽位必须拒绝");
        StringAssert.Contains("不支持", error);
        Assert.IsNull(_view.GetApplied(AppearanceSlot.BodyTint));
        Assert.AreEqual(0, _view.ApplyCallCount, "不支持时不得调用角色视图");
    }

    [Test]
    public void TryApply_NullTexture_IsRejected()
    {
        Assert.IsFalse(_applier.TryApply(AppearanceSlot.Thumbnail, null, out string error));
        Assert.IsNotEmpty(error);
        Assert.AreEqual(0, _view.ApplyCallCount);
    }

    [Test]
    public void TryApply_ViewRejects_RollsBackToPreviousTextureAndKeepsItAlive()
    {
        Texture2D first = CreateTexture("A");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, first, out _), "首次应用应成功");

        Texture2D second = CreateTexture("B");
        _view.FailNextApply = true;

        bool ok = _applier.TryApply(AppearanceSlot.Thumbnail, second, out string error);

        Assert.IsFalse(ok, "视图拒绝时必须返回失败");
        Assert.IsNotEmpty(error);
        Assert.AreSame(first, _view.GetApplied(AppearanceSlot.Thumbnail), "失败后视图必须仍持有旧纹理");
        Assert.IsTrue(first != null, "回滚时不得销毁旧纹理");
        Assert.AreSame(first, _applier.GetApplied(AppearanceSlot.Thumbnail));
        Assert.AreEqual(1, _applier.UndoDepth, "失败的应用不记录撤销点");
    }

    [Test]
    public void TryApply_SecondTime_KeepsPreviousAliveForUndoButNeverReleasesLiveOne()
    {
        Texture2D first = CreateTexture("A");
        Texture2D second = CreateTexture("B");

        Assert.IsTrue(_applier.TryApply(AppearanceSlot.FaceTexture, first, out _));
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.FaceTexture, second, out _));

        Assert.IsTrue(first != null, "上一份纹理是撤销点，必须保留");
        Assert.IsTrue(second != null, "视图正在引用的新纹理不得销毁");
        Assert.AreSame(second, _view.GetApplied(AppearanceSlot.FaceTexture));
        Assert.AreEqual(2, _applier.UndoDepth);
    }

    [Test]
    public void TryApply_BeyondUndoDepth_ReleasesOldestBackup()
    {
        var textures = new Texture2D[GeneratedAppearanceApplier.MaxUndoDepth + 2];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = CreateTexture("T" + i);
            Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, textures[i], out string error), error);
        }

        Assert.IsTrue(textures[0] == null, "被挤出撤销历史的旧纹理应释放");
        Assert.IsTrue(textures[textures.Length - 1] != null, "最新纹理必须存活");
        Assert.AreEqual(GeneratedAppearanceApplier.MaxUndoDepth, _applier.UndoDepth);
        Assert.AreSame(textures[textures.Length - 1], _view.GetApplied(AppearanceSlot.Thumbnail));
    }

    // ==================== 撤销 / 恢复默认 ====================

    [Test]
    public void TryUndo_RestoresPreviousTextureAndReleasesCurrent()
    {
        Texture2D first = CreateTexture("A");
        Texture2D second = CreateTexture("B");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, first, out _));
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, second, out _));

        bool ok = _applier.TryUndo(out string error);

        Assert.IsTrue(ok, error);
        Assert.AreSame(first, _view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsTrue(second == null, "撤销后当前纹理不再被引用，应释放");
        Assert.IsTrue(first != null, "被撤销回去的纹理必须存活");

        // 撤销链的终点是「角色默认外观」：回到第一份贴图后仍可再撤销一次退回默认。
        Assert.IsTrue(_applier.CanUndo, "撤销一次后仍应能退回默认外观");
        Assert.IsTrue(_applier.TryUndo(out string second_undo), second_undo);
        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail), "第二次撤销应回到默认外观");
        Assert.IsFalse(_applier.CanUndo, "退到默认后撤销栈应清空");
    }

    [Test]
    public void TryUndo_WithoutApply_IsRejected()
    {
        Assert.IsFalse(_applier.TryUndo(out string error));
        Assert.IsNotEmpty(error);
    }

    [Test]
    public void TryRestoreDefault_ClearsViewAndReleasesAppliedTexture()
    {
        Texture2D texture = CreateTexture("A");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, texture, out _));

        bool ok = _applier.TryRestoreDefault(AppearanceSlot.Thumbnail, out string error);

        Assert.IsTrue(ok, error);
        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsTrue(texture == null, "恢复默认后应释放自有纹理");
        Assert.IsFalse(_applier.HasApplied(AppearanceSlot.Thumbnail));
    }

    [Test]
    public void TryRestoreDefault_WhenNothingApplied_IsRejected()
    {
        Assert.IsFalse(_applier.TryRestoreDefault(AppearanceSlot.Thumbnail, out string error));
        Assert.IsNotEmpty(error);
    }

    [Test]
    public void TryRestoreAll_ClearsEveryAppliedSlot()
    {
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, CreateTexture("A"), out _));
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.FaceTexture, CreateTexture("B"), out _));

        int restored = _applier.TryRestoreAll(out string error);

        Assert.AreEqual(2, restored, error);
        Assert.AreEqual(0, _view.HeldTextureCount);
    }

    [Test]
    public void TryRetarget_SameCharacterNewInstance_MovesAppearanceWithoutCopy()
    {
        Texture2D texture = CreateTexture("A");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, texture, out _));

        var newView = new FakeAppearanceView();

        Assert.IsTrue(_applier.TryRetarget(newView, out string warning), warning);
        Assert.AreSame(texture, newView.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail), "旧视图必须同帧解引用");
        Assert.IsTrue(texture != null, "迁移过程中不得销毁纹理");
        Assert.AreSame(newView, _applier.View);
    }

    [Test]
    public void TryRetarget_DifferentCharacter_IsRejectedAndKeepsOldView()
    {
        Texture2D texture = CreateTexture("A");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, texture, out _));

        var other = new FakeAppearanceView("other-char");

        Assert.IsFalse(_applier.TryRetarget(other, out string warning));
        StringAssert.Contains("更换", warning);
        Assert.AreSame(texture, _view.GetApplied(AppearanceSlot.Thumbnail), "拒绝迁移时旧视图不应被改动");
        Assert.IsNull(other.GetApplied(AppearanceSlot.Thumbnail));
    }

    [Test]
    public void ReleaseForCharacterSwitch_ClearsViewAndReleasesOwnedTextures()
    {
        Texture2D thumbnail = CreateTexture("A");
        Texture2D face = CreateTexture("B");
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.Thumbnail, thumbnail, out _));
        Assert.IsTrue(_applier.TryApply(AppearanceSlot.FaceTexture, face, out _));

        _applier.ReleaseForCharacterSwitch();

        Assert.IsNull(_view.GetApplied(AppearanceSlot.Thumbnail));
        Assert.IsNull(_view.GetApplied(AppearanceSlot.FaceTexture));
        Assert.IsTrue(thumbnail == null, "换角色后本类自有纹理必须回收");
        Assert.IsTrue(face == null, "换角色后本类自有纹理必须回收");
        Assert.IsFalse(_applier.CanUndo);
        Assert.IsNull(_applier.View);
    }

    [Test]
    public void Applier_WithoutView_RejectsApply()
    {
        var applier = new GeneratedAppearanceApplier(null);
        Texture2D texture = CreateTexture("A");
        try
        {
            Assert.IsFalse(applier.TryApply(AppearanceSlot.Thumbnail, texture, out string error));
            Assert.IsNotEmpty(error);
        }
        finally
        {
            Destroy(texture);
        }
    }

    // ==================== 测试替身 ====================

    private sealed class FakeAppearanceView : ICharacterView
    {
        private readonly HashSet<AppearanceSlot> _supported = new HashSet<AppearanceSlot>
        {
            AppearanceSlot.Thumbnail,
            AppearanceSlot.FaceTexture
        };

        private readonly Dictionary<AppearanceSlot, Texture2D> _applied =
            new Dictionary<AppearanceSlot, Texture2D>();

        private readonly string _characterId;

        public FakeAppearanceView(string characterId = "fake-char")
        {
            _characterId = characterId;
        }

        public bool FailNextApply;

        public int ApplyCallCount { get; private set; }

        public int HeldTextureCount => _applied.Count;

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

            if (FailNextApply)
            {
                FailNextApply = false;
                error = "角色材质不可写，无法替换贴图。";
                return false;
            }

            if (texture == null)
            {
                error = "纹理为空。";
                return false;
            }

            // 模拟「先建立新引用」：失败分支不会走到这里
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

    private static Texture2D CreateTexture(string name)
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "Test_" + name };
        var pixels = new Color32[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 120, 60, 255);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null) Object.DestroyImmediate(texture);
    }
}
