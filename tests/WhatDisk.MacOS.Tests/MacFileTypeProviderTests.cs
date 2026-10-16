using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests the macOS view of file types. Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacFileTypeProviderTests
{
    /// <summary>The provider under test.</summary>
    private readonly MacFileTypeProvider _provider = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MacFileTypeProviderTests"/> class, skipping off macOS.
    /// </summary>
    public MacFileTypeProviderTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// Known extensions are described by the system; unknown ones fall back to the extension.
    /// </summary>
    [Fact]
    public void Describe_UsesSystemDescriptions()
    {
        Assert.Contains("PDF", _provider.Describe(".pdf"), StringComparison.Ordinal);
        Assert.NotEqual("JPG file", _provider.Describe(".jpg"));
        Assert.Equal("QWZX file", _provider.Describe(".qwzx"));
        Assert.Equal("Files without an extension", _provider.Describe(FileTypes.NoExtension));
    }

    /// <summary>
    /// Application bundles and other packages are a type of their own; ordinary folders are not.
    /// </summary>
    /// <returns>A task that completes when the sample tree has been scanned.</returns>
    [Fact]
    public async Task GetPackageType_RecognisesPackages()
    {
        var temp = Directory.CreateTempSubdirectory("whatdisk-types-");
        try
        {
            Directory.CreateDirectory(Path.Join(temp.FullName, "Tool.app", "Contents"));
            Directory.CreateDirectory(Path.Join(temp.FullName, "plain"));
            using var session = new DiskScanner(new MacFileSystemEnumerator(), new MacVolumeService(), TimeProvider.System).Start(new ScanRequest([temp.FullName]));
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var children = session.Roots[0].Children.OfType<DirectoryNode>().ToDictionary(static child => child.Name);

            Assert.Equal(".app", _provider.GetPackageType(children["Tool.app"]));
            Assert.Null(_provider.GetPackageType(children["plain"]));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
