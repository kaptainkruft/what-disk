using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Scanning;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// The app's settings: opening the settings window, and applying settings that change what is already on screen.
/// Other settings are read when they are next needed (the next scan, search or delete).
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Estimates the potential duplicate space; replaced when its minimum length changes.</summary>
    private PotentialDuplicateAggregator _potentialDuplicateTotals;

    /// <summary>
    /// Gets the app's settings.
    /// </summary>
    public AppSettings Settings { get; }

    /// <summary>
    /// Gets the Potential duplicate space pane.
    /// </summary>
    public PotentialDuplicatesViewModel PotentialDuplicates { get; }

    /// <summary>
    /// Gets a value indicating whether a question is being asked over the window; the commands wait meanwhile, so
    /// that menu shortcuts cannot act behind it.
    /// </summary>
    public bool IsDialogOpen => _dialogs.IsShowing;

    /// <summary>
    /// Opens the settings in a tab, or shows that tab when it is already open.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings()
    {
        if (Tabs.OfType<SettingsViewModel>().FirstOrDefault() is { } open)
        {
            SelectedTab = open;
        }
        else
        {
            OpenTab(new SettingsViewModel(Settings, _shell, _dialogs));
        }
    }

    /// <summary>
    /// Determines whether Settings is enabled: not while a question is being asked.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanOpenSettings() => !IsDialogOpen;

    /// <summary>
    /// Re-evaluates the commands when a question starts or stops being asked over the window.
    /// </summary>
    /// <param name="sender">The dialog service.</param>
    /// <param name="e">Unused.</param>
    private void OnDialogShowingChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDialogOpen));
        NotifyItemCommands();
        StartScanCommand.NotifyCanExecuteChanged();
        ScanVolumeCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
        OpenSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gets the size units the settings choose.
    /// </summary>
    /// <returns>The units.</returns>
    private ByteUnitConvention SizeUnits() =>
        Settings.Get(SettingsCatalog.General.SizeUnits) == "binary" ? ByteUnitConvention.PowersOf1024 : ByteUnitConvention.PowersOf1000;

    /// <summary>
    /// Gets the duplicate search's options from the settings.
    /// </summary>
    /// <returns>The options.</returns>
    private DuplicateFinderOptions DuplicateOptions() => new()
    {
        FullHashLimit = Settings.Get(SettingsCatalog.Duplicates.HashUpToMiB) * 1024L * 1024,
        SampleCount = Settings.Get(SettingsCatalog.Duplicates.SampleCount),
        SampleLength = Settings.Get(SettingsCatalog.Duplicates.SampleSizeKiB) * 1024,
        Parallelism = Settings.Get(SettingsCatalog.Duplicates.Readers),
    };

    /// <summary>
    /// Describes a whole number of mebibytes, such as "1 MiB".
    /// </summary>
    /// <param name="bytes">A multiple of 1 MiB.</param>
    /// <returns>The description.</returns>
    private static string DescribeMebibytes(long bytes) => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{bytes / (1024 * 1024):N0} MiB");

    /// <summary>
    /// Creates the potential duplicate estimator for the minimum length the settings choose.
    /// </summary>
    /// <returns>The estimator.</returns>
    private PotentialDuplicateAggregator CreatePotentialDuplicateAggregator() =>
        new(Settings.Get(SettingsCatalog.Duplicates.EstimateOverMiB) * 1024L * 1024);

    /// <summary>
    /// Forgets the cached totals (file types and potential duplicates) of a directory and its ancestors, after its
    /// contents changed.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private void InvalidateTotals(DirectoryNode directory)
    {
        _fileTypeTotals.Invalidate(directory);
        _potentialDuplicateTotals.Invalidate(directory);
    }

    /// <summary>
    /// Applies a changed setting that affects what is on screen: new size units re-format every size in the tree,
    /// chart, details, potential duplicate space and volume list, and a new minimum length re-estimates the potential
    /// duplicate space.
    /// </summary>
    /// <param name="sender">The settings.</param>
    /// <param name="setting">The setting that changed.</param>
    private void OnSettingChanged(object? sender, SettingDefinition setting)
    {
        if (setting == SettingsCatalog.General.SizeUnits && SizeUnits() != _sizes.Convention)
        {
            _sizes.Convention = SizeUnits();
            Tree.Reformat();
            _chartedScope = default;
            Refresh();
            if (Volumes.Count > 0)
            {
                LoadVolumesCommand.Execute(null);
            }
        }
        else if (setting == SettingsCatalog.Duplicates.EstimateOverMiB)
        {
            _potentialDuplicateTotals = CreatePotentialDuplicateAggregator();
            _chartedScope = default;
            Refresh();
        }
    }
}
