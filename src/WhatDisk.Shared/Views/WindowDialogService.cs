using Avalonia.Controls;
using WhatDisk.FileOperations;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// Asks questions with modal windows over the main window.
/// </summary>
/// <param name="owner">Gets the window that owns the questions.</param>
public sealed class WindowDialogService(Func<Window?> owner) : IDialogService
{
    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (owner() is not { IsVisible: true } window)
        {
            return false;
        }

        // A destructive action is never the default, so Return alone cannot destroy anything.
        var question = new QuestionWindow(request.Title, request.Message,
        [
            new QuestionButton("Cancel", "cancel", IsDefault: request.IsDestructive, IsCancel: true),
            new QuestionButton(request.ConfirmText, "confirm", IsDefault: !request.IsDestructive, IsDestructive: request.IsDestructive),
        ]);
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
        var folder = Path.GetFileName(Path.GetDirectoryName(conflict.ExistingPath)) ?? conflict.ExistingPath;
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
}
