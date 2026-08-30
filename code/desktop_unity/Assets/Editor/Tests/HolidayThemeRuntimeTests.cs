using NUnit.Framework;
using UnityEngine;

public class HolidayThemeRuntimeTests
{
    [SetUp]
    public void ResetTheme()
    {
        string ignored;
        HolidayThemeRuntime.TrySetTheme("off", out ignored);
    }

    [TearDown]
    public void RestoreDefaultTheme()
    {
        string ignored;
        HolidayThemeRuntime.TrySetTheme("off", out ignored);
    }

    [Test]
    public void ThemeSwitchesToChineseNewYearAndBack()
    {
        string message;
        Assert.IsTrue(HolidayThemeRuntime.TrySetTheme("cn_new_year", out message));
        Assert.AreEqual("cn_new_year", HolidayThemeRuntime.ActiveId);
        Assert.IsTrue(HolidayThemeRuntime.IsHolidayActive);
        StringAssert.Contains("新春主题", message);

        Assert.IsTrue(HolidayThemeRuntime.TrySetTheme("off", out message));
        Assert.AreEqual("default", HolidayThemeRuntime.ActiveId);
        Assert.IsFalse(HolidayThemeRuntime.IsHolidayActive);
    }

    [Test]
    public void UnknownThemeDoesNotReplaceCurrentTheme()
    {
        string message;
        Assert.IsFalse(HolidayThemeRuntime.TrySetTheme("not_a_theme", out message));
        Assert.AreEqual("default", HolidayThemeRuntime.ActiveId);
        StringAssert.Contains("未知节日主题", message);
    }

    [Test]
    public void ChineseNewYearAccessoryWritesOnlyBoundedPixels()
    {
        string ignored;
        HolidayThemeRuntime.TrySetTheme("cn_new_year", out ignored);
        var pixels = new Color32[17 * 24];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        HolidayThemeRuntime.ApplyPixelAccessory(pixels, 17, 24);

        int changed = 0;
        int minX = 17, maxX = -1, minY = 24, maxY = -1;
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 17; x++)
            {
                if (pixels[y * 17 + x].a <= 0) continue;
                changed++;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        Assert.Greater(changed, 0);
        Assert.LessOrEqual(changed, 60);
        Assert.GreaterOrEqual(minX, 1);
        Assert.LessOrEqual(maxX, 15);
        Assert.GreaterOrEqual(minY, 17);
        Assert.LessOrEqual(maxY, 23);
    }
}
