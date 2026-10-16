using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WhatDisk.Hosting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests that the macOS head registers its platform services ahead of the shared defaults.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacPlatformRegistrationTests
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MacPlatformRegistrationTests"/> class, skipping off macOS.
    /// </summary>
    public MacPlatformRegistrationTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// The service provider validates and resolves the macOS implementations.
    /// </summary>
    [Fact]
    public void CreateServices_UsesTheMacOSImplementations()
    {
        using var services = WhatDiskApp.CreateServices(static collection => collection.AddMacOSPlatform());

        Assert.IsType<MacFileSystemEnumerator>(services.GetRequiredService<IFileSystemEnumerator>());
        Assert.IsType<MacVolumeService>(services.GetRequiredService<IVolumeService>());
        Assert.IsType<MacIconProvider>(services.GetRequiredService<IIconProvider>());
        var platform = services.GetRequiredService<PlatformCapabilities>();
        Assert.Equal("macOS", platform.PlatformName);
        Assert.False(platform.SupportsMultipleVolumeSelection);
        Assert.IsType<DiskScanner>(services.GetRequiredService<IDiskScanner>());
    }
}
