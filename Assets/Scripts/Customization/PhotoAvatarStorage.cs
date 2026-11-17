using System;
using System.IO;
using PianoTeacher3D.Characters;
using UnityEngine;

namespace PianoTeacher3D.Customization
{
    /// <summary>存储生成的脸部贴图和角色外观参数，照片与照片路径不会写入档案。</summary>
    [Serializable]
    public sealed class PhotoAvatarData
    {
        public string Id;
        public string DisplayName;
        public bool ShortHair;
        public int Outfit;
        public int Complexion;
        public Vector2 FaceScale = Vector2.one;
        public string FittedAtlas;

        public bool IsValid()
        {
            return Id != null && Id.StartsWith("photo.", StringComparison.Ordinal) &&
                Guid.TryParseExact(Id.Substring(6), "N", out _) &&
                !string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Length <= 22 &&
                Outfit >= 0 && Outfit <= 2 && Complexion >= 0 && Complexion <= 2 &&
                FaceScale.x >= .88f && FaceScale.x <= 1.12f &&
                FaceScale.y >= .92f && FaceScale.y <= 1.09f &&
                !string.IsNullOrEmpty(FittedAtlas) && FittedAtlas.Length <= 1500000;
        }

        public CharacterProfile CreateProfile()
        {
            var profile = CharacterRegistry.CreateRocketboxProfile(Id, DisplayName,
                ShortHair ? CharacterRegistry.RocketboxMaleResourcesPath : CharacterRegistry.RocketboxResourcesPath,
                ShortHair ? 46 : 42, "本地照片 · 标点拟合");
            profile.PhotoAvatar = this;
            profile.Style = CharacterStyle.Custom;
            // 照片角色从此存储读取外观，避免旧缩略图外观管线覆盖脸部。
            profile.SupportedAppearanceSlots = Array.Empty<AppearanceSlot>();
            return profile;
        }
    }

    public sealed class PhotoAvatarStorage
    {
        public const int MaxProfiles = 24;
        private const long MaxFileSize = 1600000;
        private readonly string _folder;

        public PhotoAvatarStorage(string folder = null)
        {
            _folder = folder ?? Path.Combine(Application.persistentDataPath, "PianoTeacher3D", "PhotoAvatars");
        }

        public PhotoAvatarData[] LoadAll()
        {
            var found = new System.Collections.Generic.List<PhotoAvatarData>();
            try
            {
                if (!Directory.Exists(_folder)) return found.ToArray();
                var paths = Directory.GetFiles(_folder, "photo.*.json");
                Array.Sort(paths, StringComparer.Ordinal);
                foreach (string path in paths)
                {
                    if (found.Count >= MaxProfiles) break;
                    try
                    {
                        long size = new FileInfo(path).Length;
                        if (size <= 0 || size > MaxFileSize) continue;
                        var data = JsonUtility.FromJson<PhotoAvatarData>(File.ReadAllText(path));
                        if (data != null && data.IsValid() &&
                            string.Equals(Path.GetFileNameWithoutExtension(path), data.Id, StringComparison.Ordinal))
                            found.Add(data);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                              e is ArgumentException || e is System.Security.SecurityException)
                    {
                        // 坏档案由目录保留，照常加载其它角色。
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return found.ToArray();
        }

        public bool Save(PhotoAvatarData data, out string error)
        {
            if (data == null || !data.IsValid())
            {
                error = "生成的角色档案不完整，请返回重新预览。";
                return false;
            }
            try
            {
                Directory.CreateDirectory(_folder);
                string destination = Path.Combine(_folder, data.Id + ".json");
                if (File.Exists(destination))
                {
                    error = "角色已保存。";
                    return false;
                }
                string pending = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
                File.WriteAllText(pending, JsonUtility.ToJson(data));
                File.Move(pending, destination);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = "无法写入本地角色库，请确认存储空间与写入权限。";
                return false;
            }
        }

        public bool Archive(PhotoAvatarData data, out string error)
        {
            if (data == null || !data.IsValid())
            {
                error = "这个角色没有可移除的照片档案。";
                return false;
            }
            try
            {
                string source = Path.Combine(_folder, data.Id + ".json");
                string trash = Path.Combine(_folder, "trash");
                Directory.CreateDirectory(trash);
                string destination = Path.Combine(trash, data.Id + "." + Guid.NewGuid().ToString("N") + ".json");
                File.Move(source, destination);
                error = null;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = "无法把角色移动到本地回收目录，请检查文件是否被占用。";
                return false;
            }
        }
    }
}
