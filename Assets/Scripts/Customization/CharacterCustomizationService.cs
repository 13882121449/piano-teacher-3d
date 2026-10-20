using System;
using System.IO;
using UnityEngine;
using PianoTeacher3D.Characters;

namespace PianoTeacher3D.Customization
{
    /// <summary>照片个性化流程阶段（方案 9.2）。UI 只读，不自行维护状态。</summary>
    public enum CustomizationPhase
    {
        /// <summary>空闲。</summary>
        Idle = 0,

        /// <summary>正在读取并校验照片。</summary>
        ValidatingPhoto = 1,

        /// <summary>正在生成（含内部重试）。</summary>
        Generating = 2,

        /// <summary>预览已就绪，等待用户确认（此阶段尚未改动外观）。</summary>
        PreviewReady = 3,

        /// <summary>已应用到角色。</summary>
        Applied = 4
    }

    /// <summary>
    /// 照片个性化编排（方案 9.2 流程）：
    /// <c>隐私同意 → 输入校验 → 提交任务 → 轮询进度 → 结果校验 → 预览（不应用）→ 用户确认后应用 → 记录可撤销点 → 删除/恢复默认</c>。
    /// <para>
    /// 硬约束：
    /// ① 同一时刻只允许一个在飞任务，重复提交直接拒绝并给可读原因（方案 12.2「按钮应避免重复提交」）；
    /// ② 失败 / 离线 / 超时 / 取消都<b>不改动当前外观</b>，也不触碰演奏层；
    /// ③ 预览与应用是两步：预览阶段绝不调用 <see cref="ICharacterView.ApplyAppearance"/>；
    /// ④ 主线程轮询（<see cref="Tick"/>），不用 <c>async void</c>，不引入网络调用。
    /// </para>
    /// </summary>
    public sealed class CharacterCustomizationService
    {
        private readonly AppearanceStorage _storage;
        private readonly PrivacyConsentController _consent;

        private IImageGenerationProvider _provider;
        private PhotoDecodeJob _decodeJob;
        private ImageGenerationJob _generationJob;

        private Texture2D _sourceTexture;
        private Texture2D _preview;
        private string _photoPath = string.Empty;
        private string _characterId = string.Empty;
        private int _requestCounter;

        /// <summary>构造。生成提供方可稍后通过 <see cref="SetProvider"/> 替换（例如离线降级）。</summary>
        public CharacterCustomizationService(AppearanceStorage storage, PrivacyConsentController consent,
            IImageGenerationProvider provider)
        {
            _storage = storage;
            _consent = consent;
            _provider = provider;

            // 启动清理（方案 9.6）：上一次异常退出可能留下写了一半的 .tmp / .stage
            if (_storage != null) _storage.CleanupIncomplete();
        }

        /// <summary>当前阶段。</summary>
        public CustomizationPhase Phase { get; private set; } = CustomizationPhase.Idle;

        /// <summary>是否有任务在飞（读图或生成）。</summary>
        public bool IsBusy => _decodeJob != null || _generationJob != null;

        /// <summary>进度（0..1）：校验阶段为 0，生成阶段按提供方回报。</summary>
        public float Progress01
        {
            get
            {
                if (_decodeJob != null) return 0f;
                if (_generationJob != null) return _generationJob.Progress01;
                return Phase == CustomizationPhase.PreviewReady || Phase == CustomizationPhase.Applied ? 1f : 0f;
            }
        }

        /// <summary>预览纹理（不转移所有权；应用成功后归应用器所有，这里变为 null）。</summary>
        public Texture2D Preview => _preview;

        /// <summary>是否有可确认的预览。</summary>
        public bool HasPreview => _preview != null;

        /// <summary>当前选择的风格（重新生成时复用）。</summary>
        public CharacterStyle Style { get; private set; } = CharacterStyle.Custom;

        /// <summary>是否已有可重新生成的源照片（解码成功后才为 true）。</summary>
        public bool HasSourcePhoto => _sourceTexture != null;

