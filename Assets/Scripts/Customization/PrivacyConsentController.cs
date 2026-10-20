using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PianoTeacher3D.Core;

namespace PianoTeacher3D.Customization
{
    /// <summary>
    /// 隐私同意门禁（方案 9.6 / 9.7）。
    /// <para>
    /// 五要素文案必须是常量（不允许 UI 自己写一套，避免不同界面说法不一致）：
    /// 用途、处理方、是否保存、保存时长、删除方式。
    /// </para>
    /// <para>
    /// 同意状态带时间戳持久化到 <c>{customization}/consent.json</c>；
    /// <b>未同意不得提交生成任务</b>（由 <see cref="CharacterCustomizationService"/> 强制）。
    /// 同意记录与照片数据分开存放：用户删照片时不会顺手把「已同意」也删掉，
    /// 只有显式撤销才会清除。
    /// </para>
    /// </summary>
    public sealed class PrivacyConsentController
    {
        /// <summary>同意文案版本。文案有实质变化时 +1，旧版本记录会被视为需要重新同意。</summary>
        public const int ConsentVersion = 1;

        /// <summary>同意记录文件名。</summary>
        public const string ConsentFileName = "consent.json";

        // ---- 五要素文案（常量，UI 直接展示） ----

        /// <summary>用途。</summary>
        public const string PurposeText = "用途：把这张照片变换成角色的外观（缩略图 / 脸部贴图等），仅用于本机显示与练习界面。";

        /// <summary>处理方。</summary>
        public const string ProcessorText = "处理方：全部处理都在本机完成，不上传到任何服务器，也不会发起网络请求。";

        /// <summary>是否保存。</summary>
        public const string StorageText = "是否保存：仅保存在本机的应用数据目录（customization 文件夹），不会写入项目资源目录。";

        /// <summary>保存时长。</summary>
        public const string RetentionText = "保存时长：直到你在面板里点「删除照片与结果」，否则会一直保留。";

        /// <summary>删除方式。</summary>
        public const string DeletionText = "删除方式：面板底部「删除照片与结果」会同时删除原图、生成结果与缩略图。";

        /// <summary>同意记录（JSON）。不含任何图片内容或路径。</summary>
        [Serializable]
        public sealed class ConsentRecord
        {
            /// <summary>文案版本。</summary>
            public int Version;

            /// <summary>是否已同意。</summary>
            public bool Accepted;

            /// <summary>同意时间（UTC，ISO 8601）。</summary>
            public string AcceptedAtUtc = string.Empty;
        }

        private readonly string _root;
        private readonly Func<DateTime> _utcNow;

        private ConsentRecord _record = new ConsentRecord();

        /// <summary>
        /// 构造。<paramref name="customizationRoot"/> 通常是 <see cref="AppearanceStorage.DefaultRoot"/>；
        /// 测试传临时目录。<paramref name="utcNow"/> 可注入固定时间，便于断言时间戳。
        /// </summary>
        public PrivacyConsentController(string customizationRoot, Func<DateTime> utcNow = null)
        {
            _root = customizationRoot ?? string.Empty;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            Load();
        }

        /// <summary>同意记录路径。</summary>
        public string ConsentFilePath => Path.Combine(_root, ConsentFileName);

        /// <summary>是否已同意（版本过期视为未同意）。</summary>
        public bool IsGranted => _record != null && _record.Accepted && _record.Version == ConsentVersion;

        /// <summary>同意时间（未同意时为 null）。</summary>
        public DateTime? GrantedAtUtc
        {
            get
            {
                if (!IsGranted || string.IsNullOrEmpty(_record.AcceptedAtUtc)) return null;
                if (DateTime.TryParse(_record.AcceptedAtUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime parsed)) return parsed;
                return null;
            }
        }

        /// <summary>拼出完整的同意文案（面板直接展示，不做二次加工）。</summary>
        public static string ComposeConsentText() => string.Join("\n", new[]
        {
            PurposeText,
            ProcessorText,
            StorageText,
            RetentionText,
            DeletionText
        });

        /// <summary>读取同意记录（缺失 / 损坏都视为未同意，不抛异常）。</summary>
        public void Load()
        {
            _record = new ConsentRecord();
            try
            {
                string path = ConsentFilePath;
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                var loaded = JsonUtility.FromJson<ConsentRecord>(json);
                if (loaded != null) _record = loaded;
            }
            catch (Exception)
            {
                // 损坏的同意记录 = 未同意：宁可多问一次，也不在用户不知情时处理照片
                _record = new ConsentRecord();
            }
        }

        /// <summary>记录同意（带 UTC 时间戳）。写盘失败时返回 false，且内存状态不置为已同意。</summary>
        public bool TryGrant(out string error)
        {
            var record = new ConsentRecord
            {
                Version = ConsentVersion,
                Accepted = true,
                AcceptedAtUtc = _utcNow().ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
            };

            if (!TryPersist(record, out error)) return false;

            _record = record;
            error = string.Empty;
            return true;
        }

        /// <summary>撤销同意：删除记录文件；失败时保持原状态并返回原因。</summary>
        public bool TryRevoke(out string error)
        {
            try
            {
                string path = ConsentFilePath;
                if (File.Exists(path)) File.Delete(path);
                AtomicFileWrite.TryCleanupTemp(path);
                _record = new ConsentRecord();
                error = string.Empty;
                return true;
            }
            catch (Exception)
            {
                error = "撤销隐私同意失败（记录文件可能正被占用）。请关闭其它程序后重试。";
                return false;
            }
        }

        /// <summary>
        /// 门禁检查：未同意时给出「原因 + 下一步」，供 UI 直接展示（方案 12.3）。
        /// </summary>
        public bool TryEnsureGranted(out string reason, out string guidance)
        {
            if (IsGranted)
            {
                reason = string.Empty;
                guidance = string.Empty;
                return true;
            }

            reason = "还没有同意照片处理说明，无法开始生成。";
            guidance = "请先展开「隐私说明」，勾选「我已阅读并同意」，再点「生成外观」。";
            return false;
        }

        private bool TryPersist(ConsentRecord record, out string error)
        {
            string path = ConsentFilePath;
            string json = JsonUtility.ToJson(record, true);
            if (AtomicFileWrite.TryWriteAllText(path, json, out string writeError))
            {
                error = string.Empty;
                return true;
            }

            error = $"保存同意记录失败：{writeError}。请检查磁盘空间后重试。";
            return false;
        }
    }
}
