using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhatDisk.Platform;

namespace WhatDisk.Views;

/// <summary>
/// Shows the platform's native folder dialog through Avalonia's storage provider.
/// </summary>
/// <param name="topLevel">Gets the window that owns the dialog.</param>
public sealed class TopLevelFolderPicker(Func<TopLevel?> topLevel) : IFolderPicker
{
    /// <inheritdoc/>
    public bool IsSupported => topLevel()?.StorageProvider.CanPickFolder ?? false;

    /// <inheritdoc/>
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        if (topLevel() is not { } owner || !owner.StorageProvider.CanPickFolder)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to scan",
            AllowMultiple = false,
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
