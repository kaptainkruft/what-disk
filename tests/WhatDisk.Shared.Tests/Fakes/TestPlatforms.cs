using WhatDisk.Formatting;
using WhatDisk.Platform;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// Platform conventions like those of the macOS and Windows heads, so that shared views can be tested with either
/// platform's words and shortcuts on any OS. (Each head's own tests check its real conventions.)
/// </summary>
internal static class TestPlatforms
{
    /// <summary>
    /// Gets macOS-like conventions: Finder words, the macOS shortcuts and an application menu.
    /// </summary>
    public static PlatformCapabilities MacOS { get; } = new()
    {
        PlatformName = "macOS",
        PathPlaceholder = "Folder to scan",
        Shortcuts = ShortcutScheme.MacOS,
        FileManagerName = "Finder",
        TrashName = "Trash",
        InfoCommandName = "Get Info",
        PermanentDeleteName = "Delete Immediately",
        HasApplicationMenu = true,
    };

    /// <summary>
    /// Gets Windows-like conventions: File Explorer words, the Windows shortcuts, 1024-byte units and an in-window menu
    /// bar.
    /// </summary>
    public static PlatformCapabilities Windows { get; } = new()
    {
        PlatformName = "Windows",
        PathPlaceholder = "Folder to scan",
        SupportsMultipleVolumeSelection = true,
        ByteUnits = ByteUnitConvention.PowersOf1024,
        Shortcuts = ShortcutScheme.Windows,
        FileManagerName = "File Explorer",
        TrashName = "Recycle Bin",
        InfoCommandName = "Properties",
        PermanentDeleteName = "Delete Permanently",
        HasApplicationMenu = false,
    };

    /// <summary>
    /// Gets the conventions with a given name.
    /// </summary>
    /// <param name="name">"macOS" or "Windows".</param>
    /// <returns>The conventions.</returns>
    public static PlatformCapabilities Named(string name) => name switch
    {
        "macOS" => MacOS,
        "Windows" => Windows,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown platform."),
    };
}
