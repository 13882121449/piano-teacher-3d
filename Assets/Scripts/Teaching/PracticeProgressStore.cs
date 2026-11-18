using System;
using System.Collections.Generic;
using System.IO;
using PianoTeacher3D.Core;
using UnityEngine;

namespace PianoTeacher3D.Teaching
{
    /// <summary>
    /// 练习记录（2026-11-18）：每首曲子的最好成绩与星级、每天的练习时长、连续练习天数。
    /// 存在 <c>persistentDataPath/practice_progress.json</c>，用项目已有的原子写入（写临时文件再替换），
    /// 文件损坏时从空记录开始，不影响主流程。
    /// </summary>
    public sealed class PracticeProgressStore
    {
        [Serializable]
        public sealed class SongRecord
        {
            public string Key;
            public float BestAccuracy;
            public int Stars;
            public float BestSpeed;
            public int Plays;
        }

        [Serializable]
        public sealed class DayRecord
        {
            public string Date;      // yyyy-MM-dd（本地日期）
            public float Seconds;
        }

        [Serializable]
        private sealed class Data
        {
            public List<SongRecord> Songs = new List<SongRecord>();
            public List<DayRecord> Days = new List<DayRecord>();
        }

        private static PracticeProgressStore _shared;
        private readonly string _path;
        private Data _data = new Data();
        private float _unsavedSeconds;

        /// <summary>全局共享实例（主菜单与课程场景共用）。</summary>
        /// <remarks>命令行批处理（自动化测试）时用临时目录里的独立文件，不污染用户真实的练习记录。</remarks>
        public static PracticeProgressStore Shared => _shared ??= new PracticeProgressStore(
            Application.isBatchMode ? Path.Combine(Path.GetTempPath(), "pt3d_batch_practice_progress.json") : DefaultPath);

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "practice_progress.json");

        public PracticeProgressStore(string path)
        {
            _path = path;
            Load();
        }

        /// <summary>曲目记录的键：文件名（不含目录，大小写不敏感）。</summary>
        public static string KeyFor(string songPath)
            => string.IsNullOrEmpty(songPath) ? string.Empty : Path.GetFileName(songPath).ToLowerInvariant();

        public SongRecord GetSong(string songPath)
        {
            string key = KeyFor(songPath);
            return _data.Songs.Find(s => s.Key == key);
        }

        /// <summary>记一遍成绩：只在比原来好时更新最好成绩与星级。返回是否刷新了纪录。</summary>
        public bool RecordRun(string songPath, float accuracy, float speed)
        {
            string key = KeyFor(songPath);
            if (string.IsNullOrEmpty(key)) return false;
            var record = _data.Songs.Find(s => s.Key == key);
            if (record == null)
            {
                record = new SongRecord { Key = key };
                _data.Songs.Add(record);
            }
            record.Plays++;
            int stars = PracticeScorer.StarsFor(accuracy, speed);
            bool improved = accuracy > record.BestAccuracy + 1e-4f || stars > record.Stars;
            if (accuracy > record.BestAccuracy) record.BestAccuracy = accuracy;
            if (stars > record.Stars) record.Stars = stars;
            if (speed > record.BestSpeed) record.BestSpeed = speed;
            Save();
            return improved;
        }

        /// <summary>累计今天的练习时长（秒），满 30 s 落盘一次。</summary>
        public void AddPracticeSeconds(float seconds, DateTime? now = null)
        {
            if (seconds <= 0f) return;
            string date = (now ?? DateTime.Now).ToString("yyyy-MM-dd");
            var day = _data.Days.Find(d => d.Date == date);
            if (day == null)
            {
                day = new DayRecord { Date = date };
                _data.Days.Add(day);
            }
            day.Seconds += seconds;
            _unsavedSeconds += seconds;
            if (_unsavedSeconds >= 30f) Save();
        }

        public float SecondsOn(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            var day = _data.Days.Find(d => d.Date == key);
            return day != null ? day.Seconds : 0f;
        }

        /// <summary>连续练习天数：从今天（今天没练则从昨天）往前数，每天至少 60 s 才算。</summary>
        public int StreakDays(DateTime? today = null)
        {
            DateTime day = (today ?? DateTime.Now).Date;
            if (SecondsOn(day) < 60f) day = day.AddDays(-1);
            int streak = 0;
            while (SecondsOn(day) >= 60f)
            {
                streak++;
                day = day.AddDays(-1);
            }
            return streak;
        }

        public void Save()
        {
            _unsavedSeconds = 0f;
            try
            {
                string json = JsonUtility.ToJson(_data, true);
                if (!AtomicFileWrite.TryWriteAllText(_path, json, out string error))
                    Debug.LogWarning("[Practice] 练习记录保存失败：" + error);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Practice] 练习记录保存失败：" + e.Message);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var loaded = JsonUtility.FromJson<Data>(File.ReadAllText(_path));
                if (loaded != null) _data = loaded;
                _data.Songs ??= new List<SongRecord>();
                _data.Days ??= new List<DayRecord>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Practice] 练习记录读取失败，从空记录开始：" + e.Message);
                _data = new Data();
            }
        }
    }
}
