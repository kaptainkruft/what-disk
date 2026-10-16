using WhatDisk.Charting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Charting;

/// <summary>
/// Tests the totalling of file sizes by type.
/// </summary>
public sealed class FileTypeAggregatorTests
{
    /// <summary>
    /// Files throughout the subtree are totalled by extension, ignoring case; links and placeholders are ignored.
    /// </summary>
    [Fact]
    public void Aggregate_TotalsFilesByExtensionThroughoutTheSubtree()
    {
        var root = TestTree.Root();
        var photos = TestTree.Dir(root, "photos");
        TestTree.Publish(root, photos, TestTree.File(root, "readme", 7), TestTree.File(root, "notes.TXT", 3),
            new FileNode("link.txt", root, NodeKind.SymbolicLink, 99, 99, DateTime.MinValue, EntryAttributes.None, false),
            new PlaceholderNode("mnt", root, SkipReason.SeparateVolume, null, null));
        TestTree.Publish(photos, TestTree.File(photos, "a.JPG", 100), TestTree.File(photos, "b.jpg", 50), TestTree.File(photos, "c.txt", 2));

        var totals = new FileTypeAggregator(new DefaultFileTypeProvider()).Aggregate([root], TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, long> { [".jpg"] = 150, [".txt"] = 5, [FileTypes.NoExtension] = 7 }, totals);
    }

    /// <summary>
    /// A directory the platform calls a package counts as one item of the package's type.
    /// </summary>
    [Fact]
    public void Aggregate_CountsPackagesAsOneType()
    {
        var root = TestTree.Root();
        var app = TestTree.Dir(root, "Safari.app");
        TestTree.Publish(root, app);
        TestTree.Publish(app, TestTree.File(app, "Safari", 400), TestTree.File(app, "Info.plist", 1));

        var totals = new FileTypeAggregator(new AppPackages()).Aggregate([root], TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, long> { [".app"] = 401 }, totals);
    }

    /// <summary>
    /// Totals of large completed directories are cached and reused; incomplete ones are always walked again.
    /// </summary>
    [Fact]
    public void Aggregate_ReusesTotalsOfCompletedDirectories()
    {
        var root = TestTree.Root();
        var big = TestTree.Dir(root, "big");
        TestTree.Publish(root, big);
        TestTree.Publish(big, [.. Enumerable.Range(0, FileTypeAggregator.CacheThreshold).Select(index => TestTree.File(big, $"f{index}.bin", 1))]);
        big.CompleteOne();
        var aggregator = new FileTypeAggregator(new DefaultFileTypeProvider());
        Assert.Equal(FileTypeAggregator.CacheThreshold, aggregator.Aggregate([root], TestContext.Current.CancellationToken)[".bin"]);

        // Changing the published children behind the cache's back proves the cached totals are used.
        big.PublishChildren([]);

        Assert.Equal(FileTypeAggregator.CacheThreshold, aggregator.Aggregate([root], TestContext.Current.CancellationToken)[".bin"]);
    }

    /// <summary>
    /// File type keys are lower-case extensions; names without a usable extension share one key.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="expected">The expected key.</param>
    [Theory]
    [InlineData("photo.JPG", ".jpg")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("Makefile", "")]
    [InlineData(".profile", "")]
    [InlineData("trailing.", "")]
    [InlineData("odd.averyveryverylongextension", "")]
    public void FileTypes_FromName_UsesTheExtension(string name, string expected) =>
        Assert.Equal(expected, FileTypes.FromName(name));

    /// <summary>
    /// Labels and default descriptions read naturally.
    /// </summary>
    [Fact]
    public void FileTypes_LabelsAndDescriptionsReadNaturally()
    {
        Assert.Equal(".pdf", FileTypes.Label(".pdf"));
        Assert.Equal("No extension", FileTypes.Label(FileTypes.NoExtension));
        Assert.Equal("PDF file", new DefaultFileTypeProvider().Describe(".pdf"));
        Assert.Equal("Files without an extension", new DefaultFileTypeProvider().Describe(FileTypes.NoExtension));
    }

    /// <summary>
    /// A provider that treats <c>.app</c> directories as packages.
    /// </summary>
    private sealed class AppPackages : DefaultFileTypeProvider
    {
        /// <inheritdoc/>
        public override string? GetPackageType(DirectoryNode directory) =>
            directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? ".app" : null;
    }
}
