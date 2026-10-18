using System.Diagnostics;
using System.Runtime.Versioning;
using WhatDisk.Duplicates;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests the <c>getattrlistbulk</c> enumerator against the real file system. Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacFileSystemEnumeratorTests : IDisposable
{
    /// <summary>A temporary directory deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-mac-");

    /// <summary>The enumerator under test.</summary>
    private readonly MacFileSystemEnumerator _enumerator = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MacFileSystemEnumeratorTests"/> class, skipping off macOS.
    /// </summary>
    public MacFileSystemEnumeratorTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// Names, types and logical sizes agree with the portable enumerator, and allocation and identity are reported.
    /// </summary>
    [Fact]
    public void ReadDirectory_AgreesWithThePortableEnumerator()
    {
        File.WriteAllBytes(Path.Join(_temp.FullName, "small.bin"), new byte[10]);
        File.WriteAllBytes(Path.Join(_temp.FullName, "large.bin"), new byte[100_000]);
        File.WriteAllBytes(Path.Join(_temp.FullName, "empty.bin"), []);
        Directory.CreateDirectory(Path.Join(_temp.FullName, "sub"));
        File.CreateSymbolicLink(Path.Join(_temp.FullName, "link"), "small.bin");

        var native = Read(_temp.FullName).ToDictionary(static entry => entry.Name);
        var portable = new List<FileSystemEntryInfo>();
        new ManagedFileSystemEnumerator().ReadDirectory(_temp.FullName, portable, TestContext.Current.CancellationToken);

        Assert.Equal(portable.Select(static entry => entry.Name).Order(), native.Keys.Order());
        Assert.Equal(EntryType.Directory, native["sub"].Type);
        Assert.Equal(EntryType.SymbolicLink, native["link"].Type);
        Assert.Equal(10, native["small.bin"].LogicalSize);
        Assert.Equal(100_000, native["large.bin"].LogicalSize);
        Assert.True(native["large.bin"].AllocatedSize >= 100_000);
        Assert.Equal(0, native["empty.bin"].AllocatedSize);
        Assert.All(native.Values, static entry => Assert.True(entry.Identity.IsKnown));
        Assert.Single(native.Values.Select(static entry => entry.Identity.Device).Distinct());
    }

    /// <summary>
    /// Hard links report their link count and share an identity.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReportsHardLinks()
    {
        var original = Path.Join(_temp.FullName, "original.bin");
        File.WriteAllBytes(original, new byte[5000]);
        using (var process = Process.Start("/bin/ln", [original, Path.Join(_temp.FullName, "copy.bin")]))
        {
            process.WaitForExit();
        }

        var entries = Read(_temp.FullName);

        Assert.All(entries, static entry => Assert.Equal(2u, entry.LinkCount));
        Assert.Single(entries.Select(static entry => entry.Identity).Distinct());
    }

    /// <summary>
    /// The duplicate search uses the enumerator's identities to compare hard links to the same file once, so a file
    /// and its hard link are not reported as duplicates of each other, while a real copy is.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task DuplicateFinder_ComparesHardLinksOnce()
    {
        var original = Path.Join(_temp.FullName, "original.bin");
        var link = Path.Join(_temp.FullName, "link.bin");
        var copy = Path.Join(_temp.FullName, "copy.bin");
        File.WriteAllBytes(original, new byte[5000]);
        using (var process = Process.Start("/bin/ln", [original, link]))
        {
            process.WaitForExit();
        }

        File.Copy(original, copy);
        var finder = new DuplicateFinder(_enumerator.GetIdentity);

        var result = await finder.FindAsync([new DuplicateCandidate(original, 5000), new DuplicateCandidate(link, 5000), new DuplicateCandidate(copy, 5000)], null, TestContext.Current.CancellationToken);

        Assert.Equal([copy, link], Assert.Single(result.Sets).Paths);
        Assert.Equal(1, result.HardLinks);
    }

    /// <summary>
    /// The identity returned by a read is that of the directory itself.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReturnsTheDirectoryIdentity()
    {
        var identity = _enumerator.ReadDirectory(_temp.FullName, new List<FileSystemEntryInfo>(), TestContext.Current.CancellationToken);

        Assert.True(identity.IsKnown);
        Assert.Equal(_enumerator.GetIdentity(_temp.FullName), identity);
    }

    /// <summary>
    /// Missing and unreadable directories raise the expected exceptions.
    /// </summary>
    [Fact]
    public void ReadDirectory_MapsErrors()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Read(Path.Join(_temp.FullName, "missing")));
        Assert.Throws<DirectoryNotFoundException>(() =>
        {
            var file = Path.Join(_temp.FullName, "file.txt");
            File.WriteAllText(file, "x");
            return Read(file);
        });

        var locked = Directory.CreateDirectory(Path.Join(_temp.FullName, "locked"));
        File.SetUnixFileMode(locked.FullName, UnixFileMode.None);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => Read(locked.FullName));
        }
        finally
        {
            File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// The root of the startup disk exposes firmlinks into the data volume and mount points such as <c>/dev</c>.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReportsFirmlinksAndMountPointsAtTheRoot()
    {
        var entries = Read("/").ToDictionary(static entry => entry.Name);

        Assert.True(entries["Users"].Attributes.HasFlag(EntryAttributes.Firmlink));
        Assert.True(entries["dev"].Attributes.HasFlag(EntryAttributes.MountPoint));
        Assert.False(entries["System"].Attributes.HasFlag(EntryAttributes.Firmlink));
    }

    /// <summary>
    /// The system and data volumes of the startup disk have different physical device numbers.
    /// </summary>
    [Fact]
    public void GetIdentity_DistinguishesTheSystemAndDataVolumes()
    {
        var root = _enumerator.GetIdentity("/");
        var users = _enumerator.GetIdentity("/Users");

        Assert.True(root.IsKnown && users.IsKnown);
        Assert.NotEqual(root.Device, users.Device);
        Assert.Equal(users.Device, _enumerator.GetIdentity("/System/Volumes/Data/Users").Device);
    }

    /// <summary>
    /// A full scan with the native enumerator matches a scan with the portable one.
    /// </summary>
    /// <returns>A task that completes when both scans have finished.</returns>
    [Fact]
    public async Task Scan_MatchesThePortableEnumerator()
    {
        var random = new Random(42);
        for (var index = 0; index < 120; index++)
        {
            var directory = Directory.CreateDirectory(Path.Join(_temp.FullName, $"a{index % 7}", $"b{index % 11}"));
            File.WriteAllBytes(Path.Join(directory.FullName, $"f{index}.bin"), new byte[random.Next(0, 50_000)]);
        }

        var volumes = new MacVolumeService();
        var native = await ScanAsync(new DiskScanner(_enumerator, volumes, TimeProvider.System));
        var portable = await ScanAsync(new DiskScanner(new ManagedFileSystemEnumerator(), volumes, TimeProvider.System));

        Assert.Equal(portable.FileCount, native.FileCount);
        Assert.Equal(portable.DirectoryCount, native.DirectoryCount);
        Assert.Equal(portable.LogicalSize, native.LogicalSize);
        Assert.True(native.Size >= native.LogicalSize);
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// Scans the temporary directory.
    /// </summary>
    /// <param name="scanner">The scanner to use.</param>
    /// <returns>The scanned root.</returns>
    private async Task<RootNode> ScanAsync(DiskScanner scanner)
    {
        using var session = scanner.Start(new ScanRequest([_temp.FullName]));
        Assert.Equal(ScanStatus.Completed, await session.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        return session.Roots[0];
    }

    /// <summary>
    /// Reads a directory with the native enumerator.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <returns>The entries.</returns>
    private List<FileSystemEntryInfo> Read(string path)
    {
        var entries = new List<FileSystemEntryInfo>();
        _enumerator.ReadDirectory(path, entries, TestContext.Current.CancellationToken);
        return entries;
    }
}
