using System;
using System.IO;
using NUnit.Framework;
using PianoTeacher3D.Customization;
using UnityEngine;

public class PhotoAvatarFitterTests
{
    private static Vector2[] FrontalLandmarks() => new[]
    {
        new Vector2(.5f, .22f), new Vector2(.22f, .52f), new Vector2(.78f, .52f), new Vector2(.5f, .80f),
        new Vector2(.39f, .43f), new Vector2(.61f, .43f), new Vector2(.5f, .53f), new Vector2(.5f, .65f)
    };

    [Test]
    public void FitBlendsIntoTemplateAndChecksLandmarkOrder()
    {
        var image = new Texture2D(240, 240, TextureFormat.RGBA32, false);
        var pixels = new Color32[240 * 240];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 135, 99, 255);
        image.SetPixels32(pixels);
        image.Apply();
        Texture2D atlas = null;
        try
        {
            var marks = FrontalLandmarks();
            Assert.IsTrue(PhotoAvatarFitter.TryFit(image, marks, false, out atlas, out var scale, out var error), error);
            Assert.AreEqual(512, atlas.width);
            Assert.Greater(atlas.GetPixelBilinear(.50f, .64f).a, .8f, "脸颊区域应写入照片颜色");
            Assert.AreEqual(0f, atlas.GetPixelBilinear(.430f, .715f).a, .03f, "眼球区域保留三维模板");
            Assert.AreEqual(0f, atlas.GetPixelBilinear(.1f, .9f).a, .001f, "照片边界以透明度衔接发际");
            Assert.That(scale.x, Is.InRange(.88f, 1.12f));
            (marks[4], marks[5]) = (marks[5], marks[4]);
            Assert.IsFalse(PhotoAvatarFitter.TryFit(image, marks, false, out var invalid, out _, out _));
            Assert.IsNull(invalid);
        }
        finally
        {
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    [Test]
    public void ArchiveMovesSavedAvatarOutOfActiveLibrary()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_verify_out",
            "1115_照片角色_v1", "storage_" + Guid.NewGuid().ToString("N")));
        var storage = new PhotoAvatarStorage(folder);
        var data = new PhotoAvatarData
        {
            Id = "photo." + Guid.NewGuid().ToString("N"),
            DisplayName = "测试角色",
            Outfit = 2,
            FaceScale = Vector2.one,
            FittedAtlas = "测试用生成数据"
        };
        Assert.IsTrue(storage.Save(data, out var error), error);
        var entries = new PhotoAvatarStorage(folder).LoadAll();
        Assert.AreEqual(1, entries.Length);
        Assert.AreEqual(data.Id, entries[0].Id);
        Assert.AreEqual(2, entries[0].Outfit);
        Assert.IsTrue(storage.Archive(entries[0], out error), error);
        Assert.IsEmpty(new PhotoAvatarStorage(folder).LoadAll());
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(folder, "trash")).Length);
    }
}
