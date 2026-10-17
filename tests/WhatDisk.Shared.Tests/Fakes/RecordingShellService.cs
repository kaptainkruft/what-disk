using WhatDisk.Platform;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// A shell service that records every request. Moving to the Trash deletes the item, so the file system changes as it
/// would with a real Trash.
/// </summary>
internal sealed class RecordingShellService : IShellService
{
    /// <summary>
    /// Gets every request, as "action: path|path".
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// Gets the applications returned by <see cref="GetApplications"/>.
    /// </summary>
    public List<ApplicationChoice> Applications { get; } = [new("Preview", "/Applications/Preview.app", true), new("TextEdit", "/Applications/TextEdit.app", false)];

    /// <inheritdoc/>
    public ShellCapabilities Capabilities { get; set; } =
        ShellCapabilities.Trash | ShellCapabilities.Open | ShellCapabilities.OpenWith | ShellCapabilities.Reveal | ShellCapabilities.ShowInfo
        | ShellCapabilities.QuickLook | ShellCapabilities.CopyItems | ShellCapabilities.CopyText;

    /// <inheritdoc/>
    public Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        Record("trash", [path]);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Open(IReadOnlyList<string> paths) => Record("open", paths);

    /// <inheritdoc/>
    public IReadOnlyList<ApplicationChoice> GetApplications(string path) => Applications;

    /// <inheritdoc/>
    public void OpenWith(IReadOnlyList<string> paths, ApplicationChoice application) => Record($"open with {application.Name}", paths);

    /// <inheritdoc/>
    public void Reveal(IReadOnlyList<string> paths) => Record("reveal", paths);

    /// <inheritdoc/>
    public void ShowInfo(IReadOnlyList<string> paths) => Record("info", paths);

    /// <inheritdoc/>
    public void QuickLook(IReadOnlyList<string> paths) => Record("quick look", paths);

    /// <inheritdoc/>
    public void CopyItems(IReadOnlyList<string> paths) => Record("copy", paths);

    /// <inheritdoc/>
    public void CopyText(string text) => Record("copy text", [text]);

    /// <summary>
    /// Records a request.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="paths">The paths.</param>
    private void Record(string action, IReadOnlyList<string> paths) => Calls.Add($"{action}: {string.Join('|', paths)}");
}
