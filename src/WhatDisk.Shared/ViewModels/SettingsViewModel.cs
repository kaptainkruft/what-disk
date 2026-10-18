using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Platform;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// The settings window: one page per section of the settings file, in the file's order, each listing the section's
/// settings for this platform. Settings of this platform only sit among the others, marked with an asterisk. Changes
/// apply and are saved at once.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Opens the settings file in the default editor.</summary>
    private readonly IShellService _shell;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="shell">Opens the settings file.</param>
    public SettingsViewModel(AppSettings settings, IShellService shell)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shell);
        Settings = settings;
        _shell = shell;
        Pages =
        [
            .. SettingsCatalog.Sections
                .Select(section => new SettingsPageViewModel(
                    section,
                    [.. section.Settings.Where(setting => setting.AppliesTo(settings.CurrentPlatform)).Select(setting => SettingItemViewModel.Create(setting, settings))]))
                .Where(static page => page.Items.Count > 0),
        ];
        SelectedPage = Pages[0];
        Settings.Changed += OnSettingChanged;
        UpdateMessage();
    }

    /// <summary>
    /// Gets the settings.
    /// </summary>
    public AppSettings Settings { get; }

    /// <summary>
    /// Gets the pages, one per section of the settings file.
    /// </summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>
    /// Gets or sets the page shown.
    /// </summary>
    [ObservableProperty]
    public partial SettingsPageViewModel SelectedPage { get; set; }

    /// <summary>
    /// Gets where the settings are saved, for the footer.
    /// </summary>
    public string FileText => Settings.FilePath is { } path ? $"Saved in {path}" : "Settings are not saved.";

    /// <summary>
    /// Gets the footnote explaining the asterisk, such as "* macOS only".
    /// </summary>
    public string PlatformNote => SettingDefinition.KeyOf(Settings.CurrentPlatform) is { } key
        ? $"* {Settings.Platform.PlatformName} only, stored in the section's “{key}” part of the settings file."
        : string.Empty;

    /// <summary>
    /// Gets a problem reading or saving the settings file, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// Reads the settings file again, so that changes made to it by hand appear; called when the window is shown or
    /// brought to the front.
    /// </summary>
    public void Reload()
    {
        Settings.Reload();
        UpdateMessage();
    }

    /// <summary>
    /// Returns every setting of this platform to its default.
    /// </summary>
    public void ResetAll() => Settings.ResetAll();

    /// <inheritdoc/>
    public void Dispose()
    {
        Settings.Changed -= OnSettingChanged;
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }

    /// <summary>
    /// Opens the settings file in the default editor for JSON files, creating it first when nothing has been changed
    /// yet.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile()
    {
        if (Settings.FilePath is not { } path)
        {
            return;
        }

        try
        {
            Settings.EnsureFile();
            _shell.Open([path]);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            Message = $"Could not open the settings file: {exception.Message}";
        }
    }

    /// <summary>
    /// Determines whether the settings file can be opened.
    /// </summary>
    /// <returns><see langword="true"/> when settings are saved to a file and the platform can open files.</returns>
    private bool CanOpenFile() => Settings.FilePath is not null && (_shell.Capabilities & ShellCapabilities.Open) != 0;

    /// <summary>
    /// Shows a changed setting's new value, and any problem saving it.
    /// </summary>
    /// <param name="sender">The settings.</param>
    /// <param name="setting">The setting that changed.</param>
    private void OnSettingChanged(object? sender, SettingDefinition setting) => UpdateMessage();

    /// <summary>
    /// Shows the latest problem reading or saving the settings file.
    /// </summary>
    private void UpdateMessage() => Message = Settings.SaveError ?? Settings.LoadError;
}

