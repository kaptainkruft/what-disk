using WhatDisk.Platform;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.Settings;

/// <summary>
/// Tests the settings window's view model: pages follow the sections of the file, platform settings appear among the
/// others for their platform only, and editing, resetting and reloading keep the items and the file in step.
/// </summary>
public sealed class SettingsViewModelTests : IDisposable
{
    /// <summary>The folder holding the settings file, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-settings-page-");

    /// <summary>Records the files opened.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>
    /// Gets the settings file's path.
    /// </summary>
    private string FilePath => Path.Join(_temp.FullName, AppSettings.FileName);

    /// <inheritdoc/>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// There is one page per section, in the file's order, each with the section's settings for this platform only;
    /// a platform's own setting sits among the others, marked with an asterisk.
    /// </summary>
    /// <param name="platformName">The platform.</param>
    /// <param name="platformPath">The path of the platform's own setting.</param>
    /// <param name="otherPath">The path of the other platform's setting, which is not shown.</param>
    [Theory]
    [InlineData("macOS", "scanning.macos.showHiddenVolumes", "deleting.windows.warnWhenNotRecyclable")]
    [InlineData("Windows", "deleting.windows.warnWhenNotRecyclable", "scanning.macos.showHiddenVolumes")]
    public void Pages_FollowTheSectionsAndThePlatform(string platformName, string platformPath, string otherPath)
    {
        using var page = new SettingsViewModel(new AppSettings(TestPlatforms.Named(platformName), FilePath), _shell);

        Assert.Equal(["General", "Scanning", "Duplicates", "Deleting"], page.Pages.Select(static item => item.Title));
        var items = page.Pages.SelectMany(static item => item.Items).ToList();
        Assert.Contains(items, item => item.Path == platformPath && item.IsPlatformSpecific && item.PlatformMark == "*");
        Assert.DoesNotContain(items, item => item.Path == otherPath);
        Assert.All(items.Where(static item => !item.IsPlatformSpecific), static item => Assert.Equal(string.Empty, item.PlatformMark));
        Assert.Contains($"{platformName} only", page.PlatformNote, StringComparison.Ordinal);
        Assert.Same(page.Pages[0], page.SelectedPage);
        Assert.Equal($"Saved in {FilePath}", page.FileText);
    }

    /// <summary>
    /// Editing an item saves the value and marks the item modified; Reset returns it to the default; a number out of
    /// range is brought into range.
    /// </summary>
    [Fact]
    public void Items_EditAndResetTheSettings()
    {
        var settings = new AppSettings(TestPlatforms.MacOS, FilePath);
        using var page = new SettingsViewModel(settings, _shell);
        var threads = Item<IntSettingViewModel>(page, "scanning.threads");
        var hidden = Item<BoolSettingViewModel>(page, "scanning.macos.showHiddenVolumes");
        var theme = Item<ChoiceSettingViewModel>(page, "general.theme");

        threads.Value = 500;
        hidden.Value = true;
        theme.SelectedChoice = theme.Choices.Single(static choice => choice.Value == "dark");

        Assert.Equal(64, threads.Value);
        Assert.True(threads.IsModified);
        Assert.Equal(64, settings.Get(SettingsCatalog.Scanning.Threads));
        Assert.Contains("\"showHiddenVolumes\": true", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.Equal("dark", settings.Get(SettingsCatalog.General.Theme));
        Assert.Equal("0–64 threads; 0 is Automatic", threads.RangeText);

        threads.ResetCommand.Execute(null);

        Assert.Equal(0, threads.Value);
        Assert.False(threads.IsModified);
        Assert.Equal(0, settings.Get(SettingsCatalog.Scanning.Threads));
    }

    /// <summary>
    /// Items follow the file when it is edited by hand and reloaded, and Restore Defaults resets every item.
    /// </summary>
    [Fact]
    public void Items_FollowReloadAndRestoreDefaults()
    {
        var settings = new AppSettings(TestPlatforms.MacOS, FilePath);
        using var page = new SettingsViewModel(settings, _shell);
        var readers = Item<IntSettingViewModel>(page, "duplicates.readers");
        var changes = new List<string?>();
        readers.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        File.WriteAllText(FilePath, """{ "duplicates": { "readers": 1 } }""");
        page.Reload();

        Assert.Equal(1, readers.Value);
        Assert.True(readers.IsModified);
        Assert.Contains(nameof(IntSettingViewModel.Value), changes);

        page.ResetAll();

        Assert.Equal(0, readers.Value);
        Assert.False(readers.IsModified);
    }

    /// <summary>
    /// Open Settings File creates the file when needed and opens it; problems reading the file are shown.
    /// </summary>
    [Fact]
    public void OpenFile_CreatesAndOpensTheFile()
    {
        File.WriteAllText(FilePath, "not json");
        using var page = new SettingsViewModel(new AppSettings(TestPlatforms.MacOS, FilePath), _shell);
        Assert.StartsWith("The settings file could not be read", page.Message, StringComparison.Ordinal);
        File.Delete(FilePath);

        page.OpenFileCommand.Execute(null);

        Assert.True(File.Exists(FilePath));
        Assert.Equal([$"open: {FilePath}"], _shell.Calls);
        _shell.Capabilities = ShellCapabilities.None;
        using var withoutShell = new SettingsViewModel(new AppSettings(TestPlatforms.MacOS, FilePath), _shell);
        Assert.False(withoutShell.OpenFileCommand.CanExecute(null));
    }

    /// <summary>
    /// Finds an item by its path in the settings file.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="page">The settings window.</param>
    /// <param name="path">The setting's path.</param>
    /// <returns>The item.</returns>
    private static T Item<T>(SettingsViewModel page, string path)
        where T : SettingItemViewModel =>
        Assert.IsType<T>(page.Pages.SelectMany(static item => item.Items).Single(item => item.Path == path));
}
