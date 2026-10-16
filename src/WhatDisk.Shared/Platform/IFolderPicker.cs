namespace WhatDisk.Platform;

/// <summary>
/// Lets the user choose a folder with the platform's native dialog.
/// </summary>
public interface IFolderPicker
{
    /// <summary>
    /// Gets a value indicating whether a folder dialog is available.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Shows the folder dialog.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The chosen folder's local path, or <see langword="null"/> if the user cancelled.</returns>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken);
}