/// <summary>
/// One page of the settings window: a section of the settings file.
/// </summary>
public sealed class SettingsPageViewModel : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPageViewModel"/> class.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="items">The section's settings for this platform.</param>
    public SettingsPageViewModel(SettingSection section, IReadOnlyList<SettingItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(section);
        Key = section.Key;
        Title = section.Title;
        Description = section.Description;
        Items = items;
    }

    /// <summary>
    /// Gets the section's key in the settings file.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the page's title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets what the page covers.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the page's settings.
    /// </summary>
    public IReadOnlyList<SettingItemViewModel> Items { get; }

    /// <summary>
    /// Gets a value indicating whether any setting on the page is for this platform only.
    /// </summary>
    public bool HasPlatformSettings => Items.Any(static item => item.IsPlatformSpecific);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}

/// <summary>
/// One setting in the settings window: its label, description and path in the settings file, an editor for its value,
/// and whether it differs from the default. Values are read from and written to the settings as they change.
/// </summary>
public abstract partial class SettingItemViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingItemViewModel"/> class.
    /// </summary>
    /// <param name="definition">The setting.</param>
    /// <param name="settings">The settings.</param>
    protected SettingItemViewModel(SettingDefinition definition, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(settings);
        Definition = definition;
        Settings = settings;
        IsModified = settings.IsModified(definition);
        settings.Changed += OnSettingChanged;
    }

    /// <summary>
    /// Gets the setting.
    /// </summary>
    public SettingDefinition Definition { get; }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label => Definition.Label;

    /// <summary>
    /// Gets what the setting does.
    /// </summary>
    public string Description => Definition.Description;

    /// <summary>
    /// Gets the setting's path in the settings file, such as <c>scanning.threads</c>.
    /// </summary>
    public string Path => Definition.Path;

    /// <summary>
    /// Gets a value indicating whether the setting is for this platform only.
    /// </summary>
    public bool IsPlatformSpecific => Definition.PlatformName is not null;

    /// <summary>
    /// Gets the mark after a platform's own setting's label (an asterisk), or an empty string.
    /// </summary>
    public string PlatformMark => IsPlatformSpecific ? "*" : string.Empty;

    /// <summary>
    /// Gets the explanation of the asterisk on a platform's own setting, or <see langword="null"/>.
    /// </summary>
    public string? PlatformTip => Definition.PlatformName is { } name
        ? $"{name} only. Stored in the “{Definition.PlatformKey}” part of the “{Definition.Section}” section of the settings file."
        : null;

    /// <summary>
    /// Gets a value indicating whether the setting differs from its default.
    /// </summary>
    [ObservableProperty]
    public partial bool IsModified { get; private set; }

    /// <summary>
    /// Gets the settings.
    /// </summary>
    protected AppSettings Settings { get; }

    /// <summary>
    /// Creates the item for a setting, with the editor its type needs.
    /// </summary>
    /// <param name="definition">The setting.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>The item.</returns>
    public static SettingItemViewModel Create(SettingDefinition definition, AppSettings settings) => definition switch
    {
        BoolSetting toggle => new BoolSettingViewModel(toggle, settings),
        IntSetting number => new IntSettingViewModel(number, settings),
        ChoiceSetting choice => new ChoiceSettingViewModel(choice, settings),
        _ => throw new NotSupportedException($"No editor for {definition.GetType().Name}."),
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        Settings.Changed -= OnSettingChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Raises property changes for the value, after it changed in the settings.
    /// </summary>
    protected abstract void OnValueChanged();

    /// <summary>
    /// Returns the setting to its default.
    /// </summary>
    [RelayCommand]
    private void Reset() => Settings.Reset(Definition);

    /// <summary>
    /// Shows the new value when this setting changed, whether here, by Reset, or by editing the file.
    /// </summary>
    /// <param name="sender">The settings.</param>
    /// <param name="setting">The setting that changed.</param>
    private void OnSettingChanged(object? sender, SettingDefinition setting)
    {
        if (setting == Definition)
        {
            OnValueChanged();
            IsModified = Settings.IsModified(Definition);
        }
    }
}

