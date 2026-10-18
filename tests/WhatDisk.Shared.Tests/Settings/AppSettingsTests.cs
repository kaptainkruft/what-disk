using System.Text.Json.Nodes;
using WhatDisk.Platform;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Settings;

/// <summary>
/// Tests the settings file: only differences from the defaults are written, platform settings go in their platform's
/// subsection, other platforms' settings and unknown keys are kept, and invalid files and values fall back to the
/// defaults.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    /// <summary>The folder holding the settings file, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-settings-");

    /// <summary>
    /// Gets the settings file's path; its folder does not exist until something is saved.
    /// </summary>
    private string FilePath => Path.Join(_temp.FullName, "WhatDisk", AppSettings.FileName);

    /// <inheritdoc/>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// Without a file every setting has its default, and reading creates no file.
    /// </summary>
    [Fact]
    public void WithoutAFile_EverySettingHasItsDefault()
    {
        var settings = Open();

        Assert.Equal(0, settings.Get(SettingsCatalog.Scanning.Threads));
        Assert.Equal(64, settings.Get(SettingsCatalog.Duplicates.SampleCount));
        Assert.Equal("system", settings.Get(SettingsCatalog.General.Theme));
        Assert.False(settings.IsModified(SettingsCatalog.Scanning.Threads));
        Assert.Null(settings.LoadError);
        Assert.False(File.Exists(FilePath));
    }

    /// <summary>
    /// Only values that differ from the default are written; setting a value back to its default removes it, and an
    /// empty section goes with it.
    /// </summary>
    [Fact]
    public void Set_WritesOnlyDifferencesFromTheDefaults()
    {
        var settings = Open();

        settings.Set(SettingsCatalog.Scanning.Threads, 8);
        settings.Set(SettingsCatalog.Duplicates.SampleCount, 64);
        settings.Set(SettingsCatalog.General.Theme, "dark");

        Assert.Equal("""{"scanning":{"threads":8},"general":{"theme":"dark"}}""", Compact(File.ReadAllText(FilePath)));
        Assert.True(settings.IsModified(SettingsCatalog.Scanning.Threads));
        Assert.False(settings.IsModified(SettingsCatalog.Duplicates.SampleCount));

        settings.Set(SettingsCatalog.Scanning.Threads, 0);

        Assert.Equal("""{"general":{"theme":"dark"}}""", Compact(File.ReadAllText(FilePath)));
        Assert.Equal(0, Open().Get(SettingsCatalog.Scanning.Threads));
    }

    /// <summary>
    /// A setting for one platform is written to its section's subsection for that platform.
    /// </summary>
    /// <param name="platformName">The platform.</param>
    /// <param name="expected">The file's contents after the change.</param>
    [Theory]
    [InlineData("macOS", """{"scanning":{"macos":{"showHiddenVolumes":true}}}""")]
    [InlineData("Windows", """{"deleting":{"windows":{"warnWhenNotRecyclable":false}}}""")]
    public void PlatformSettings_GoInTheirPlatformsSubsection(string platformName, string expected)
    {
        var settings = Open(TestPlatforms.Named(platformName));

        if (platformName == "macOS")
        {
            settings.Set(SettingsCatalog.Scanning.ShowHiddenVolumes, true);
        }
        else
        {
            settings.Set(SettingsCatalog.Deleting.WarnWhenNotRecyclable, false);
        }

        Assert.Equal(expected, Compact(File.ReadAllText(FilePath)));
    }

    /// <summary>
    /// Settings of other platforms, unknown keys and other values of a section are kept when the file is saved;
    /// comments and trailing commas are accepted when it is read.
    /// </summary>
    [Fact]
    public void Saving_KeepsOtherPlatformsAndUnknownKeys()
    {
        Write("""
            {
              // Written by hand.
              "scanning": { "threads": 6, "windows": { "future": 1 }, },
              "deleting": { "windows": { "warnWhenNotRecyclable": false } },
              "plugins": { "enabled": true },
            }
            """);
        var settings = Open(TestPlatforms.MacOS);
        Assert.Null(settings.LoadError);
        Assert.Equal(6, settings.Get(SettingsCatalog.Scanning.Threads));

        settings.Set(SettingsCatalog.Scanning.ShowHiddenVolumes, true);
        settings.Reset(SettingsCatalog.Scanning.Threads);

        Assert.Equal(
            """{"scanning":{"windows":{"future":1},"macos":{"showHiddenVolumes":true}},"deleting":{"windows":{"warnWhenNotRecyclable":false}},"plugins":{"enabled":true}}""",
            Compact(File.ReadAllText(FilePath)));
    }

    /// <summary>
    /// Some defaults follow the platform: sizes are decimal on macOS and binary on Windows, so choosing the platform's
    /// own units writes nothing.
    /// </summary>
    [Fact]
    public void Defaults_CanFollowThePlatform()
    {
        Assert.Equal("decimal", Open(TestPlatforms.MacOS).Get(SettingsCatalog.General.SizeUnits));
        var windows = Open(TestPlatforms.Windows);
        Assert.Equal("binary", windows.Get(SettingsCatalog.General.SizeUnits));

        windows.Set(SettingsCatalog.General.SizeUnits, "binary");

        Assert.False(File.Exists(FilePath) && Compact(File.ReadAllText(FilePath)) != "{}");
    }

    /// <summary>
    /// Values of the wrong kind count as the default; numbers out of range are brought into range and fractions are
    /// rounded; choices ignore case.
    /// </summary>
    [Fact]
    public void InvalidValues_FallBackToTheDefault()
    {
        Write("""
            {
              "scanning": { "threads": "many", "networkThreads": 500, "refreshMilliseconds": 7.6, "includeNetworkVolumes": "yes" },
              "general": { "theme": "DARK", "sizeUnits": "imperial" },
              "duplicates": []
            }
            """);

        var settings = Open();

        Assert.Equal(0, settings.Get(SettingsCatalog.Scanning.Threads));
        Assert.Equal(128, settings.Get(SettingsCatalog.Scanning.NetworkThreads));
        Assert.Equal(50, settings.Get(SettingsCatalog.Scanning.RefreshMilliseconds));
        Assert.False(settings.Get(SettingsCatalog.Scanning.IncludeNetworkVolumes));
        Assert.Equal("dark", settings.Get(SettingsCatalog.General.Theme));
        Assert.Equal("decimal", settings.Get(SettingsCatalog.General.SizeUnits));
        Assert.Equal(64, settings.Get(SettingsCatalog.Duplicates.SampleCount));
        Assert.False(settings.IsModified(SettingsCatalog.Scanning.Threads));

        settings.Set(SettingsCatalog.Duplicates.SampleCount, 100);
        Assert.Equal(100, settings.Get(SettingsCatalog.Duplicates.SampleCount));
    }

    /// <summary>
    /// A file that is not valid JSON leaves every setting at its default and is reported; before it is first
    /// overwritten it is kept as <c>settings.invalid.json</c>.
    /// </summary>
    [Fact]
    public void InvalidFile_IsReportedAndKeptAside()
    {
        Write("{ \"scanning\": { \"threads\": 8 ");

        var settings = Open();

        Assert.NotNull(settings.LoadError);
        Assert.Equal(0, settings.Get(SettingsCatalog.Scanning.Threads));

        settings.Set(SettingsCatalog.Scanning.Threads, 4);

        Assert.Equal("{ \"scanning\": { \"threads\": 8 ", File.ReadAllText(Path.ChangeExtension(FilePath, ".invalid.json")));
        Assert.Equal("""{"scanning":{"threads":4}}""", Compact(File.ReadAllText(FilePath)));
        Assert.Null(settings.LoadError);
    }

    /// <summary>
    /// Changed is raised for a setting only when its value changes, including by Reset; Reload raises it for values
    /// edited in the file meanwhile.
    /// </summary>
    [Fact]
    public void Changed_IsRaisedWhenValuesChange()
    {
        var settings = Open();
        var changed = new List<string>();
        settings.Changed += (_, setting) => changed.Add(setting.Path);

        settings.Set(SettingsCatalog.Scanning.Threads, 8);
        settings.Set(SettingsCatalog.Scanning.Threads, 8);
        settings.Set(SettingsCatalog.Duplicates.Readers, 0);
        settings.Reset(SettingsCatalog.Scanning.Threads);
        Assert.Equal(["scanning.threads", "scanning.threads"], changed);

        changed.Clear();
        Write("""{ "duplicates": { "readers": 1 }, "general": { "theme": "system" } }""");
        settings.Reload();
        Assert.Equal(["duplicates.readers"], changed);
        Assert.Equal(1, settings.Get(SettingsCatalog.Duplicates.Readers));
    }

    /// <summary>
    /// Restoring the defaults removes every setting of this platform, but keeps other platforms' settings and keys
    /// this version does not know.
    /// </summary>
    [Fact]
    public void ResetAll_KeepsOtherPlatformsAndUnknownKeys()
    {
        Write("""{ "scanning": { "threads": 2, "macos": { "showHiddenVolumes": true } }, "deleting": { "windows": { "warnWhenNotRecyclable": false } }, "x": 1 }""");
        var settings = Open(TestPlatforms.MacOS);

        settings.ResetAll();

        Assert.Equal("""{"deleting":{"windows":{"warnWhenNotRecyclable":false}},"x":1}""", Compact(File.ReadAllText(FilePath)));
    }

    /// <summary>
    /// Settings without a file are kept in memory, and EnsureFile creates the file on request.
    /// </summary>
    [Fact]
    public void InMemorySettings_AndEnsureFile()
    {
        var memory = new AppSettings(TestPlatforms.MacOS, null);
        memory.Set(SettingsCatalog.Scanning.Threads, 3);
        Assert.Equal(3, memory.Get(SettingsCatalog.Scanning.Threads));
        Assert.Equal("""{"scanning":{"threads":3}}""", Compact(memory.ToJson()));

        var settings = Open();
        settings.EnsureFile();
        Assert.Equal("{}", Compact(File.ReadAllText(FilePath)));
    }

    /// <summary>
    /// The default location is the platform's place for application settings.
    /// </summary>
    [Fact]
    public void DefaultFilePath_IsInTheUsersApplicationData()
    {
        var path = AppSettings.DefaultFilePath();

        Assert.EndsWith(Path.Join("WhatDisk", "settings.json"), path, StringComparison.Ordinal);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains(Path.Join("Library", "Application Support"), path, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Removes the indentation from JSON, for comparing.
    /// </summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The JSON without whitespace between tokens.</returns>
    private static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    /// <summary>
    /// Opens the settings file.
    /// </summary>
    /// <param name="platform">The platform, or <see langword="null"/> for macOS.</param>
    /// <returns>The settings.</returns>
    private AppSettings Open(PlatformCapabilities? platform = null) => new(platform ?? TestPlatforms.MacOS, FilePath);

    /// <summary>
    /// Writes the settings file, as if by hand.
    /// </summary>
    /// <param name="json">The contents.</param>
    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json);
    }
}
