using System;
using System.Collections;
using UnityEngine;

namespace PianoTeacher3D.Characters
{
    /// <summary>
    /// 角色视图工厂：把「角色资料」翻译成可激活的角色实例。
    /// 由 <c>AvatarManager</c> 注入具体实现；测试里注入 fake 实现，即可在无场景的 EditMode 下验证事务。
    /// </summary>
    public interface ICharacterViewFactory
    {
        /// <summary>当前运行环境是否能创建该来源的角色（例如缺少 AvatarManager 时不能激活程序化角色）。</summary>
        bool Supports(CharacterProfile profile);

        /// <summary>创建视图；失败时返回 null 并给出可读原因（不抛异常）。</summary>
        ICharacterView Create(CharacterProfile profile, out string error);
    }

    /// <summary>
    /// 角色切换事务（方案 7.3 的 8 步流程）。
    /// <para>
    /// <b>为什么是「纯 C# + 逐帧 Tick」而不是 async/await</b>：Unity 的资源加载、Transform 操作、
    /// Animator 重绑定都必须在主线程完成；<c>Task</c> 回到主线程需要 SynchronizationContext，
    /// 一旦调用方从非主线程发起切换就会炸在渲染线程外。因此这里用显式状态机，
    /// 每一步都可以在任意帧边界停下来，宿主（<see cref="CharacterSwitchHost"/>）用协程驱动。
    /// </para>
    /// <para>
    /// <b>8 步</b>：① 检查存在/兼容 → ② 加载 → ③ 校验 → ④ 锁状态（停用旧角色）
    /// → ⑤ 激活接管 → ⑥ 切换引用 → ⑦ 确认可驱动 → ⑧ 卸载旧角色。
    /// 任一步失败：释放新角色的临时资源、恢复旧角色、状态置 <see cref="CharacterSwitchState.RolledBack"/>。
    /// </para>
    /// <para>
    /// <b>重复实例防护</b>：切换进行中收到新请求时，旧请求先被置为 <see cref="CharacterSwitchState.Cancelled"/>
    /// 且其临时视图当场释放，然后才开始新请求；同一 ID 的重复请求直接返回已提交结果，不创建第二个实例。
    /// </para>
    /// <para>
    /// <b>不碰演奏状态</b>：本类不引用 <c>LessonSessionController</c>、时间轴、音频或播放位置，
    /// 切换期间这些状态不可能被重置。
    /// </para>
    /// </summary>
    public sealed class CharacterSwitchService : IDisposable
    {
        /// <summary>事务步数（供测试断言「8 步」）。</summary>
        public const int TransactionSteps = 8;

        private readonly ICharacterViewFactory _factory;
        private readonly CharacterRegistry _registry;

        private CharacterProfile _target;
        private ICharacterView _pendingView;
        private ICharacterView _outgoingView;
        private bool _outgoingDeactivated;
        private int _step;

        private CharacterSwitchResult _lastResult;
        private bool _disposed;

        public CharacterSwitchService(ICharacterViewFactory factory, CharacterRegistry registry = null)
        {
            _factory = factory;
            _registry = registry;
        }

        /// <summary>当前状态。</summary>
        public CharacterSwitchState State { get; private set; } = CharacterSwitchState.Idle;

        /// <summary>当前已提交的角色视图；内置角色未创建视图时为 null。</summary>
        public ICharacterView CurrentView { get; private set; }

        /// <summary>当前角色 ID（没有视图时为空字符串）。</summary>
        public string CurrentCharacterId => CurrentView != null ? CurrentView.CharacterId : string.Empty;

        /// <summary>最近一次<b>终结</b>结果（Committed / RolledBack / Cancelled）。</summary>
        public CharacterSwitchResult LastResult => _lastResult;

        /// <summary>已执行到第几步（0..7）；空闲时为 0。</summary>
        public int StepIndex => _step;

        /// <summary>是否有事务在进行。</summary>
        public bool IsBusy => _target != null;

        /// <summary>切换终结（含取消）时触发。参数为终结结果。</summary>
        public event Action<CharacterSwitchResult> OnSwitched;

        /// <summary>状态变化时触发（UI 可据此显示「正在切换…」）。</summary>
        public event Action<CharacterSwitchState> OnStateChanged;

        /// <summary>
        /// 接管一个已存在的视图（不触发激活/销毁）。
        /// <c>AvatarManager</c> 在每次切换前用它把「内置角色包装」交给事务，使回滚有东西可恢复。
        /// </summary>
        public void SetCurrent(ICharacterView view)
        {
            CurrentView = view;
        }

