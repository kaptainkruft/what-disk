using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WhatDisk.Formatting;
using WhatDisk.Hosting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests that the Windows head registers its platform services and conventions ahead of the shared defaults.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformRegistrationTests
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsPlatformRegistrationTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsPlatformRegistrationTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");

    /// <summary>
    /// The service provider validates and resolves the Windows implementations.
    /// </summary>
    [Fact]
    public void CreateServices_UsesTheWindowsImplementations()
    {
        using var services = WhatDiskApp.CreateServices(static collection => collection.AddWindowsPlatform());

        Assert.IsType<WindowsFileSystemEnumerator>(services.GetRequiredService<IFileSystemEnumerator>());
        Assert.IsType<WindowsVolumeService>(services.GetRequiredService<IVolumeService>());
        Assert.IsType<WindowsIconProvider>(services.GetRequiredService<IIconProvider>());
        Assert.IsType<WindowsFileTypeProvider>(services.GetRequiredService<IFileTypeProvider>());
        Assert.IsType<WindowsShellService>(services.GetRequiredService<IShellService>());
        Assert.IsType<DiskScanner>(services.GetRequiredService<IDiskScanner>());
    }

    /// <summary>
    /// The Windows conventions: several drives per scan, case-insensitive paths, 1024-byte units, File Explorer's
    /// words and shortcuts, and no application menu.
    /// </summary>
    [Fact]
    public void Capabilities_FollowWindowsConventions()
    {
        var platform = WindowsPlatformServiceCollectionExtensions.Capabilities;

        Assert.Equal("Windows", platform.PlatformName);
        Assert.True(platform.SupportsMultipleVolumeSelection);
        Assert.Equal(StringComparison.OrdinalIgnoreCase, platform.PathComparison);
        Assert.Equal(ByteUnitConvention.PowersOf1024, platform.ByteUnits);
        Assert.Same(ShortcutScheme.Windows, platform.Shortcuts);
        Assert.Equal(("File Explorer", "Recycle Bin", "Properties"), (platform.FileManagerName, platform.TrashName, platform.InfoCommandName));
        Assert.False(platform.HasApplicationMenu);
    }
}
