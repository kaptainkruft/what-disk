namespace WhatDisk.Platform;

/// <summary>
/// The actions on files and folders that the platform's file manager provides. Each platform supports some of them;
/// <see cref="Capabilities"/> says which, so unsupported commands can be disabled.
/// </summary>
public interface IShellService
{
    /// <summary>
    /// Gets the actions this platform supports.
    /// </summary>
    ShellCapabilities Capabilities { get; }

    /// <summary>
    /// Moves an item to the Trash (or Recycle Bin), from where the user can restore it.
    /// </summary>
    /// <param name="path">The item.</param>
    /// <param name="cancellationToken">Cancels the request before it starts.</param>
    /// <returns>A task that completes when the item is in the Trash.</returns>
    /// <exception cref="IOException">The item could not be moved to the Trash.</exception>
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens items with their default applications.
    /// </summary>
    /// <param name="paths">The items.</param>
    void Open(IReadOnlyList<string> paths);

    /// <summary>
    /// Lists the applications that can open an item, the default first.
    /// </summary>
    /// <param name="path">The item.</param>
    /// <returns>The applications.</returns>
    IReadOnlyList<ApplicationChoice> GetApplications(string path);

    /// <summary>
    /// Opens items with a particular application.
    /// </summary>
    /// <param name="paths">The items.</param>
    /// <param name="application">The application.</param>
    void OpenWith(IReadOnlyList<string> paths, ApplicationChoice application);

    /// <summary>
    /// Shows items selected in the file manager.
    /// </summary>
    /// <param name="paths">The items.</param>
    void Reveal(IReadOnlyList<string> paths);

    /// <summary>
    /// Shows the file manager's information window for items.
    /// </summary>
    /// <param name="paths">The items.</param>
    void ShowInfo(IReadOnlyList<string> paths);

    /// <summary>
    /// Shows a preview of items, or closes the preview when it is already open.
    /// </summary>
    /// <param name="paths">The items.</param>
    void QuickLook(IReadOnlyList<string> paths);

    /// <summary>
    /// Puts items on the clipboard so they can be pasted in the file manager.
    /// </summary>
    /// <param name="paths">The items.</param>
    void CopyItems(IReadOnlyList<string> paths);

    /// <summary>
    /// Puts text on the clipboard.
    /// </summary>
    /// <param name="text">The text.</param>
    void CopyText(string text);
}

/// <summary>
/// The file-manager actions a platform supports.
/// </summary>
[Flags]
public enum ShellCapabilities
{
    /// <summary>No actions.</summary>
    None = 0,

    /// <summary><see cref="IShellService.MoveToTrashAsync"/>.</summary>
    Trash = 1 << 0,

    /// <summary><see cref="IShellService.Open"/>.</summary>
    Open = 1 << 1,

    /// <summary><see cref="IShellService.GetApplications"/> and <see cref="IShellService.OpenWith"/>.</summary>
    OpenWith = 1 << 2,

    /// <summary><see cref="IShellService.Reveal"/>.</summary>
    Reveal = 1 << 3,

    /// <summary><see cref="IShellService.ShowInfo"/>.</summary>
    ShowInfo = 1 << 4,

    /// <summary><see cref="IShellService.QuickLook"/>.</summary>
    QuickLook = 1 << 5,

    /// <summary><see cref="IShellService.CopyItems"/>.</summary>
    CopyItems = 1 << 6,

    /// <summary><see cref="IShellService.CopyText"/>.</summary>
    CopyText = 1 << 7,
}

/// <summary>
/// An application that can open an item.
/// </summary>
/// <param name="Name">The application's display name.</param>
/// <param name="Path">The application's path.</param>
/// <param name="IsDefault">Whether it is the default application for the item.</param>
public sealed record ApplicationChoice(string Name, string Path, bool IsDefault);
