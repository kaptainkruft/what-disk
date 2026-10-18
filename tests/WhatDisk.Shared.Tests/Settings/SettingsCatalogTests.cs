using System.Text.RegularExpressions;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Settings;

/// <summary>
/// Checks the catalogue of settings: unique, consistently named paths, sections that match their settings, and
/// defaults that are valid values on every platform.
/// </summary>
public sealed partial class SettingsCatalogTests
{
    /// <summary>
    /// Every setting has a unique path, in camel case, and belongs to the section that lists it.
    /// </summary>
    [Fact]
    public void Paths_AreUniqueCamelCaseAndInTheirSection()
    {
        Assert.Equal(SettingsCatalog.All.Count(), SettingsCatalog.All.Select(static setting => setting.Path).Distinct().Count());
        Assert.Equal(SettingsCatalog.Sections.Count, SettingsCatalog.Sections.Select(static section => section.Key).Distinct().Count());
        foreach (var section in SettingsCatalog.Sections)
        {
            Assert.Matches(CamelCase(), section.Key);
            Assert.NotEmpty(section.Title);
            Assert.NotEmpty(section.Description);
            Assert.All(section.Settings, setting =>
            {
                Assert.Equal(section.Key, setting.Section);
                Assert.Matches(CamelCase(), setting.Key);
                Assert.NotEmpty(setting.Label);
                Assert.NotEmpty(setting.Description);
            });
        }
    }

    /// <summary>
    /// Every default is a valid value of its setting on every platform: numbers in range, choices among the choices.
    /// </summary>
    /// <param name="platformName">The platform.</param>
    [Theory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public void Defaults_AreValidOnEveryPlatform(string platformName)
    {
        var platform = TestPlatforms.Named(platformName);
        foreach (var setting in SettingsCatalog.All)
        {
            switch (setting)
            {
                case IntSetting number:
                    Assert.InRange(number.DefaultFor(platform), number.Minimum, number.Maximum);
                    break;
                case ChoiceSetting choice:
                    Assert.Contains(choice.DefaultFor(platform), choice.Choices.Select(static option => option.Value));
                    break;
            }
        }
    }

    /// <summary>
    /// Platform settings are stored under the platform's key in their section.
    /// </summary>
    [Fact]
    public void PlatformSettings_HaveThePlatformInTheirPath()
    {
        Assert.Equal("scanning.macos.showHiddenVolumes", SettingsCatalog.Scanning.ShowHiddenVolumes.Path);
        Assert.Equal("deleting.windows.warnWhenNotRecyclable", SettingsCatalog.Deleting.WarnWhenNotRecyclable.Path);
        Assert.Equal("duplicates.sampleCount", SettingsCatalog.Duplicates.SampleCount.Path);
        Assert.True(SettingsCatalog.Scanning.ShowHiddenVolumes.AppliesTo(SettingPlatform.MacOS));
        Assert.False(SettingsCatalog.Scanning.ShowHiddenVolumes.AppliesTo(SettingPlatform.Windows));
        Assert.True(SettingsCatalog.Duplicates.SampleCount.AppliesTo(SettingPlatform.Windows));
    }

    /// <summary>
    /// Matches a camel-case key.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCase();
}
