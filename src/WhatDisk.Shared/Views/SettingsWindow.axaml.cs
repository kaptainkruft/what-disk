using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// The settings window: the sections of the settings file down the left, the chosen section's settings on the right.
/// Changes apply at once, so there is no OK or Cancel. On macOS it closes like any window (the red button, ⌘W or
/// Escape); elsewhere it has a Close button too. Coming back to it re-reads the settings file, so edits made to the
/// file by hand appear.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = new RelayCommand(Close) });
        Activated += (_, _) => (DataContext as SettingsViewModel)?.Reload();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel settings)
        {
            var platform = settings.Settings.Platform;
            CloseButton.IsVisible = !platform.HasApplicationMenu;
            if (platform.HasApplicationMenu)
            {
                KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.W, platform.Shortcuts.CommandKey), Command = new RelayCommand(Close) });
            }
        }
    }

    /// <summary>
    /// Asks before returning every setting to its default, then does.
    /// </summary>
    /// <param name="sender">The Restore Defaults button.</param>
    /// <param name="e">Unused.</param>
    private async void OnResetAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings)
        {
            return;
        }

        var cancel = new QuestionButton("Cancel", "cancel", IsCancel: true);
        var restore = new QuestionButton("Restore Defaults", "restore", IsDefault: true);
        var question = new QuestionWindow(
            "Restore every setting to its default?",
            "Every setting on every page returns to its default. Settings for other platforms in the settings file are kept.",
            OperatingSystem.IsMacOS() ? [cancel, restore] : [restore, cancel]);
        if (await question.ShowAsync(this) == "restore")
        {
            settings.ResetAll();
        }
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">The Close button.</param>
    /// <param name="e">Unused.</param>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