        /// <summary>
        /// 当前照片的文件名（<b>只给文件名，不暴露目录</b>：避免把用户路径显示在界面上）。
        /// </summary>
        public string SelectedPhotoFileName
        {
            get
            {
                if (string.IsNullOrEmpty(_photoPath)) return string.Empty;
                try
                {
                    return Path.GetFileName(_photoPath) ?? string.Empty;
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>已绑定的角色视图（可能为 null）。</summary>
        public ICharacterView View { get; private set; }

        /// <summary>外观应用器（未绑定角色时为 null）。</summary>
        public GeneratedAppearanceApplier Applier { get; private set; }

        /// <summary>最近一次失败的原因（成功或进行中为空）。</summary>
        public string LastErrorReason { get; private set; } = string.Empty;

        /// <summary>最近一次失败的下一步建议。</summary>
        public string LastErrorGuidance { get; private set; } = string.Empty;

        /// <summary>外观已应用但落盘失败时的提示（为空表示无问题）。</summary>
        public string StorageWarning { get; private set; } = string.Empty;

        /// <summary>是否有可撤销点。</summary>
        public bool CanUndo => Applier != null && Applier.CanUndo;

        /// <summary>当前生成提供方 ID（日志 / UI 展示）。</summary>
        public string ProviderId => _provider != null ? _provider.ProviderId : string.Empty;

        // ==================== 绑定 ====================

        /// <summary>
        /// 绑定角色视图（角色切换后请重新调用，<b>不要缓存视图实例</b>）。
        /// <para>
        /// 三种情况：
        /// ① 同一实例重复绑定 → 什么都不做（否则会把已应用外观误当「换角色」清掉）；
        /// ② <b>同一角色的新实例</b>（内置包装切换后会重建）→ 把已应用外观迁移到新视图，
        ///    纹理所有权不变，不复制、不销毁；
        /// ③ 换到别的角色 / 解绑 → 先让旧视图同帧解引用，再回收本类自有纹理（避免纹理泄漏）。
        /// </para>
        /// <para>
        /// 返回值 true 时 <paramref name="error"/> 也可能非空：那表示迁移时有槽位被新实例拒绝
        /// （外观已丢弃并恢复默认），调用方可以把它当提示展示。
        /// </para>
        /// </summary>
        public bool BindCharacter(ICharacterView view, out string error)
        {
            if (view == null)
            {
                // 解绑：旧视图先同帧解引用，再释放我们自己的纹理
                if (Applier != null) Applier.ReleaseForCharacterSwitch();
                View = null;
                Applier = null;
                _characterId = string.Empty;
                error = "角色为空，无法绑定外观。请先选择一个角色。";
                return false;
            }

            if (!CharacterIdRules.Validate(view.CharacterId).Ok)
            {
                error = "角色 ID 不合法（应为小写字母/数字开头，允许 . _ -），无法保存或应用外观。";
                return false;
            }

            if (Applier != null && ReferenceEquals(view, View))
            {
                error = string.Empty;
                return true;
            }

            // 同一角色的新实例：迁移外观（内置 0→1→0 会得到三个不同实例，不能丢外观）
            if (Applier != null && string.Equals(view.CharacterId, _characterId, StringComparison.Ordinal))
            {
                if (Applier.TryRetarget(view, out string migrateWarning))
                {
                    View = view;
                    error = migrateWarning;
                    return true;
                }
            }

            if (Applier != null) Applier.ReleaseForCharacterSwitch();

            View = view;
            _characterId = view.CharacterId;
            Applier = new GeneratedAppearanceApplier(view);
            error = string.Empty;
            return true;
        }

        /// <summary>替换生成提供方（离线降级 / 测试注入）。</summary>
        public void SetProvider(IImageGenerationProvider provider)
        {
            _provider = provider;
        }

        // ==================== 提交 / 轮询 ====================

        /// <summary>
        /// 提交照片。顺序：同意门禁 → 忙碌检查 → 提供方可用性 → 扩展名初筛 → 后台读图。
        /// 返回 false 时 <paramref name="error"/> 已包含「原因 + 下一步」。
        /// </summary>
        public bool Submit(string photoPath, CharacterStyle style, out string error)
        {
            if (!_consent.TryEnsureGranted(out string reason, out string guidance))
            {
                error = $"{reason}\n{guidance}";
                return false;
            }

            if (IsBusy)
            {
                error = "正在处理上一张照片，请等待完成或点「取消」后再试。";
                return false;
            }

            if (View == null || Applier == null)
            {
                error = "还没有选择角色，无法生成外观。请先在角色面板里选择一个角色。";
                return false;
            }

            if (_provider == null || !_provider.IsAvailable)
            {
                error = "图像生成当前不可用（离线或未配置）。当前外观保持不变，请稍后重试。";
                return false;
            }

            if (!PhotoInputValidator.HasAllowedExtension(photoPath))
            {
                error = "只支持 PNG / JPG 照片。请先把照片另存为 .png 或 .jpg，再重新选择。";
                return false;
            }

            ClearError();
            StorageWarning = string.Empty;
            DiscardPreview();
            DestroyTexture(ref _sourceTexture);

            _photoPath = photoPath ?? string.Empty;
            Style = style;
            _decodeJob = PhotoDecodeJob.Start(_photoPath);
            Phase = CustomizationPhase.ValidatingPhoto;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 主线程推进一步。<paramref name="now"/> 通常传 <c>Time.unscaledTime</c>，
        /// 测试传手动时钟以零等待推进。
        /// </summary>
        public void Tick(float now)
        {
            TickDecode();
            TickGeneration(now);
        }

        /// <summary>取消当前任务（读图或生成）。取消不影响当前外观，也不删除已有预览。</summary>
        public bool Cancel(out string error)
        {
            if (_decodeJob == null && _generationJob == null)
            {
                error = "当前没有正在进行的任务。";
                return false;
            }

            if (_decodeJob != null)
            {
                _decodeJob.Cancel();
                _decodeJob = null;
                _photoPath = string.Empty;
            }

            if (_generationJob != null)
            {
                _generationJob.Cancel();
                _generationJob = null;
            }

            Phase = _preview != null ? CustomizationPhase.PreviewReady : CustomizationPhase.Idle;
            error = string.Empty;
            return true;
        }

        // ==================== 预览 / 应用 ====================

        /// <summary>
        /// 应用预览到指定槽位（用户确认后才调用）。
        /// 失败时保留预览与当前外观，返回可读原因；成功时记录可撤销点。
        /// </summary>
        public bool ApplyPreview(AppearanceSlot slot, out string error)
        {
            if (_preview == null)
            {
                error = "还没有可应用的预览图。请先生成外观，确认预览后再点「应用」。";
                return false;
            }

            if (IsBusy)
            {
                error = "生成尚未结束，请等待完成或点「取消」后再应用。";
                return false;
            }

            if (Applier == null)
            {
                error = "还没有可应用外观的角色。请先选择一个角色。";
                return false;
            }

            if (!Applier.TryApply(slot, _preview, out string applyError))
            {
                // 失败：预览仍然归本类，继续保留给用户重试
                error = applyError;
                return false;
            }

            _preview = null;  // 所有权已转交应用器（视图正在引用，不能在这里销毁）
            Phase = CustomizationPhase.Applied;
            ClearError();
            PersistApplied(slot);
            error = string.Empty;
            return true;
        }

        /// <summary>用同一张照片与同一风格重新生成（旧预览在新结果就绪前保持不变）。</summary>
        public bool Regenerate(out string error)
        {
            if (IsBusy)
            {
                error = "正在生成中，请等待完成或点「取消」后再重新生成。";
                return false;
            }

            if (_sourceTexture == null || string.IsNullOrEmpty(_photoPath))
            {
                error = "没有可用于重新生成的照片。请重新选择一张照片。";
                return false;
            }

            if (_provider == null || !_provider.IsAvailable)
            {
                error = "图像生成当前不可用。当前外观保持不变，请稍后重试。";
                return false;
            }

            ClearError();
            StartGeneration();
            error = string.Empty;
            return true;
        }

        /// <summary>恢复该槽位的角色默认外观（并释放本类持有的纹理）。</summary>
        public bool RestoreDefault(AppearanceSlot slot, out string error)
        {
            if (IsBusy)
            {
                error = "生成尚未结束，请等待完成或点「取消」后再恢复默认。";
                return false;
            }

            if (Applier == null)
            {
                error = "还没有可恢复外观的角色。请先选择一个角色。";
                return false;
            }

            if (!Applier.TryRestoreDefault(slot, out string restoreError))
            {
                error = restoreError;
                return false;
            }

            if (_preview == null) Phase = CustomizationPhase.Idle;
            error = string.Empty;
            return true;
        }

        /// <summary>撤销上一次应用（回到上一份外观或角色默认外观）。</summary>
        public bool UndoLastApply(out string error)
        {
            if (IsBusy)
            {
                error = "生成尚未结束，请等待完成或点「取消」后再撤销。";
                return false;
            }

            if (Applier == null)
            {
                error = "还没有可撤销外观的角色。请先选择一个角色。";
                return false;
            }

            if (!Applier.TryUndo(out string undoError))
            {
                error = undoError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 恢复该角色上次保存的外观（重启或 A→B→A 切回后使用）。
        /// <para>
        /// 只读 <c>result.png</c> + <c>appearance.json</c>，不重新生成；已应用同一槽位时直接返回成功（幂等）。
        /// 失败时当前外观保持不变，并给出原因 + 下一步。
        /// </para>
        /// </summary>
        public bool TryRestoreStoredAppearance(out string error)
        {
            if (IsBusy)
            {
                error = "生成尚未结束，请等待完成或点「取消」后再恢复。";
                return false;
            }

            if (Applier == null || Applier.View == null)
            {
                error = "还没有可恢复外观的角色。请先选择一个角色。";
                return false;
            }

            if (_storage == null)
            {
                error = "没有配置照片存储，无法恢复外观。";
                return false;
            }

            if (!_storage.TryLoadManifest(_characterId, out AppearanceRecord record))
            {
                error = "这个角色还没有保存过外观。请先生成并应用一次。";
                return false;
            }

            if (!Enum.TryParse(record.Slot, out AppearanceSlot slot))
            {
                error = "保存的外观记录已损坏（槽位无法识别）。请重新生成一次。";
                return false;
            }

            if (!Applier.View.Supports(slot))
            {
                error = "该角色不支持已保存的外观槽位，无法恢复。请重新生成并选择「缩略图」或「整体色调」。";
                return false;
            }

            if (Enum.TryParse(record.Style, out CharacterStyle storedStyle)) Style = storedStyle;

            if (Applier.HasApplied(slot))
            {
                // 幂等：已经恢复过（或本次会话还留着），不重复读盘
                Phase = CustomizationPhase.Applied;
                ClearError();
                error = string.Empty;
                return true;
            }

            if (!_storage.TryLoadResult(_characterId, out Texture2D texture, out string loadError))
            {
                error = loadError;
                return false;
            }

            if (!Applier.TryApply(slot, texture, out string applyError))
            {
                DestroyTexture(ref texture);
                error = applyError;
                return false;
            }

            Phase = CustomizationPhase.Applied;
            ClearError();
            error = string.Empty;
            return true;
        }

        // ==================== 删除 / 释放 ====================

        /// <summary>
        /// 删除本地照片数据（原图 + 结果 + 缩略图 + 临时文件），恢复角色默认外观，
        /// 并释放预览与源图纹理。进行中的任务会先被取消（方案 9.6）。
        /// </summary>
        public bool DeletePhotoAndResult(out string error)
        {
            Cancel(out _);
            DiscardPreview();
            DestroyTexture(ref _sourceTexture);
            _photoPath = string.Empty;
            Phase = CustomizationPhase.Idle;
            StorageWarning = string.Empty;

            string restoreError = string.Empty;
            if (Applier != null) Applier.TryRestoreAll(out restoreError);

            if (string.IsNullOrEmpty(_characterId))
            {
                error = string.IsNullOrEmpty(restoreError) ? string.Empty : restoreError;
                return string.IsNullOrEmpty(restoreError);
            }

            if (_storage == null)
            {
                error = "没有配置照片存储，无法删除本地文件。";
                return false;
            }

            if (!_storage.DeleteOne(_characterId, out string deleteError))
            {
                error = deleteError;
                return false;
            }

            if (!string.IsNullOrEmpty(restoreError))
            {
                error = restoreError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>丢弃尚未应用的预览纹理（不触碰视图正在使用的纹理）。</summary>
        public void DiscardPreview()
        {
            DestroyTexture(ref _preview);
            if (Phase == CustomizationPhase.PreviewReady) Phase = CustomizationPhase.Idle;
        }

        // ==================== 内部：流程 ====================

        private void TickDecode()
        {
            if (_decodeJob == null) return;
            if (!_decodeJob.Poll()) return;

            PhotoDecodeJob finished = _decodeJob;
            _decodeJob = null;
            PhotoValidationResult result = finished.Result;

            if (!result.Ok)
            {
                Phase = CustomizationPhase.Idle;
                SetError(result.Reason, result.Guidance);
                _photoPath = string.Empty;
                return;
            }

            if (_storage != null && !_storage.TrySaveOriginal(_characterId, _photoPath, out string saveError))
            {
                // 原图存不下来就不继续：否则用户以为已保存，重启后却什么都没有
                Phase = CustomizationPhase.Idle;
                SetError(saveError, "请检查磁盘空间与文件权限，或换一台设备后再试。");
                // result 是 readonly struct，不能直接对 result.Texture 取 ref：先落到局部变量再销毁
                Texture2D rejected = result.Texture;
                DestroyTexture(ref rejected);
                _photoPath = string.Empty;
                return;
            }

            DestroyTexture(ref _sourceTexture);
            _sourceTexture = result.Texture;
            StartGeneration();
        }

        private void StartGeneration()
        {
            if (_sourceTexture == null || _provider == null) return;

            _requestCounter++;
            var request = GenerationRequest.Create(_sourceTexture, Style,
                $"req-{_requestCounter}-{DateTime.UtcNow.Ticks}");

            _generationJob = _provider.StartJob(request);
            Phase = CustomizationPhase.Generating;
        }

        private void TickGeneration(float now)
        {
            if (_generationJob == null) return;

            _generationJob.Tick(now);
            if (!_generationJob.IsFinished) return;

            ImageGenerationJob finished = _generationJob;
            _generationJob = null;

            if (finished.Succeeded)
            {
                // 旧预览在新结果就绪后才释放
                DiscardPreview();
                _preview = finished.Result;
                Phase = CustomizationPhase.PreviewReady;
                ClearError();
                return;
            }

            // 失败 / 超时 / 取消：一律不动当前外观，只把原因交给 UI
            Phase = CustomizationPhase.Idle;
            if (finished.State != ImageGenerationState.Canceled)
            {
                SetError(finished.ErrorMessage, finished.ErrorGuidance);
            }
        }

        private void PersistApplied(AppearanceSlot slot)
        {
            if (_storage == null || _sourceTexture == null) return;

            Texture2D applied = Applier != null ? Applier.GetApplied(slot) : null;

            if (!_storage.TrySaveResult(_characterId, applied, out string resultError))
            {
                StorageWarning = resultError;
                return;
            }

            if (!_storage.TrySaveThumbnail(_characterId, applied, out string thumbError))
            {
                StorageWarning = thumbError;
                return;
            }

            var record = new AppearanceRecord
            {
                CharacterId = _characterId,
                Slot = slot.ToString(),
                Style = Style.ToString(),
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                Width = applied != null ? applied.width : 0,
                Height = applied != null ? applied.height : 0,
                ProviderId = ProviderId
            };

            if (!_storage.TrySaveManifest(record, out string manifestError)) StorageWarning = manifestError;
        }

        private void SetError(string reason, string guidance)
        {
            LastErrorReason = string.IsNullOrEmpty(reason) ? "操作未完成。" : reason;
            LastErrorGuidance = string.IsNullOrEmpty(guidance) ? "请重试；若仍然失败，请换一张照片。" : guidance;
        }

        private void ClearError()
        {
            LastErrorReason = string.Empty;
            LastErrorGuidance = string.Empty;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