/// <summary>
/// An on/off setting, edited with a switch.
/// </summary>
public sealed class BoolSettingViewModel : SettingItemViewModel
{
    /// <summary>The setting.</summary>
    private readonly BoolSetting _setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoolSettingViewModel"/> class.
    /// </summary>
    /// <param name="definition">The setting.</param>
    /// <param name="settings">The settings.</param>
    public BoolSettingViewModel(BoolSetting definition, AppSettings settings)
        : base(definition, settings) => _setting = definition;

    /// <summary>
    /// Gets or sets the value; setting it saves it.
    /// </summary>
    public bool Value
    {
        get => Settings.Get(_setting);
        set => Settings.Set(_setting, value);
    }

    /// <inheritdoc/>
    protected override void OnValueChanged() => OnPropertyChanged(nameof(Value));
}

/// <summary>
/// A whole-number setting, edited with a number box and its unit.
/// </summary>
public sealed class IntSettingViewModel : SettingItemViewModel
{
    /// <summary>The setting.</summary>
    private readonly IntSetting _setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntSettingViewModel"/> class.
    /// </summary>
    /// <param name="definition">The setting.</param>
    /// <param name="settings">The settings.</param>
    public IntSettingViewModel(IntSetting definition, AppSettings settings)
        : base(definition, settings) => _setting = definition;

    /// <summary>
    /// Gets or sets the value, as the number box edits it; setting it saves it, brought into range. Clearing the box
    /// changes nothing.
    /// </summary>
    public decimal? Value
    {
        get => Settings.Get(_setting);
        set
        {
            if (value is { } number)
            {
                Settings.Set(_setting, (int)Math.Clamp(Math.Round(number), _setting.Minimum, _setting.Maximum));
            }

            // Show the value as saved, in case it was brought into range or the box was cleared.
            OnPropertyChanged(nameof(Value));
        }
    }

    /// <summary>
    /// Gets the smallest value allowed.
    /// </summary>
    public decimal Minimum => _setting.Minimum;

    /// <summary>
    /// Gets the largest value allowed.
    /// </summary>
    public decimal Maximum => _setting.Maximum;

    /// <summary>
    /// Gets the unit shown after the box, such as "MiB", or an empty string.
    /// </summary>
    public string Unit => _setting.Unit ?? string.Empty;

    /// <summary>
    /// Gets the range, and what zero means where it is special, such as "0–64 threads; 0 is Automatic".
    /// </summary>
    public string RangeText => _setting.ZeroMeans is { } zero
        ? string.Create(CultureInfo.CurrentCulture, $"{_setting.DescribeRange()}; 0 is {zero}")
        : _setting.DescribeRange();

    /// <inheritdoc/>
    protected override void OnValueChanged() => OnPropertyChanged(nameof(Value));
}

/// <summary>
/// A setting with a fixed list of values, edited with a drop-down list.
/// </summary>
public sealed class ChoiceSettingViewModel : SettingItemViewModel
{
    /// <summary>The setting.</summary>
    private readonly ChoiceSetting _setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChoiceSettingViewModel"/> class.
    /// </summary>
    /// <param name="definition">The setting.</param>
    /// <param name="settings">The settings.</param>
    public ChoiceSettingViewModel(ChoiceSetting definition, AppSettings settings)
        : base(definition, settings) => _setting = definition;

    /// <summary>
    /// Gets the values to choose from.
    /// </summary>
    public IReadOnlyList<SettingChoice> Choices => _setting.Choices;

    /// <summary>
    /// Gets or sets the chosen value; setting it saves it.
    /// </summary>
    public SettingChoice? SelectedChoice
    {
        get => _setting.Choices.FirstOrDefault(choice => choice.Value == Settings.Get(_setting));
        set
        {
            if (value is not null)
            {
                Settings.Set(_setting, value.Value);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnValueChanged() => OnPropertyChanged(nameof(SelectedChoice));
}
