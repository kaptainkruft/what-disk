using WhatDisk.Platform;

namespace WhatDisk.ViewModels;

/// <summary>
/// What the window's menus (declared in <c>MainWindow.axaml</c>) show: the platform's words, and whether the platform
/// supports each file manager action. These never change after start-up, so the menus are set up once and left alone.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Gets the title of Show in Finder or Show in File Explorer.
    /// </summary>
    public string RevealHeader => $"Show in {Platform.FileManagerName}";

    /// <summary>
    /// Gets the title of Move to Trash or Move to Recycle Bin.
    /// </summary>
    public string MoveToTrashHeader => $"Move to {Platform.TrashName}";

    /// <summary>
    /// Gets the title of Delete Immediately… or Delete Permanently….
    /// </summary>
    public string DeletePermanentlyHeader => $"{Platform.PermanentDeleteName}…";

    /// <summary>
    /// Gets a value indicating whether the menus are drawn in the window (Windows, or macOS with the menu bar turned off
    /// in the settings): only then are Exit (or Quit), Settings in Tools and the Help menu shown, since the macOS menu
    /// bar has them in the application menu.
    /// </summary>
    public bool HasWindowMenus => !Platform.HasApplicationMenu;

    /// <summary>
    /// Gets the title of the menu item that quits: Quit WhatDisk where it has a shortcut of its own (macOS), E_xit
    /// otherwise (Windows, where Alt+F4 is the system's).
    /// </summary>
    public string ExitHeader => Platform.Shortcuts.Quit is null ? "E_xit" : "Quit WhatDisk";

    /// <summary>
    /// Gets a value indicating whether the platform can open items.
    /// </summary>
    public bool CanShellOpen => Supports(ShellCapabilities.Open);

    /// <summary>
    /// Gets a value indicating whether the platform can show items in its file manager.
    /// </summary>
    public bool CanShellReveal => Supports(ShellCapabilities.Reveal);

    /// <summary>
    /// Gets a value indicating whether the platform can preview items (Quick Look).
    /// </summary>
    public bool CanShellQuickLook => Supports(ShellCapabilities.QuickLook);

    /// <summary>
    /// Gets a value indicating whether the platform can show an item's information window.
    /// </summary>
    public bool CanShellShowInfo => Supports(ShellCapabilities.ShowInfo);

    /// <summary>
    /// Gets a value indicating whether the platform has a Trash or Recycle Bin.
    /// </summary>
    public bool CanShellTrash => Supports(ShellCapabilities.Trash);

    /// <summary>
    /// Gets a value indicating whether the platform can copy items for pasting in its file manager.
    /// </summary>
    public bool CanShellCopyItems => Supports(ShellCapabilities.CopyItems);

    /// <summary>
    /// Gets a value indicating whether the platform can copy text to the clipboard.
    /// </summary>
    public bool CanShellCopyText => Supports(ShellCapabilities.CopyText);
}
