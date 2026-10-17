using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Windows;

/// <summary>
/// Registers the Windows implementations of the platform services, ahead of the shared defaults.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Gets the Windows conventions: several drives per scan, case-insensitive paths, sizes in units of 1024 labelled
    /// KB, MB and GB as in File Explorer, File Explorer's words and shortcuts, and an in-window menu bar.
    /// </summary>
    public static PlatformCapabilities Capabilities { get; } = new()
    {
        PlatformName = "Windows",
        SupportsMultipleVolumeSelection = true,
        PathPlaceholder = @"Folder to scan, for example C:\Users or \\server\share",
        PathComparison = StringComparison.OrdinalIgnoreCase,
        ByteUnits = ByteUnitConvention.PowersOf1024,
        DirectoryNoun = "Folder",
        Shortcuts = ShortcutScheme.Windows,
        FileManagerName = "File Explorer",
        TrashName = "Recycle Bin",
        InfoCommandName = "Properties",
        PermanentDeleteName = "Delete Permanently",
        HasApplicationMenu = false,
    };

    /// <summary>
    /// Adds the Windows platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton(Capabilities);
        services.AddSingleton<IFileSystemEnumerator>(static _ => WindowsFileSystemEnumerator.CreateOrFallback());
        services.AddSingleton<IVolumeService, WindowsVolumeService>();
        services.AddSingleton<IIconProvider, WindowsIconProvider>();
        services.AddSingleton<IFileTypeProvider, WindowsFileTypeProvider>();
        services.AddSingleton<IShellService, WindowsShellService>();
        return services;
    }
}
