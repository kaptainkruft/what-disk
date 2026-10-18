using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using WhatDisk.Duplicates;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.Settings;

/// <summary>
/// Tests that the main window follows the settings, against real files in a temporary folder: scan options, the
/// delete confirmation threshold, size units (at once), the duplicate search and its automatic verification, and the
/// theme.
/// </summary>
public sealed class SettingsAppliedTests : IDisposable
{
    /// <summary>The scanned folder, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-settings-applied-");

    /// <summary>The scanned folder's path with symbolic links resolved.</summary>
    private readonly string _base;

    /// <summary>The volumes: the whole file system.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>Answers the questions and records the windows shown.</summary>
    private readonly ScriptedDialogService _dialogs = new() { Confirm = false };

    /// <summary>The settings, kept in memory.</summary>
    private readonly AppSettings _settings = new(TestPlatforms.MacOS, null);

    /// <summary>The view model, once created.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsAppliedTests"/> class with two identical 2,000-byte files
    /// and one different file.
    /// </summary>
    public SettingsAppliedTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", Path.GetPathRoot(_base)!);
        var content = new byte[2000];
        new Random(1).NextBytes(content);
        File.WriteAllBytes(Path.Join(_base, "a.bin"), content);
        File.WriteAllBytes(Path.Join(_base, "b.bin"), content);
        File.WriteAllBytes(Path.Join(_base, "c.txt"), new byte[10]);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _viewModel?.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// A scan uses the number of threads and the network rule the settings choose.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Scan_UsesTheScanningSettings()
    {
        _settings.Set(SettingsCatalog.Scanning.Threads, 3);
        _settings.Set(SettingsCatalog.Scanning.IncludeNetworkVolumes, true);

        var viewModel = await ScanAsync();

        Assert.Equal(3, viewModel.Session!.Request.Options.MaxDegreeOfParallelism);
        Assert.True(viewModel.Session.Request.Options.IncludeNetworkVolumes);
    }

    /// <summary>
    /// The delete confirmation follows the settings: with a threshold of 0 files, deleting one small file asks.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Delete_AsksAboveTheThresholdTheSettingsChoose()
    {
        var viewModel = await ScanAsync();
        Assert.Equal((10, 10_000_000L), (viewModel.DeleteConfirmationFiles, viewModel.DeleteConfirmationBytes));
        _settings.Set(SettingsCatalog.Deleting.ConfirmAboveFiles, 0);
        _settings.Set(SettingsCatalog.Deleting.ConfirmAboveMegabytes, 2);
        _settings.Set(SettingsCatalog.General.SizeUnits, "binary");
        Assert.Equal((0, 2L * 1024 * 1024), (viewModel.DeleteConfirmationFiles, viewModel.DeleteConfirmationBytes));
        var row = viewModel.Tree.FindVisible(((DirectoryNode)viewModel.Tree.RootRows[0].Node).Children.Single(static child => child.Name == "c.txt"))!;
        viewModel.Selection.Clear();
        viewModel.Selection.Select(viewModel.Tree.IndexOf(row));

        await viewModel.DeletePermanentlyCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Confirmations);
        Assert.True(File.Exists(Path.Join(_base, "c.txt")));
    }

    /// <summary>
    /// Changing the size units re-formats the sizes already shown, at once.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task SizeUnits_ApplyAtOnce()
    {
        var viewModel = await ScanAsync();
        var file = viewModel.Tree.Rows.Single(static row => row.Name == "a.bin");
        Assert.Equal("2.00 KB", file.SizeText);

        _settings.Set(SettingsCatalog.General.SizeUnits, "binary");

        Assert.Equal("1.95 KB", file.SizeText);
    }

    /// <summary>
    /// The duplicate search uses the settings: with no hashing limit every file is sampled, and with automatic
    /// verification the sampled sets are hashed as soon as the results appear.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicates_UsesTheDuplicateSettings()
    {
        _settings.Set(SettingsCatalog.Duplicates.HashUpToMiB, 0);
        _settings.Set(SettingsCatalog.Duplicates.SampleCount, 8);
        _settings.Set(SettingsCatalog.Duplicates.SampleSizeKiB, 1);
        var viewModel = await ScanAsync();

        await viewModel.FindDuplicatesInScanCommand.ExecuteAsync(null);

        var sampled = Assert.Single(viewModel.DuplicateTabs());
        Assert.Equal(DuplicateComparison.Samples, Assert.Single(sampled.Sets).Comparison);
        Assert.Contains("compared by 8 samples of 1 KiB", sampled.Details, StringComparison.Ordinal);

        _settings.Set(SettingsCatalog.Duplicates.VerifyAutomatically, true);
        await viewModel.FindDuplicatesInScanCommand.ExecuteAsync(null);

        var verified = viewModel.DuplicateTabs()[1];
        Assert.Equal(DuplicateComparison.Hash, Assert.Single(verified.Sets).Comparison);
        Assert.Equal("Every set is confirmed by hash.", verified.VerificationSummary);
    }

    /// <summary>
    /// Settings opens a tab with these settings, and shows it.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task OpenSettings_ShowsTheSettingsTab()
    {
        var viewModel = await ScanAsync();

        viewModel.OpenSettingsCommand.Execute(null);

        var settings = Assert.Single(viewModel.Tabs.OfType<SettingsViewModel>());
        Assert.Same(_settings, settings.Settings);
        Assert.Same(settings, viewModel.SelectedTab);
    }

    /// <summary>
    /// The theme setting maps to Avalonia's theme variants.
    /// </summary>
    [Fact]
    public void Theme_MapsToThemeVariants()
    {
        Assert.Equal(ThemeVariant.Light, App.ThemeFor("light"));
        Assert.Equal(ThemeVariant.Dark, App.ThemeFor("dark"));
        Assert.Equal(ThemeVariant.Default, App.ThemeFor("system"));
    }

    /// <summary>
    /// Scans the temporary folder.
    /// </summary>
    /// <returns>The view model, with the tree active.</returns>
    private async Task<MainWindowViewModel> ScanAsync()
    {
        _viewModel = new MainWindowViewModel(
            new DiskScanner(new ManagedFileSystemEnumerator(), _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            TestPlatforms.MacOS,
            TimeProvider.System,
            shell: new RecordingShellService(),
            dialogs: _dialogs,
            settings: _settings)
        {
            PathText = _temp.FullName,
            IsTreeActive = true,
        };
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        Assert.Equal(ScanStatus.Completed, _viewModel.Session?.Status);
        return _viewModel;
    }
}
