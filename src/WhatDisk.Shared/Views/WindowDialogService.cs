using WhatDisk.FileOperations;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// Asks questions inside the main window, over its dimmed, disabled content, rather than in windows of their own.
/// </summary>
public sealed class WindowDialogService : IDialogService
{
    /// <summary>Gets the main window.</summary>
    private readonly Func<MainWindow?> _owner;

    /// <summary>The main window the service listens to, once known.</summary>
    private MainWindow? _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowDialogService"/> class.
    /// </summary>
    /// <param name="owner">Gets the main window, which shows the questions.</param>
    public WindowDialogService(Func<MainWindow?> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        Subscribe(owner());
    }

    /// <inheritdoc/>
    public event EventHandler? IsShowingChanged;

    /// <inheritdoc/>
    public bool IsShowing => _subscribed?.IsShowingDialog == true;

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A destructive action is never the default, so Return alone cannot destroy anything.
        var cancel = new QuestionButton("Cancel", "cancel", IsDefault: request.IsDestructive, IsCancel: true);
        var confirm = new QuestionButton(request.ConfirmText, "confirm", IsDefault: !request.IsDestructive, IsDestructive: request.IsDestructive);

        // macOS puts the confirming button last (rightmost); Windows puts it first, before Cancel.
        var question = new QuestionView(request.Title, request.Message, OperatingSystem.IsMacOS() ? [cancel, confirm] : [confirm, cancel]);
        return await AskAsync(question) == "confirm";
    }

    /// <inheritdoc/>
    public async Task<ConflictDecision> ResolveConflictAsync(TransferConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        var name = Path.GetFileName(conflict.ExistingPath);
        var parent = Path.GetDirectoryName(conflict.ExistingPath) ?? conflict.ExistingPath;
        var folder = Path.GetFileName(parent) is { Length: > 0 } folderName ? folderName : parent;
        var kind = conflict.IsDirectory ? "folder" : "item";
        var question = new QuestionView(
            $"An {kind} named “{name}” already exists in “{folder}”.",
            "Replace it, keep both (the new one gets a numbered name), skip this item, or stop?",
            [
                new QuestionButton("Stop", nameof(ConflictResolution.Stop), IsCancel: true),
                new QuestionButton("Skip", nameof(ConflictResolution.Skip)),
                new QuestionButton("Keep Both", nameof(ConflictResolution.KeepBoth), IsDefault: true),
                new QuestionButton("Replace", nameof(ConflictResolution.Replace), IsDestructive: true),
            ],
            "Apply to all");
        var answer = await AskAsync(question);
        if (answer is null)
        {
            return new ConflictDecision(ConflictResolution.Skip);
        }

        var resolution = Enum.TryParse<ConflictResolution>(answer, out var parsed) ? parsed : ConflictResolution.Stop;
        return new ConflictDecision(resolution, question.IsChecked);
    }

    /// <summary>
    /// Shows a question in the main window and waits for the answer.
    /// </summary>
    /// <param name="question">The question.</param>
    /// <returns>The answer, or <see langword="null"/> when there is no visible main window to ask in.</returns>
    private async Task<string?> AskAsync(QuestionView question)
    {
        if (_owner() is not { IsVisible: true } window)
        {
            return null;
        }

        Subscribe(window);
        return await window.ShowDialogAsync(question);
    }

    /// <summary>
    /// Listens to a main window's dialogs (questions and the About box), so that <see cref="IsShowingChanged"/> follows
    /// them all.
    /// </summary>
    /// <param name="window">The main window, or <see langword="null"/>.</param>
    private void Subscribe(MainWindow? window)
    {
        if (window is null || ReferenceEquals(_subscribed, window))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.DialogShowingChanged -= OnDialogShowingChanged;
        }

        _subscribed = window;
        window.DialogShowingChanged += OnDialogShowingChanged;
    }

    /// <summary>
    /// Passes on that a question started or stopped showing.
    /// </summary>
    /// <param name="sender">The main window.</param>
    /// <param name="e">Unused.</param>
    private void OnDialogShowingChanged(object? sender, EventArgs e) => IsShowingChanged?.Invoke(this, e);
}
