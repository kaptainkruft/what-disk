using System.Diagnostics;

namespace WhatDisk.Platform;

/// <summary>
/// A shell service that can only open items, through the operating system's default handler. Platforms provide
/// richer implementations.
/// </summary>
public class DefaultShellService : IShellService
{
    /// <inheritdoc/>
    public virtual ShellCapabilities Capabilities => ShellCapabilities.Open;

    /// <inheritdoc/>
    public virtual Task MoveToTrashAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Moving items to the Trash is not supported on this platform.");

    /// <inheritdoc/>
    public virtual void Open(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// <inheritdoc/>
    public virtual IReadOnlyList<ApplicationChoice> GetApplications(string path) => [];

    /// <inheritdoc/>
    public virtual void OpenWith(IReadOnlyList<string> paths, ApplicationChoice application) =>
        throw new NotSupportedException("Choosing an application is not supported on this platform.");

    /// <inheritdoc/>
    public virtual void Reveal(IReadOnlyList<string> paths) =>
        throw new NotSupportedException("Revealing items is not supported on this platform.");

    /// <inheritdoc/>
    public virtual void ShowInfo(IReadOnlyList<string> paths) =>
        throw new NotSupportedException("Item information is not supported on this platform.");

    /// <inheritdoc/>
    public virtual void QuickLook(IReadOnlyList<string> paths) =>
        throw new NotSupportedException("Previews are not supported on this platform.");

    /// <inheritdoc/>
    public virtual void CopyItems(IReadOnlyList<string> paths) =>
        throw new NotSupportedException("Copying items is not supported on this platform.");

    /// <inheritdoc/>
    public virtual void CopyText(string text) =>
        throw new NotSupportedException("Copying text is not supported on this platform.");
}
