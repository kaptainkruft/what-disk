using System.Runtime.Versioning;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the portable enumerator against real directories.
/// </summary>
public sealed class ManagedFileSystemEnumeratorTests : IDisposable
{
    /// <summary>A temporary directory deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-tests-");

    /// <summary>
    /// Entries are reported with their types and logical sizes, without "." or "..".
    /// </summary>
    [Fact]
    public void ReadDirectory_ReportsEntriesWithTypesAndSizes()
    {
        File.WriteAllBytes(Path.Join(_temp.FullName, "a.bin"), new byte[1234]);
        Directory.CreateDirectory(Path.Join(_temp.FullName, "sub"));
        var entries = new List<FileSystemEntryInfo>();

        var identity = new ManagedFileSystemEnumerator().ReadDirectory(_temp.FullName, entries, TestContext.Current.CancellationToken);

        Assert.False(identity.IsKnown);
        Assert.Equal(2, entries.Count);
        var file = Assert.Single(entries, entry => entry.Name == "a.bin");
        Assert.Equal(EntryType.File, file.Type);
        Assert.Equal(1234, file.LogicalSize);
        Assert.Equal(FileSystemEntryInfo.UnknownSize, file.AllocatedSize);
        Assert.Equal(EntryType.Directory, Assert.Single(entries, entry => entry.Name == "sub").Type);
    }

    /// <summary>
    /// Symbolic links are reported as links.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReportsSymbolicLinksAsLinks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires extra privileges on Windows.");
        Directory.CreateDirectory(Path.Join(_temp.FullName, "target"));
        Directory.CreateSymbolicLink(Path.Join(_temp.FullName, "link"), Path.Join(_temp.FullName, "target"));
        var entries = new List<FileSystemEntryInfo>();

        new ManagedFileSystemEnumerator().ReadDirectory(_temp.FullName, entries, TestContext.Current.CancellationToken);

        Assert.Equal(EntryType.SymbolicLink, Assert.Single(entries, entry => entry.Name == "link").Type);
    }

    /// <summary>
    /// A missing directory raises <see cref="DirectoryNotFoundException"/>.
    /// </summary>
    [Fact]
    public void ReadDirectory_ThrowsForMissingDirectories() =>
        Assert.Throws<DirectoryNotFoundException>(() =>
            new ManagedFileSystemEnumerator().ReadDirectory(Path.Join(_temp.FullName, "missing"), new List<FileSystemEntryInfo>(), TestContext.Current.CancellationToken));

    /// <summary>
    /// A full scan of a real tree produces the exact totals.
    /// </summary>
    [Fact]
    public async Task Scan_OfARealTreeProducesExactTotals()
    {
        long expected = 0;
        for (var index = 0; index < 20; index++)
        {
            var directory = Directory.CreateDirectory(Path.Join(_temp.FullName, $"d{index % 4}", $"e{index}"));
            File.WriteAllBytes(Path.Join(directory.FullName, "data.bin"), new byte[index * 100]);
            expected += index * 100;
        }

        var scanner = new DiskScanner(new ManagedFileSystemEnumerator(), new FakeVolumeService(), TimeProvider.System);
        using var session = scanner.Start(new ScanRequest([_temp.FullName]));
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());

        var root = session.Roots[0];
        Assert.Equal(expected, root.Size);
        Assert.Equal(20, root.FileCount);
        Assert.Equal(24, root.DirectoryCount);
    }

    /// <summary>
    /// An unreadable directory is reported as access denied.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Scan_ReportsUnreadableDirectories()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Permission bits are a Unix concept.");
        Assert.SkipWhen(Environment.UserName == "root", "The superuser can read any directory.");
        var locked = Directory.CreateDirectory(Path.Join(_temp.FullName, "locked"));
        File.WriteAllText(Path.Join(locked.FullName, "secret.txt"), "x");
        File.SetUnixFileMode(locked.FullName, UnixFileMode.None);
        try
        {
            var scanner = new DiskScanner(new ManagedFileSystemEnumerator(), new FakeVolumeService(), TimeProvider.System);
            using var session = scanner.Start(new ScanRequest([_temp.FullName]));
            await session.WaitAsync();

            var node = Assert.IsType<DirectoryNode>(session.Roots[0].Children.Single(), exactMatch: false);
            Assert.Equal(ScanErrorKind.AccessDenied, node.Error?.Kind);
        }
        finally
        {
            File.SetUnixFileMode(locked.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _temp.Delete(recursive: true);
}
