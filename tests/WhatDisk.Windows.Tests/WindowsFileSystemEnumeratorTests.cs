using System.Runtime.Versioning;
using WhatDisk.Scanning;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Compares the Windows enumerator with System.IO on real directories.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemEnumeratorTests : IDisposable
{
    /// <summary>The directory the tests work in.</summary>
    private readonly TempTree _tree;

    /// <summary>The enumerator under test.</summary>
    private readonly WindowsFileSystemEnumerator _enumerator = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFileSystemEnumeratorTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsFileSystemEnumeratorTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
        _tree = new TempTree();
    }

    /// <summary>
    /// Names, types, sizes, attributes and times match System.IO, allocated sizes are reported, and every entry has
    /// the directory's volume as its device.
    /// </summary>
    [Fact]
    public void ReadDirectory_MatchesSystemIO()
    {
        _tree.Write("small.txt", 10);
        _tree.Write("large.bin", 200_000);
        _tree.Write("Folder/inner.txt", 1);
        var hidden = _tree.Write(".hidden", 3);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var entries = new List<FileSystemEntryInfo>();

        var identity = _enumerator.ReadDirectory(_tree.Root, entries, TestContext.Current.CancellationToken);

        Assert.True(identity.IsKnown);
        Assert.Equal(_enumerator.GetIdentity(_tree.Root), identity);
        Assert.Equal([".hidden", "Folder", "large.bin", "small.txt"], entries.Select(static entry => entry.Name).Order(StringComparer.Ordinal));
        foreach (var entry in entries)
        {
            var info = new FileInfo(_tree.PathOf(entry.Name));
            if (entry.Type == EntryType.File)
            {
                Assert.Equal(info.Length, entry.LogicalSize);
                Assert.True(entry.AllocatedSize >= 0);
                Assert.Equal(info.LastWriteTimeUtc, entry.LastWriteTimeUtc);
            }
            else
            {
                Assert.Equal(EntryType.Directory, entry.Type);
            }

            Assert.Equal(identity.Device, entry.Identity.Device);
        }

        var large = entries.Single(static entry => entry.Name == "large.bin");
        Assert.True(large.AllocatedSize >= 200_000, $"Allocated {large.AllocatedSize} bytes for a 200,000-byte file.");
        Assert.True(entries.Single(static entry => entry.Name == ".hidden").Attributes.HasFlag(EntryAttributes.Hidden));
    }

    /// <summary>
    /// A directory larger than one 64 KB listing batch is read completely.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReadsEveryBatch()
    {
        for (var index = 0; index < 1500; index++)
        {
            File.WriteAllBytes(_tree.PathOf($"file-{index:D4}-with-a-name-long-enough-to-fill-the-buffer.dat"), []);
        }

        var entries = new List<FileSystemEntryInfo>();
        _enumerator.ReadDirectory(_tree.Root, entries, TestContext.Current.CancellationToken);

        Assert.Equal(1500, entries.Count);
        Assert.Equal(1500, entries.Select(static entry => entry.Name).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A junction is a link (never followed), and so is a symbolic link where one can be created.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReportsJunctionsAndSymbolicLinksAsLinks()
    {
        var target = Directory.CreateDirectory(_tree.PathOf("target")).FullName;
        _tree.Write("target/file.txt", 5);
        _tree.Junction("junction", target);
        var canSymlink = TryCreateSymbolicLink(_tree.PathOf("symlink.txt"), _tree.PathOf("target/file.txt"));
        var entries = new List<FileSystemEntryInfo>();

        _enumerator.ReadDirectory(_tree.Root, entries, TestContext.Current.CancellationToken);

        var junction = entries.Single(static entry => entry.Name == "junction");
        Assert.Equal(EntryType.SymbolicLink, junction.Type);
        Assert.True(junction.Attributes.HasFlag(EntryAttributes.ReparsePoint));
        Assert.False(junction.Attributes.HasFlag(EntryAttributes.MountPoint));
        if (canSymlink)
        {
            Assert.Equal(EntryType.SymbolicLink, entries.Single(static entry => entry.Name == "symlink.txt").Type);
        }
    }

    /// <summary>
    /// Reading a missing directory or a file fails with a classified error.
    /// </summary>
    [Fact]
    public void ReadDirectory_ClassifiesFailures()
    {
        var file = _tree.Write("file.txt", 1);

        var missing = Assert.Throws<FileSystemException>(() => _enumerator.ReadDirectory(_tree.PathOf("missing"), [], TestContext.Current.CancellationToken));
        var notDirectory = Assert.Throws<FileSystemException>(() => _enumerator.ReadDirectory(file, [], TestContext.Current.CancellationToken));

        Assert.Equal(ScanErrorKind.NotFound, missing.Kind);
        Assert.Contains(_tree.PathOf("missing"), missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0, notDirectory.NativeErrorCode);
    }

    /// <summary>
    /// Paths longer than 260 characters are read.
    /// </summary>
    [Fact]
    public void ReadDirectory_ReadsLongPaths()
    {
        var deep = string.Join('\\', Enumerable.Repeat("a-folder-name-of-forty-characters-long-x", 8));
        _tree.Write(Path.Join(deep, "deep.txt"), 7);
        var entries = new List<FileSystemEntryInfo>();

        _enumerator.ReadDirectory(_tree.PathOf(deep), entries, TestContext.Current.CancellationToken);

        Assert.True(_tree.PathOf(deep).Length > 260);
        Assert.Equal(7, Assert.Single(entries).LogicalSize);
    }

    /// <summary>
    /// Single items are described like listing entries, with their link count.
    /// </summary>
    [Fact]
    public void GetEntry_DescribesFilesFoldersAndJunctions()
    {
        var file = _tree.Write("Folder/report.txt", 1234);
        var junction = _tree.Junction("link", _tree.PathOf("Folder"));

        var fileEntry = _enumerator.GetEntry(file);
        var folderEntry = _enumerator.GetEntry(_tree.PathOf("Folder"));
        var junctionEntry = _enumerator.GetEntry(junction);

        Assert.Equal(("report.txt", EntryType.File, 1234L, 1u), (fileEntry?.Name, fileEntry?.Type, fileEntry?.LogicalSize, fileEntry?.LinkCount));
        Assert.Equal(EntryType.Directory, folderEntry?.Type);
        Assert.Equal(EntryType.SymbolicLink, junctionEntry?.Type);
        Assert.Null(_enumerator.GetEntry(_tree.PathOf("missing")));
    }

    /// <summary>
    /// The canonical path resolves junctions, so a folder reached through one is scanned under its real path.
    /// </summary>
    [Fact]
    public void GetCanonicalPath_ResolvesJunctions()
    {
        var target = Directory.CreateDirectory(_tree.PathOf("real")).FullName;
        var junction = _tree.Junction("alias", target);

        Assert.Equal(_enumerator.GetCanonicalPath(target), _enumerator.GetCanonicalPath(junction), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// Creates a symbolic link if the account may (administrators and Developer Mode).
    /// </summary>
    /// <param name="path">The link.</param>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/> when the link was created.</returns>
    private static bool TryCreateSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
