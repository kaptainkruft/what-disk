using WhatDisk.FileOperations;

namespace WhatDisk.ViewModels;

/// <summary>
/// Asks the user questions, and shows windows of results, on behalf of the view models.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Asks the user to confirm an action.
    /// </summary>
    /// <param name="request">What to ask.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ConfirmAsync(ConfirmationRequest request);

    /// <summary>
    /// Asks the user how to handle a name that is already taken at the destination.
    /// </summary>
    /// <param name="conflict">The conflict.</param>
    /// <returns>The user's decision.</returns>
    Task<ConflictDecision> ResolveConflictAsync(TransferConflict conflict);

    /// <summary>
    /// Shows the results of a duplicate search in a window of their own, without waiting for it to close.
    /// </summary>
    /// <param name="results">The results.</param>
    void ShowDuplicates(DuplicateResultsViewModel results);

    /// <summary>
    /// Shows the settings window, or brings it to the front when it is already open.
    /// </summary>
    /// <param name="settings">The settings window's view model, used when the window is not already open.</param>
    void ShowSettings(SettingsViewModel settings);
}

/// <summary>
/// A question asking the user to confirm an action.
/// </summary>
/// <param name="Title">The question, for example "Delete 3 items immediately?".</param>
/// <param name="Message">The explanation, for example the total size and whether it can be undone.</param>
/// <param name="ConfirmText">The label of the confirming button.</param>
/// <param name="IsDestructive">Whether the action destroys data, so the button is styled as dangerous.</param>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmText, bool IsDestructive);

/// <summary>
/// A dialog service that declines every confirmation, skips every conflict and shows nothing, so that nothing
/// destructive happens when no user interface is attached.
/// </summary>
public sealed class DecliningDialogService : IDialogService
{
    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(ConfirmationRequest request) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<ConflictDecision> ResolveConflictAsync(TransferConflict conflict) => Task.FromResult(new ConflictDecision(ConflictResolution.Skip));

    /// <inheritdoc/>
    public void ShowDuplicates(DuplicateResultsViewModel results)
    {
    }

    /// <inheritdoc/>
    public void ShowSettings(SettingsViewModel settings)
    {
    }
}
