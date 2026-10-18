using Avalonia.Controls;
using Avalonia.Input;

namespace WhatDisk.Views;

/// <summary>
/// A dialog shown inside the main window, over a dimmed and disabled page, rather than in a window of its own: a
/// question or the About box. Keeping the app to one window means the macOS menu bar never switches between windows'
/// menus, which Avalonia does unreliably on macOS 27. Escape gives the cancel result.
/// </summary>
public abstract class InWindowDialog : UserControl
{
    /// <summary>Completes with the dialog's result.</summary>
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the result when the dialog is completed, once.
    /// </summary>
    public Task<string?> Completion => _completion.Task;

    /// <summary>
    /// Gets the result that Escape gives, or <see langword="null"/>.
    /// </summary>
    protected virtual string? CancelResult => null;

    /// <summary>
    /// Gives keyboard focus to the control that should have it when the dialog appears, such as the default button.
    /// </summary>
    public abstract void FocusDefault();

    /// <summary>
    /// Ends the dialog with a result.
    /// </summary>
    /// <param name="result">The result.</param>
    protected void Complete(string? result) => _completion.TrySetResult(result);

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Complete(CancelResult);
            e.Handled = true;
        }
    }
}
