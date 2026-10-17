using WhatDisk.Formatting;

namespace WhatDisk.Platform;

/// <summary>
/// Describes platform conventions that shape the user interface.
/// </summary>
public sealed record PlatformCapabilities
{
    /// <summary>
    /// Gets the name of the platform, for example <c>Windows</c>.
    /// </summary>
    public required string PlatformName { get; init; }

    /// <summary>
    /// Gets a value indicating whether several volumes can be selected for one scan (as with drive letters on Windows).
    /// </summary>
    public bool SupportsMultipleVolumeSelection { get; init; }

    /// <summary>
    /// Gets the hint shown in the empty path box, with examples of paths the platform accepts.
    /// </summary>
    public required string PathPlaceholder { get; init; }

    /// <summary>
    /// Gets how paths are compared on this platform.
    /// </summary>
    public StringComparison PathComparison { get; init; } = StringComparison.Ordinal;

    /// <summary>
    /// Gets the platform's convention for byte units.
    /// </summary>
    public ByteUnitConvention ByteUnits { get; init; } = ByteUnitConvention.PowersOf1000;

    /// <summary>
    /// Gets the platform's word for a directory, for example <c>Folder</c>.
    /// </summary>
    public string DirectoryNoun { get; init; } = "Folder";

    /// <summary>
    /// Gets the keyboard shortcuts and drag modifiers for actions on items.
    /// </summary>
    public ShortcutScheme Shortcuts { get; init; } = ShortcutScheme.ForCurrentPlatform;

    /// <summary>
    /// Gets the name of the platform's file manager, as in "Show in Finder" or "Show in File Explorer".
    /// </summary>
    public string FileManagerName { get; init; } = "File Manager";

    /// <summary>
    /// Gets the name of the place deleted items go to, for example <c>Trash</c> or <c>Recycle Bin</c>.
    /// </summary>
    public string TrashName { get; init; } = "Trash";

    /// <summary>
    /// Gets the name of the file manager's information window command, for example <c>Get Info</c> or <c>Properties</c>.
    /// </summary>
    public string InfoCommandName { get; init; } = "Properties";

    /// <summary>
    /// Gets the name of the command that deletes without the Trash, for example <c>Delete Immediately</c> or
    /// <c>Delete Permanently</c>.
    /// </summary>
    public string PermanentDeleteName { get; init; } = "Delete Permanently";

    /// <summary>
    /// Gets a value indicating whether the platform has an application menu (macOS), which holds About; elsewhere the
    /// window's menu bar has a Help menu for it, and menu titles have access keys.
    /// </summary>
    public bool HasApplicationMenu { get; init; }

    /// <summary>
    /// Gets capabilities suitable for an unidentified platform.
    /// </summary>
    public static PlatformCapabilities Generic { get; } = new()
    {
        PlatformName = "Generic",
        PathPlaceholder = "Enter a path to scan",
    };
}
