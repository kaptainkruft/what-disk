using System.Diagnostics;
using System.Runtime.Versioning;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Mounts a real disk image inside a scanned folder and checks that the scan lists it as a separate volume.
/// Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MountedVolumeScanTests : IDisposable
{
    /// <summary>A temporary directory deleted after the test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-mount-");

    /// <summary>The mount point of the disk image, once attached.</summary>
    private string? _mountPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MountedVolumeScanTests"/> class, skipping off macOS.
    /// </summary>
    public MountedVolumeScanTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// A volume mounted beneath the scanned folder becomes its own top-level root, named after the volume, and its
    /// contents are not counted in the folder that contains the mount point.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [Fact]
    public async Task Scan_ListsAMountedDiskImageAsASeparateVolume()
    {
        var tree = Directory.CreateDirectory(Path.Join(_temp.FullName, "tree"));
        var mount = Directory.CreateDirectory(Path.Join(tree.FullName, "mnt"));
        var image = Path.Join(_temp.FullName, "test.dmg");
        Run("hdiutil", "create", "-size", "8m", "-fs", "HFS+", "-volname", "WhatDiskTest", image, "-quiet");
        Run("hdiutil", "attach", image, "-mountpoint", mount.FullName, "-nobrowse", "-quiet");
        _mountPoint = mount.FullName;
        await File.WriteAllBytesAsync(Path.Join(mount.FullName, "inside.bin"), new byte[300_000], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Join(tree.FullName, "outside.bin"), new byte[40_000], TestContext.Current.CancellationToken);

        var scanner = new DiskScanner(new MacFileSystemEnumerator(), new MacVolumeService(), TimeProvider.System);
        using var session = scanner.Start(new ScanRequest([tree.FullName]));
        Assert.Equal(ScanStatus.Completed, await session.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

        Assert.Equal(2, session.Roots.Count);
        var folder = session.Roots[0];
        var volume = session.Roots[1];
        Assert.True(volume.IsVolumeRoot);
        Assert.Equal("WhatDiskTest", volume.Name);
        Assert.Contains(volume.Children, static child => child.Name == "inside.bin");
        Assert.True(volume.Size >= 300_000);
        Assert.InRange(folder.Size, 40_000, 100_000);
        var placeholder = Assert.IsType<PlaceholderNode>(folder.Children.Single(static child => child.Name == "mnt"));
        Assert.Same(volume, placeholder.LinkedRoot);
    }

    /// <summary>
    /// Detaches the disk image and deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (_mountPoint is not null)
        {
            Run("hdiutil", "detach", _mountPoint, "-force", "-quiet");
        }

        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Runs a command and fails the test if it does not succeed.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments.</param>
    private static void Run(string fileName, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { RedirectStandardError = true })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {error}");
    }
}