        /// <summary>
        /// 发起切换。已有事务时先取消它（旧请求置 Cancelled）。
        /// 返回是否已受理（资料为空时返回 false）。
        /// </summary>
        public bool Request(CharacterProfile profile)
        {
            if (profile == null)
            {
                _lastResult = new CharacterSwitchResult(CharacterSwitchState.RolledBack, "角色资料为空。",
                    CurrentCharacterId, CurrentCharacterId);
                OnSwitched?.Invoke(_lastResult);
                return false;
            }

            if (IsBusy) Abort(CharacterSwitchState.Cancelled, "被新的切换请求取消。");

            // 同一角色重复请求：不创建第二个实例（连续点击同一张卡片是常见误操作）
            if (CurrentView != null && string.Equals(CurrentView.CharacterId, profile.CharacterId, StringComparison.Ordinal))
            {
                CommitNoOp(profile.CharacterId);
                return true;
            }

            _target = profile;
            _step = 0;
            _pendingView = null;
            _outgoingView = CurrentView;
            _outgoingDeactivated = false;
            SetState(CharacterSwitchState.Loading);
            return true;
        }

        /// <summary>
        /// 推进恰好一步。返回 true 表示还有后续步骤需要继续调用。
        /// 测试可以逐步调用它，观察中间状态与取消时机。
        /// </summary>
        public bool Tick()
        {
            if (_target == null) return false;

            int step = _step;
            _step = step + 1;

            switch (step)
            {
                case 0: if (!StepCheck()) return false; break;
                case 1: if (!StepLoad()) return false; break;
                case 2: if (!StepValidate()) return false; break;
                case 3: StepLock(); break;
                case 4: if (!StepActivate()) return false; break;
                case 5: StepSwap(); break;
                case 6: if (!StepConfirm()) return false; break;
                case 7: StepUnload(); return false;
                default: return false;
            }
            return _target != null;
        }

        /// <summary>协程驱动：每帧推进一步。宿主 <see cref="CharacterSwitchHost"/> 使用。</summary>
        public IEnumerator RunToCompletion()
        {
            while (Tick())
            {
                yield return null;
            }
        }

        /// <summary>取消进行中的事务（无事务时无副作用）。</summary>
        public void Cancel(string reason = null)
        {
            if (!IsBusy) return;
            Abort(CharacterSwitchState.Cancelled, reason ?? "切换已取消。");
        }

        /// <summary>释放当前视图与进行中的事务（场景卸载时调用）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (IsBusy) Abort(CharacterSwitchState.Cancelled, "服务释放，切换取消。");

            var view = CurrentView;
            CurrentView = null;
            if (view == null) return;

            try { view.OnCharacterDeactivated(); }
            catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 停用角色失败：" + e.Message); }
            try { view.DisposeCharacterResources(); }
            catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 释放角色资源失败：" + e.Message); }
        }

        // ==================== 8 步实现 ====================

        private bool StepCheck()
        {
            var valid = CharacterAssetValidator.ValidateProfile(_target);
            if (!valid.Ok)
            {
                Abort(CharacterSwitchState.RolledBack, valid.Message);
                return false;
            }

            if (_registry != null && !_registry.IsRegistered(_target.CharacterId))
            {
                Abort(CharacterSwitchState.RolledBack,
                    $"角色「{_target.DisplayName}」未在角色库中登记。");
                return false;
            }

            if (_factory == null || !_factory.Supports(_target))
            {
                Abort(CharacterSwitchState.RolledBack,
                    $"当前环境不支持来源为 {_target.Source} 的角色。");
                return false;
            }
            return true;
        }

        private bool StepLoad()
        {
            ICharacterView view = null;
            string error = null;
            try
            {
                view = _factory.Create(_target, out error);
            }
            catch (Exception e)
            {
                error = "角色加载异常：" + e.Message;
            }

            if (view == null)
            {
                Abort(CharacterSwitchState.RolledBack, error ?? "角色加载失败。");
                return false;
            }

            _pendingView = view;
            SetState(CharacterSwitchState.Validating);
            return true;
        }

        private bool StepValidate()
        {
            var valid = CharacterAssetValidator.ValidateView(_pendingView);
            if (!valid.Ok)
            {
                Abort(CharacterSwitchState.RolledBack, valid.Message);
                return false;
            }
            SetState(CharacterSwitchState.Activating);
            return true;
        }

