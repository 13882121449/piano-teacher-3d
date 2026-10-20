using System.Collections.Generic;
using NUnit.Framework;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Tests
{
    /// <summary>
    /// 角色切换事务（方案 7.3 的 8 步流程）：提交、回滚、取消、重复实例防护、资源释放次数。
    /// 全部使用 fake 视图，纯内存运行，不加载预制体、不进 PlayMode。
    /// </summary>
    public class CharacterSwitchServiceTests
    {
        private const string IdA = "test.alpha";
        private const string IdB = "test.beta";
        private const string IdC = "test.gamma";

        private GameObject _rootGo;
        private FakeFactory _factory;
        private CharacterSwitchService _service;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("FakeCharacterRoot");
            _factory = new FakeFactory(_rootGo.transform);
            _service = new CharacterSwitchService(_factory);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;

            _factory?.Dispose();
            _factory = null;

            if (_rootGo != null) Object.DestroyImmediate(_rootGo);
            _rootGo = null;
        }

        [Test]
        public void SuccessfulSwitch_RunsEightSteps_AndCommits()
        {
            var profile = _factory.Define(IdA);

            Assert.IsTrue(_service.Request(profile));
            Assert.AreEqual(CharacterSwitchState.Loading, _service.State);

            int steps = 0;
            while (_service.Tick()) steps++;

            Assert.AreEqual(CharacterSwitchService.TransactionSteps - 1, steps,
                "第一次 Tick 在 Request 之后立即执行，因此 while 内只观察到最后一步之前的次数");
            Assert.AreEqual(CharacterSwitchState.Committed, _service.State);
            Assert.IsTrue(_service.LastResult.Succeeded);
            Assert.AreEqual(IdA, _service.CurrentCharacterId);
            Assert.AreSame(_factory.Views[IdA], _service.CurrentView);
        }

        [Test]
        public void SuccessfulSwitch_ActivatesNewView()
        {
            var profile = _factory.Define(IdA);
            Drain(profile);

            var view = _factory.Views[IdA];
            Assert.AreEqual(1, view.ActivateCount);
            Assert.AreEqual(0, view.DeactivateCount);
            Assert.IsTrue(view.IsActivated);
            Assert.AreEqual(0, view.DisposeCount, "成功提交的角色不应被释放");
        }

        [Test]
        public void SwitchingAway_DeactivatesAndDisposesOldView()
        {
            Drain(_factory.Define(IdA));
            Drain(_factory.Define(IdB));

            var a = _factory.Views[IdA];
            var b = _factory.Views[IdB];

            Assert.AreEqual(1, a.DeactivateCount);
            Assert.AreEqual(1, a.DisposeCount);
            Assert.AreEqual(1, b.ActivateCount);
            Assert.AreEqual(IdB, _service.CurrentCharacterId);
        }

        [Test]
        public void ValidationFailure_RollsBack_AndKeepsOldCharacter()
        {
            Drain(_factory.Define(IdA));

            var b = _factory.Create(IdB);
            b.FailValidation = true;

            Drain(b.Profile);

            Assert.AreEqual(CharacterSwitchState.RolledBack, _service.State);
            Assert.IsFalse(_service.LastResult.Succeeded);
            StringAssert.Contains("骨骼", _service.LastResult.Error);
            Assert.AreEqual(IdA, _service.CurrentCharacterId, "失败后必须保留旧角色");
            Assert.IsTrue(_factory.Views[IdA].IsActivated, "旧角色必须仍然可驱动");
            Assert.AreEqual(1, b.DisposeCount, "新角色临时资源必须释放恰好一次");
            Assert.IsFalse(b.IsActivated);
        }

        [Test]
        public void ValidationFailure_DoesNotDisposeOldCharacter()
        {
            Drain(_factory.Define(IdA));

            var b = _factory.Create(IdB);
            b.FailValidation = true;
            Drain(b.Profile);

            Assert.AreEqual(0, _factory.Views[IdA].DisposeCount);
            Assert.AreEqual(1, _factory.Views[IdA].ActivateCount);
        }

        [Test]
        public void LoadFailure_RollsBack_WithoutTouchingCurrent()
        {
            Drain(_factory.Define(IdA));

            var pb = _factory.Define(IdB);
            _factory.MissingIds.Add(IdB);
            var result = Run(pb);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(CharacterSwitchState.RolledBack, _service.State);
            Assert.AreEqual(IdA, _service.CurrentCharacterId);
            Assert.AreEqual(0, _factory.Views[IdA].DisposeCount);
            StringAssert.Contains("加载失败", result.Error);
        }

        [Test]
        public void ActivationFailure_RollsBack_AndRestoresOldActivation()
        {
            Drain(_factory.Define(IdA));

            var b = _factory.Create(IdB);
            b.FailActivation = true;
            Drain(b.Profile);

            Assert.AreEqual(CharacterSwitchState.RolledBack, _service.State);
            Assert.AreEqual(IdA, _service.CurrentCharacterId);
            Assert.IsTrue(_factory.Views[IdA].IsActivated);
            Assert.AreEqual(2, _factory.Views[IdA].ActivateCount, "回滚必须重新激活旧角色");
            Assert.AreEqual(1, b.DisposeCount);
        }

        [Test]
        public void SecondRequestDuringLoad_CancelsFirst_AndDisposesItsTempView()
        {
            var a = _factory.Define(IdA);

            // 推进到第 2 步（已完成加载，_pendingView 已存在）
            _service.Request(a);
            _service.Tick();   // Check
            _service.Tick();   // Load

            Assert.IsTrue(_service.IsBusy);

            var b = _factory.Define(IdB);
            _service.Request(b);

            Assert.AreEqual(CharacterSwitchState.Cancelled, _service.LastResult.State,
                "旧请求必须被置为 Cancelled");
            Assert.AreEqual(1, _factory.Views[IdA].DisposeCount, "被取消请求的临时视图必须释放");

            Drain(b, alreadyRequested: true);

            Assert.AreEqual(CharacterSwitchState.Committed, _service.State);
            Assert.AreEqual(IdB, _service.CurrentCharacterId);
        }

        [Test]
        public void SecondRequestBeforeLoad_CancelsFirst_WithoutCreatingExtraInstance()
        {
            var a = _factory.Define(IdA);

            _service.Request(a);   // 尚未 Tick，未加载
            var b = _factory.Define(IdB);
            _service.Request(b);

            Assert.AreEqual(CharacterSwitchState.Cancelled, _service.LastResult.State);
            Assert.AreEqual(0, _factory.CreateCallsFor(IdA), "取消发生在加载前，不应产生实例");

            Drain(b, alreadyRequested: true);
            Assert.AreEqual(IdB, _service.CurrentCharacterId);
        }

        [Test]
        public void RapidSwitch_CreatesAtMostOneInstancePerRequest()
        {
            var a = _factory.Define(IdA);
            var b = _factory.Define(IdB);
            var c = _factory.Define(IdC);

            _service.Request(a);
            _service.Tick();
            _service.Request(b);
            _service.Tick();
            _service.Request(c);
            Drain(c, alreadyRequested: true);

            Assert.AreEqual(1, _factory.CreateCallsFor(IdC));
            Assert.AreEqual(CharacterSwitchState.Committed, _service.State);
            Assert.AreEqual(IdC, _service.CurrentCharacterId);

            // a 与 b 都被取消：各自最多创建一个实例，且创建过就必须释放过
            Assert.LessOrEqual(_factory.CreateCallsFor(IdA), 1);
            Assert.LessOrEqual(_factory.CreateCallsFor(IdB), 1);
            if (_factory.Views.TryGetValue(IdA, out var viewA))
                Assert.AreEqual(_factory.CreateCallsFor(IdA), viewA.DisposeCount);
            if (_factory.Views.TryGetValue(IdB, out var viewB))
                Assert.AreEqual(_factory.CreateCallsFor(IdB), viewB.DisposeCount);
        }

        [Test]
        public void SameCharacterRequest_DoesNotCreateSecondInstance()
        {
            Drain(_factory.Define(IdA));
            Assert.AreEqual(1, _factory.CreateCallsFor(IdA));

            var result = Run(_factory.ProfileOf(IdA));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, _factory.CreateCallsFor(IdA), "同一角色不得重复实例化");
            Assert.AreEqual(0, _factory.Views[IdA].DisposeCount);
            Assert.AreEqual(CharacterSwitchState.Committed, _service.State);
        }

        [Test]
        public void Cancel_DisposesPendingExactlyOnce()
        {
            _service.Request(_factory.Define(IdA));
            _service.Tick();
            _service.Tick();

            _service.Cancel();
            Assert.AreEqual(CharacterSwitchState.Cancelled, _service.State);
            Assert.AreEqual(1, _factory.Views[IdA].DisposeCount);

            _service.Cancel();   // 再次取消：无事务，不应重复释放
            Assert.AreEqual(1, _factory.Views[IdA].DisposeCount);
        }

        [Test]
        public void UnregisteredProfile_IsRejectedWhenRegistryProvided()
        {
            var registry = new CharacterRegistry(includeResourceProfiles: false);
            var service = new CharacterSwitchService(_factory, registry);
            try
            {
                var result = Run(_factory.Define(IdA), service);

                Assert.IsFalse(result.Succeeded);
                StringAssert.Contains("未在角色库", result.Error);
                Assert.AreEqual(0, _factory.CreateCallsFor(IdA), "未登记的角色不应被加载");
            }
            finally
            {
                service.Dispose();
                registry.Dispose();
            }
        }

        [Test]
        public void NullProfile_IsRejectedWithoutThrowing()
        {
            Assert.IsFalse(_service.Request(null));
            Assert.IsFalse(_service.LastResult.Succeeded);
        }

        [Test]
        public void Dispose_WhileBusy_CancelsAndReleasesCurrent()
        {
            Drain(_factory.Define(IdA));
            _service.Request(_factory.Define(IdB));
            _service.Tick();

            _service.Dispose();

            Assert.AreEqual(CharacterSwitchState.Cancelled, _service.State);
            Assert.AreEqual(1, _factory.Views[IdA].DisposeCount);
            Assert.DoesNotThrow(() => _service.Dispose());
        }

        // ==================== CharacterResourceScope ====================

        [Test]
        public void ResourceScope_DisposesOwnedObjectsExactlyOnce()
        {
            var a = ScriptableObject.CreateInstance<CharacterProfile>();
            var b = ScriptableObject.CreateInstance<CharacterProfile>();

            var scope = new CharacterResourceScope("test");
            scope.Own(a);
            scope.Own(b);
            scope.Own(a);   // 重复登记被忽略

            Assert.AreEqual(2, scope.OwnedCount);

            scope.Dispose();
            Assert.AreEqual(2, scope.ReleasedCount);
            Assert.IsTrue(scope.IsDisposed);

            scope.Dispose();   // 第二次是空操作
            Assert.AreEqual(2, scope.ReleasedCount, "不得重复释放");
            Assert.AreEqual(0, scope.OwnedCount);
        }

        [Test]
        public void ResourceScope_Release_RemovesOwnershipWithoutDestroying()
        {
            var shared = ScriptableObject.CreateInstance<CharacterProfile>();
            var scope = new CharacterResourceScope("test");
            scope.Own(shared);

            Assert.IsTrue(scope.Release(shared));
            Assert.IsFalse(scope.Release(shared));

            scope.Dispose();
            Assert.AreEqual(0, scope.ReleasedCount);
            Assert.IsTrue(shared != null, "移交所有权的共享资源不应被销毁");

            Object.DestroyImmediate(shared);
        }

        [Test]
        public void ResourceScope_IgnoresNull()
        {
            var scope = new CharacterResourceScope("test");
            Assert.DoesNotThrow(() => scope.Own(null));
            Assert.AreEqual(0, scope.OwnedCount);
            scope.Dispose();
        }

        // ==================== 辅助 ====================

        private void Drain(CharacterProfile profile, bool alreadyRequested = false)
        {
            Run(profile, _service, alreadyRequested);
        }

        private CharacterSwitchResult Run(CharacterProfile profile, CharacterSwitchService service = null,
            bool alreadyRequested = false)
        {
            var target = service ?? _service;
            if (!alreadyRequested) target.Request(profile);
            while (target.Tick()) { }
            return target.LastResult;
        }

        // ==================== Fake 实现 ====================

        private sealed class FakeFactory : ICharacterViewFactory
        {
            private readonly Transform _root;
            private readonly Dictionary<string, CharacterProfile> _profiles =
                new Dictionary<string, CharacterProfile>();
            private readonly Dictionary<string, int> _createCalls = new Dictionary<string, int>();

            public readonly Dictionary<string, FakeView> Views = new Dictionary<string, FakeView>();
            public readonly HashSet<string> MissingIds = new HashSet<string>();

            public FakeFactory(Transform root)
            {
                _root = root;
            }

            public CharacterProfile Define(string id)
            {
                if (_profiles.TryGetValue(id, out var existing)) return existing;

                var profile = ScriptableObject.CreateInstance<CharacterProfile>();
                profile.CharacterId = id;
                profile.DisplayName = id;
                profile.Source = CharacterSource.Prefab;
                profile.Prefab = null;
                profile.ResourcesPath = "Characters/Fake/" + id;
                profile.LicenseName = "MIT";
                profile.QualityTier = 1;
                _profiles[id] = profile;
                return profile;
            }

            public CharacterProfile ProfileOf(string id) => _profiles[id];

            /// <summary>清理测试里创建的 ScriptableObject，避免 EditMode 测试报「leaked objects」。</summary>
            public void Dispose()
            {
                foreach (var pair in _profiles)
                {
                    if (pair.Value != null) Object.DestroyImmediate(pair.Value);
                }
                _profiles.Clear();
                ResetTracking();
            }

            private void ResetTracking()
            {
                Views.Clear();
                _createCalls.Clear();
                MissingIds.Clear();
            }

            public int CreateCallsFor(string id) => _createCalls.TryGetValue(id, out var n) ? n : 0;

            /// <summary>预先创建（并配置失败开关）一个视图，之后 Create 返回同一个实例。</summary>
            public FakeView Create(string id)
            {
                Define(id);
                var view = new FakeView(id, _root) { Profile = _profiles[id] };
                Views[id] = view;
                return view;
            }

            public bool Supports(CharacterProfile profile) => profile != null;

            public ICharacterView Create(CharacterProfile profile, out string error)
            {
                error = null;
                if (profile == null)
                {
                    error = "角色资料为空。";
                    return null;
                }

                _createCalls.TryGetValue(profile.CharacterId, out var n);
                _createCalls[profile.CharacterId] = n + 1;

                if (MissingIds.Contains(profile.CharacterId))
                {
                    error = "角色加载失败：测试注入。";
                    return null;
                }

                if (Views.TryGetValue(profile.CharacterId, out var view))
                {
                    view.Profile = profile;
                    return view;
                }

                var created = new FakeView(profile.CharacterId, _root) { Profile = profile };
                Views[profile.CharacterId] = created;
                return created;
            }
        }

        private sealed class FakeView : ICharacterView
        {
            private readonly Transform _root;

            public FakeView(string id, Transform root)
            {
                CharacterId = id;
                _root = root;
            }

            public CharacterProfile Profile { get; set; }

            public string CharacterId { get; }

            public CharacterStyle Style => CharacterStyle.Anime;

            public Transform RootTransform => _root;

            public Animator Animator => null;

            public bool IsActivated { get; private set; }

            public int ActivateCount { get; private set; }

            public int DeactivateCount { get; private set; }

            public int DisposeCount { get; private set; }

            public bool FailValidation;

            public bool FailActivation;

            public bool Supports(AppearanceSlot slot) => slot == AppearanceSlot.Thumbnail;

            public CharacterValidation ValidateRig() => FailValidation
                ? CharacterValidation.Fail("缺少骨骼 Bip01 L Finger31")
                : CharacterValidation.Pass;

            public void OnCharacterActivated()
            {
                ActivateCount++;
                if (FailActivation) throw new System.InvalidOperationException("测试注入的激活失败");
                IsActivated = true;
            }

            public void OnCharacterDeactivated()
            {
                DeactivateCount++;
                IsActivated = false;
            }

            public bool ApplyAppearance(AppearanceSlot slot, Texture2D texture, out string error)
            {
                error = "fake 不支持外观替换。";
                return false;
            }

            public bool ClearAppearance(AppearanceSlot slot, out string error)
            {
                error = null;
                return true;
            }

            public void DisposeCharacterResources()
            {
                DisposeCount++;
                IsActivated = false;
            }
        }
    }
}
