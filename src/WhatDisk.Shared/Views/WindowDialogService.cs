using Avalonia.Controls;
using WhatDisk.FileOperations;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// Asks questions with modal windows over the main window, and shows results and the settings (one window at most) in
/// windows the main window owns.
/// </summary>
/// <param name="owner">Gets the window that owns the questions.</param>
public sealed class WindowDialogService(Func<Window?> owner) : IDialogService
{
    /// <summary>The settings window, while it is open.</summary>
    private SettingsWindow? _settingsWindow;

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (owner() is not { IsVisible: true } window)
        {
            return false;
        }

        // A destructive action is never the default, so Return alone cannot destroy anything.
        var cancel = new QuestionButton("Cancel", "cancel", IsDefault: request.IsDestructive, IsCancel: true);
        var confirm = new QuestionButton(request.ConfirmText, "confirm", IsDefault: !request.IsDestructive, IsDestructive: request.IsDestructive);

        // macOS puts the confirming button last (rightmost); Windows puts it first, before Cancel.
        var question = new QuestionWindow(request.Title, request.Message, OperatingSystem.IsMacOS() ? [cancel, confirm] : [confirm, cancel]);
        return await question.ShowAsync(window) == "confirm";
    }

    /// <inheritdoc/>
    public async Task<ConflictDecision> ResolveConflictAsync(TransferConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (owner() is not { IsVisible: true } window)
        {
            return new ConflictDecision(ConflictResolution.Skip);
        }

        var name = Path.GetFileName(conflict.ExistingPath);
        var parent = Path.GetDirectoryName(conflict.ExistingPath) ?? conflict.ExistingPath;
        var folder = Path.GetFileName(parent) is { Length: > 0 } folderName ? folderName : parent;
        var kind = conflict.IsDirectory ? "folder" : "item";
        var question = new QuestionWindow(
            $"An {kind} named “{name}” already exists in “{folder}”.",
            "Replace it, keep both (the new one gets a numbered name), skip this item, or stop?",
            [
                new QuestionButton("Stop", nameof(ConflictResolution.Stop), IsCancel: true),
                new QuestionButton("Skip", nameof(ConflictResolution.Skip)),
                new QuestionButton("Keep Both", nameof(ConflictResolution.KeepBoth), IsDefault: true),
                new QuestionButton("Replace", nameof(ConflictResolution.Replace), IsDestructive: true),
            ],
            "Apply to all");
        var answer = await question.ShowAsync(window);
        var resolution = Enum.TryParse<ConflictResolution>(answer, out var parsed) ? parsed : ConflictResolution.Stop;
        return new ConflictDecision(resolution, question.IsChecked);
    }

    /// <inheritdoc/>
    public void ShowDuplicates(DuplicateResultsViewModel results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var window = new DuplicatesWindow { DataContext = results };
        if (owner() is { IsVisible: true } main)
        {
            window.Show(main);
        }
        else
        {
            window.Show();
        }
    }

    /// <inheritdoc/>
    public void ShowSettings(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_settingsWindow is { } open)
        {
            settings.Dispose();
            open.Activate();
            return;
        }

        var window = new SettingsWindow { DataContext = settings };
        window.Closed += (_, _) =>
        {
            settings.Dispose();
            _settingsWindow = null;
        };
        _settingsWindow = window;
        if (owner() is { IsVisible: true } main)
        {
            window.Show(main);
        }
        else
        {
            window.Show();
        }
    }
}