        private void StepLock()
        {
            // 锁状态：停用旧角色，拿到唯一的「当前角色」所有权，避免两套模型同时可驱动
            if (_outgoingView == null) return;
            try
            {
                _outgoingView.OnCharacterDeactivated();
                _outgoingDeactivated = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CharacterSwitchService] 停用旧角色失败：" + e.Message);
                _outgoingDeactivated = false;
            }
        }

        private bool StepActivate()
        {
            try
            {
                _pendingView.OnCharacterActivated();
            }
            catch (Exception e)
            {
                Abort(CharacterSwitchState.RolledBack, "角色激活失败：" + e.Message);
                return false;
            }
            return true;
        }

        private void StepSwap()
        {
            CurrentView = _pendingView;
        }

        private bool StepConfirm()
        {
            if (_pendingView == null || !_pendingView.IsActivated)
            {
                Abort(CharacterSwitchState.RolledBack, "新角色激活后未进入可驱动状态。");
                return false;
            }

            if (_pendingView.RootTransform == null)
            {
                Abort(CharacterSwitchState.RolledBack, "新角色根节点缺失，无法驱动。");
                return false;
            }

            var valid = CharacterAssetValidator.ValidateView(_pendingView);
            if (!valid.Ok)
            {
                Abort(CharacterSwitchState.RolledBack, valid.Message);
                return false;
            }
            return true;
        }

        private void StepUnload()
        {
            string previousId = _outgoingView != null ? _outgoingView.CharacterId : string.Empty;

            // 所有权移交完成：pendingView 成为 currentView，不再由 pending 字段持有
            var committed = _pendingView;
            _pendingView = null;

            if (_outgoingView != null && !ReferenceEquals(_outgoingView, committed))
            {
                try { _outgoingView.DisposeCharacterResources(); }
                catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 卸载旧角色失败：" + e.Message); }
            }

            _outgoingView = null;
            _outgoingDeactivated = false;
            _target = null;
            _step = 0;
            SetState(CharacterSwitchState.Committed);

            _lastResult = new CharacterSwitchResult(CharacterSwitchState.Committed, null,
                previousId, committed != null ? committed.CharacterId : string.Empty);
            OnSwitched?.Invoke(_lastResult);
        }

        // ==================== 终结路径 ====================

        private void CommitNoOp(string id)
        {
            SetState(CharacterSwitchState.Committed);
            _lastResult = new CharacterSwitchResult(CharacterSwitchState.Committed, null, id, id);
            OnSwitched?.Invoke(_lastResult);
        }

        /// <summary>
        /// 统一的中止路径：释放新角色临时资源 → 恢复旧角色引用与激活状态 → 置终结状态 → 派发事件。
        /// <paramref name="terminal"/> 只允许 RolledBack（失败）或 Cancelled（被新请求打断）。
        /// </summary>
        private void Abort(CharacterSwitchState terminal, string reason)
        {
            string previousId = _outgoingView != null ? _outgoingView.CharacterId : string.Empty;

            // ① 先把引用恢复成旧角色，避免后续任何调用方看到半成品
            var abandoned = _pendingView;
            _pendingView = null;
            if (ReferenceEquals(CurrentView, abandoned)) CurrentView = _outgoingView;

            // ② 释放新角色的临时资源（恰一次）
            if (abandoned != null)
            {
                try { abandoned.OnCharacterDeactivated(); }
                catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 回滚停用失败：" + e.Message); }
                try { abandoned.DisposeCharacterResources(); }
                catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 回滚释放资源失败：" + e.Message); }
            }

            // ③ 恢复旧角色
            if (_outgoingView != null && _outgoingDeactivated)
            {
                try { _outgoingView.OnCharacterActivated(); }
                catch (Exception e) { Debug.LogWarning("[CharacterSwitchService] 回滚恢复旧角色失败：" + e.Message); }
            }

            _outgoingView = null;
            _outgoingDeactivated = false;
            _target = null;
            _step = 0;
            SetState(terminal);

            _lastResult = new CharacterSwitchResult(terminal, reason,
                previousId, CurrentView != null ? CurrentView.CharacterId : previousId);
            OnSwitched?.Invoke(_lastResult);
        }

        private void SetState(CharacterSwitchState state)
        {
            if (State == state) return;
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
