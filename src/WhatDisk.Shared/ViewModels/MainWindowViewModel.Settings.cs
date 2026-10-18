using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// The app's settings: opening the settings window, and applying settings that change what is already on screen.
/// Other settings are read when they are next needed (the next scan, search or delete).
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Gets the app's settings.
    /// </summary>
    public AppSettings Settings { get; }

    /// <summary>
    /// Opens the settings window, or brings it to the front when it is already open.
    /// </summary>
    [RelayCommand]
    private void OpenSettings() => _dialogs.ShowSettings(new SettingsViewModel(Settings, _shell));

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
    /// Applies a changed setting that affects what is on screen: new size units re-format every size in the tree,
    /// chart, details and volume list.
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
    }
}
