using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using QualityTierValue = PianoTeacher3D.Core.QualityTier;

namespace PianoTeacher3D.Core
{
    /// <summary>
    /// 用户偏好（对应方案 9.2「用户偏好（最近曲目、最后视角、UI 设置）与产品默认配置分开保存；
    /// 用户数据使用独立持久化目录，不写回 <c>Assets/StreamingAssets</c>」）。
    /// <para>
    /// 持久化位置：<c>Application.persistentDataPath/PianoTeacher3D/user-prefs.json</c>（<c>JsonUtility</c>）。
    /// 产品默认值集中在内嵌的 <see cref="Defaults"/>，不与用户数据混在一起。
    /// </para>
    /// <para>
    /// <see cref="Load"/> 对「文件缺失 / 空文件 / 损坏 JSON / 越界数值」容错：回落默认值并在
    /// <see cref="LoadDiagnostic"/> 留下可诊断信息；解析失败时<b>不</b>覆盖用户原文件，直到下一次成功 <see cref="Save"/>。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class UserPreferences
    {
        /// <summary>
        /// 产品默认配置（与用户偏好分开保存；不要把这个类里的常量复制到其它脚本，方案 9.2）。
        /// </summary>
        public static class Defaults
        {
            /// <summary>默认课程速度。</summary>
            public const float Speed = 1f;

            /// <summary>默认每小节拍数（4/4）。</summary>
            public const int BeatsPerBar = 4;

            /// <summary>默认预提示提前量（秒）。</summary>
            public const float HighlightLookAhead = 0.6f;

            /// <summary>默认角色下标。</summary>
            public const int AvatarIndex = 0;

            /// <summary>默认视角：1 = 全景（<c>CameraControl.CameraMode.Full</c>）。</summary>
            public const int LastCameraMode = 1;

            /// <summary>默认练习手：0 = 双手（Core.PracticeHand 的 Both）。</summary>
            public const int PracticeHand = 0;

            /// <summary>默认琴键高亮：3 = 预提示 + 发声（<c>Piano.HighlightMode.Both</c>）。</summary>
            public const int HighlightMode = 3;

            /// <summary>默认关闭节拍器。</summary>
            public const bool MetronomeEnabled = false;

            /// <summary>默认展开练习工具栏。</summary>
            public const bool PracticeToolsCollapsed = false;
            public const int DisplayMode = 0;            // 展示模式
            public const int DisplayModeMin = 0;
            public const int DisplayModeMax = 1;

            /// <summary>
            /// 质量档位默认值：0 = Standard（<c>Core.QualityTier</c>）。
            /// 仅当 <see cref="QualityTierUserOverride"/> 为 true 时该值才生效；否则按设备建议（Auto）解析。
            /// </summary>
            public const int QualityTier = 0;

            // ---- 合法范围（clamp 用；改动这里即可同时影响加载与保存） ----
            public const float SpeedMin = 0.1f;
            public const float SpeedMax = 4f;
            public const int BeatsPerBarMin = 1;
            public const int BeatsPerBarMax = 16;
            public const float HighlightLookAheadMin = 0.05f;
            public const float HighlightLookAheadMax = 3f;
            public const int AvatarIndexMin = 0;
            public const int AvatarIndexMax = 2;
            public const int CameraModeMin = 0;
            public const int CameraModeMax = 2;   // Closeup / Full / Free
            public const int PracticeHandMin = 0;
            public const int PracticeHandMax = 2; // Both / LeftOnly / RightOnly
            public const int HighlightModeMin = 0;
            public const int HighlightModeMax = 3; // Off / Upcoming / Active / Both
            public const int QualityTierMin = 0;
            public const int QualityTierMax = 1;   // Standard / High
            public const int MaxRecentSongIds = 20;
        }

        /// <summary>最后打开的曲目 ID（安全内部 ID，不含路径）。</summary>
        public string LastSongId = "";

        /// <summary>最后使用的视角（<c>CameraControl.CameraMode</c> 的整数值）。</summary>
        public int LastCameraMode = Defaults.LastCameraMode;

        /// <summary>课程速度。</summary>
        public float Speed = Defaults.Speed;

        /// <summary>练习手（0 = 双手、1 = 仅左手、2 = 仅右手，与 Core 的 PracticeHand 枚举一致）。</summary>
        public int PracticeHand = Defaults.PracticeHand;

        /// <summary>琴键高亮模式（<c>Piano.HighlightMode</c>）。</summary>
        public int HighlightMode = Defaults.HighlightMode;

        /// <summary>角色下标。</summary>
        public int AvatarIndex = Defaults.AvatarIndex;

        /// <summary>节拍器是否开启。</summary>
        public bool MetronomeEnabled = Defaults.MetronomeEnabled;

        /// <summary>每小节拍数。</summary>
        public int BeatsPerBar = Defaults.BeatsPerBar;

        /// <summary>预提示提前量（秒）。</summary>
        public float HighlightLookAhead = Defaults.HighlightLookAhead;

        /// <summary>练习工具栏是否折叠。</summary>
        public bool PracticeToolsCollapsed = Defaults.PracticeToolsCollapsed;

        /// <summary>显示模式：0 = 展示（完整手部动画），1 = 教学（隐藏手部，仅高亮被按下的琴键）。</summary>
        public int DisplayMode = Defaults.DisplayMode;

        /// <summary>
        /// 质量档位（对应 <c>Core.QualityTier</c>：0 = Standard，1 = High）。
        /// 该枚举由另一位同事在 <c>Core/QualityProfile.cs</c> 中新建，为避免并行改动冲突，这里先用 int 保存。
        /// </summary>
        public int QualityTier = Defaults.QualityTier;

        /// <summary>
        /// 用户是否<b>显式</b>选择过画质档位（优化方案 P1-07「区分 Auto / Standard / High 或增加 UserOverride」）。
        /// <para>
        /// false = Auto：启动时走 <see cref="QualityProfile.DetectRecommended"/> 的设备建议，不盲目应用 High；
        /// true = 使用 <see cref="QualityTier"/> 的用户覆盖值。
        /// 旧版本偏好文件没有该字段，反序列化后为 false，即自动回到 Auto，符合「首次启动走设备建议」。
        /// </para>
        /// </summary>
        public bool QualityTierUserOverride = false;

        /// <summary>最近曲目的 ID 列表（最新在前）。</summary>
        public List<string> RecentSongIds = new List<string>();

        /// <summary>最近一次 <see cref="Load"/> 的诊断信息（不持久化，仅用于日志 / 排查）。</summary>
        [NonSerialized] public string LoadDiagnostic;

        /// <summary>偏好文件路径（独立持久化目录，方案 9.2）。</summary>
        public static string FilePath
        {
            get
            {
                string root;
                try
                {
                    root = Application.persistentDataPath;
                    if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                }
                catch (Exception)
                {
                    root = Path.GetTempPath();
                }
                return Path.Combine(Path.Combine(root, "PianoTeacher3D"), "user-prefs.json");
            }
        }

        /// <summary>
        /// 读取偏好：缺失 / 空 / 损坏都回落默认值（不抛异常）；解析成功后再做范围 clamp。
        /// 不覆盖用户原文件（只有下一次成功 <see cref="Save"/> 才会写回）。
        /// </summary>
        public void Load()
        {
            ResetToDefaults();
            LoadDiagnostic = null;

            string path = FilePath;
            string json;
            try
            {
                if (!File.Exists(path))
                {
                    LoadDiagnostic = "首次运行或偏好文件不存在，已使用默认设置。";
                    return;
                }
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoadDiagnostic = $"读取偏好失败，已使用默认设置：{ex.GetType().Name}";
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                LoadDiagnostic = "偏好文件为空，已使用默认设置。";
                return;
            }

            UserPreferences loaded = null;
            try
            {
                loaded = JsonUtility.FromJson<UserPreferences>(json);
            }
            catch (Exception ex)
            {
                LoadDiagnostic = $"偏好文件损坏（无法解析），已使用默认设置：{ex.GetType().Name}";
                return;
            }

            if (loaded == null)
            {
                LoadDiagnostic = "偏好文件损坏（内容为空），已使用默认设置。";
                return;
            }

            CopyFrom(loaded);
            ClampAll();
        }

        /// <summary>
        /// 写入偏好（幂等；失败只记录诊断，不抛异常、不影响本次运行）。
        /// 采用「临时文件 + 原子替换」（优化方案 P1-10）：异常退出不会留下半截 JSON。
        /// </summary>
        public void Save()
        {
            ClampAll();
            if (AtomicFileWrite.TryWriteAllText(FilePath, JsonUtility.ToJson(this, true), out string error))
            {
                LoadDiagnostic = null;
                return;
            }
            LoadDiagnostic = $"保存偏好失败：{error}";
        }

        /// <summary>
        /// 解析实际生效的画质档位（优化方案 P1-07）：
        /// 用户显式覆盖过就用覆盖值，否则使用设备建议（Auto）。
        /// </summary>
        public QualityTier ResolveQualityTier()
        {
            if (!QualityTierUserOverride) return QualityProfile.DetectRecommended();
            return QualityTier == (int)QualityTierValue.High ? QualityTierValue.High : QualityTierValue.Standard;
        }

        /// <summary>记录用户显式选择的画质档位，并置上覆盖标记（下一次启动仍然生效）。</summary>
        public void SetQualityTierOverride(QualityTier tier)
        {
            QualityTier = tier == QualityTierValue.High ? (int)QualityTierValue.High : (int)QualityTierValue.Standard;
            QualityTierUserOverride = true;
        }

        /// <summary>恢复画质自动档（清掉覆盖标记，下次启动重新按设备建议）。</summary>
        public void UseAutomaticQuality()
        {
            QualityTierUserOverride = false;
        }

        /// <summary>恢复全部默认值（最近曲目 ID 一并清空）。</summary>
        public void ResetToDefaults()
        {
            LastSongId = "";
            LastCameraMode = Defaults.LastCameraMode;
            Speed = Defaults.Speed;
            PracticeHand = Defaults.PracticeHand;
            HighlightMode = Defaults.HighlightMode;
            AvatarIndex = Defaults.AvatarIndex;
            MetronomeEnabled = Defaults.MetronomeEnabled;
            BeatsPerBar = Defaults.BeatsPerBar;
            HighlightLookAhead = Defaults.HighlightLookAhead;
            PracticeToolsCollapsed = Defaults.PracticeToolsCollapsed;
            DisplayMode = Defaults.DisplayMode;
            QualityTier = Defaults.QualityTier;
            QualityTierUserOverride = false;
            RecentSongIds = new List<string>();
        }

        // ==================== 内部：复制与 clamp ====================

        private void CopyFrom(UserPreferences other)
        {
            LastSongId = other.LastSongId ?? "";
            LastCameraMode = other.LastCameraMode;
            Speed = other.Speed;
            PracticeHand = other.PracticeHand;
            HighlightMode = other.HighlightMode;
            AvatarIndex = other.AvatarIndex;
            MetronomeEnabled = other.MetronomeEnabled;
            BeatsPerBar = other.BeatsPerBar;
            HighlightLookAhead = other.HighlightLookAhead;
            PracticeToolsCollapsed = other.PracticeToolsCollapsed;
            DisplayMode = other.DisplayMode;
            QualityTier = other.QualityTier;
            QualityTierUserOverride = other.QualityTierUserOverride;
            RecentSongIds = other.RecentSongIds != null ? new List<string>(other.RecentSongIds) : new List<string>();
        }

        /// <summary>把所有数值收敛到合法范围（数值型 clamp；枚举型越界回落默认；NaN / Infinity 回落默认）。</summary>
        private void ClampAll()
        {
            Speed = ClampFloat(Speed, Defaults.SpeedMin, Defaults.SpeedMax, Defaults.Speed);
            BeatsPerBar = ClampRange(BeatsPerBar, Defaults.BeatsPerBarMin, Defaults.BeatsPerBarMax);
            HighlightLookAhead = ClampFloat(HighlightLookAhead, Defaults.HighlightLookAheadMin,
                Defaults.HighlightLookAheadMax, Defaults.HighlightLookAhead);
            AvatarIndex = ClampRange(AvatarIndex, Defaults.AvatarIndexMin, Defaults.AvatarIndexMax);
            LastCameraMode = ClampEnum(LastCameraMode, Defaults.CameraModeMin, Defaults.CameraModeMax, Defaults.LastCameraMode);
            PracticeHand = ClampEnum(PracticeHand, Defaults.PracticeHandMin, Defaults.PracticeHandMax, Defaults.PracticeHand);
            HighlightMode = ClampEnum(HighlightMode, Defaults.HighlightModeMin, Defaults.HighlightModeMax, Defaults.HighlightMode);
            QualityTier = ClampEnum(QualityTier, Defaults.QualityTierMin, Defaults.QualityTierMax, Defaults.QualityTier);

            if (string.IsNullOrEmpty(LastSongId)) LastSongId = "";

            if (RecentSongIds == null) RecentSongIds = new List<string>();
            RecentSongIds.RemoveAll(id => string.IsNullOrEmpty(id));
            while (RecentSongIds.Count > Defaults.MaxRecentSongIds)
                RecentSongIds.RemoveAt(RecentSongIds.Count - 1);
        }

        private static float ClampFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ClampRange(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ClampEnum(int value, int min, int max, int fallback)
        {
            if (value < min || value > max) return fallback;
            return value;
        }
    }
}
