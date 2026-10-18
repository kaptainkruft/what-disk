using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS;

/// <summary>
/// Registers the macOS implementations of WhatDisk's platform services.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Gets the conventions of macOS: one volume per scan, case-insensitive paths and decimal byte units.
    /// </summary>
    public static PlatformCapabilities Capabilities { get; } = new()
    {
        PlatformName = "macOS",
        SupportsMultipleVolumeSelection = false,
        PathPlaceholder = "Folder to scan, for example ~/Documents or /Volumes/Backup",
        PathComparison = StringComparison.OrdinalIgnoreCase,
        ByteUnits = ByteUnitConvention.PowersOf1000,
        DirectoryNoun = "Folder",
        Shortcuts = ShortcutScheme.MacOS,
        FileManagerName = "Finder",
        TrashName = "Trash",
        InfoCommandName = "Get Info",
        PermanentDeleteName = "Delete Immediately",
        HasApplicationMenu = true,
    };

    /// <summary>
    /// Adds the macOS services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="useMenuBar">
    /// Whether the menus are in the macOS menu bar; when not, they are drawn in the window (with Quit, Settings and
    /// About), as on Windows.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMacOSPlatform(this IServiceCollection services, bool useMenuBar = true)
    {
        services.AddSingleton(useMenuBar ? Capabilities : Capabilities with { HasApplicationMenu = false });
        services.AddSingleton<IFileSystemEnumerator>(static _ => MacFileSystemEnumerator.CreateOrFallback());
        services.AddSingleton<IVolumeService, MacVolumeService>();
        services.AddSingleton<IIconProvider, MacIconProvider>();
        services.AddSingleton<IFileTypeProvider, MacFileTypeProvider>();
        services.AddSingleton<IShellService, MacShellService>();
        return services;
    }
}
