using System.Globalization;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the selection details pane view model.
/// </summary>
public sealed class SelectionDetailsViewModelTests
{
    /// <summary>The view model under test.</summary>
    private readonly SelectionDetailsViewModel _details = new(
        new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture),
        PlatformCapabilities.Generic);

    /// <summary>
    /// A directory shows its sizes, counts, status and volume.
    /// </summary>
    [Fact]
    public void Show_DescribesADirectory()
    {
        var root = TestTree.Root();
        TestTree.Publish(root, TestTree.File(root, "a", 250));

        _details.Show(root, 1, isScanRunning: true);

        Assert.True(_details.HasSelection);
        Assert.Equal("/test", _details.Title);
        Assert.Equal("Folder", _details.Subtitle);
        var values = Values();
        Assert.Equal("250 bytes", values["Size on disk"]);
        Assert.Equal("25.0%", values["Share of volume"]);
        Assert.Equal("100.0%", values["Share of parent"]);
        Assert.Equal("1", values["Files"]);
        Assert.Equal("Scanning…", values["Status"]);
        Assert.Equal("Test Disk (/)", values["Volume"]);
        Assert.Equal("400 bytes free of 1.00 KB", values["Volume space"]);
    }

    /// <summary>
    /// A file shows its attributes and modification time.
    /// </summary>
    [Fact]
    public void Show_DescribesAFile()
    {
        var root = TestTree.Root();
        var file = new FileNode("photo.jpg", root, NodeKind.File, 4096, 3000, new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc), EntryAttributes.Hidden | EntryAttributes.Compressed, false);

        _details.Show(file, 0.5, isScanRunning: false);

        Assert.Equal("File", _details.Subtitle);
        var values = Values();
        Assert.Equal("hidden, compressed", values["Attributes"]);
        Assert.Equal("3.00 KB", values["Logical size"]);
        Assert.True(values.ContainsKey("Modified"));
        Assert.False(values.ContainsKey("Files"));
    }

    /// <summary>
    /// A placeholder explains why it was not scanned.
    /// </summary>
    [Fact]
    public void Show_ExplainsPlaceholders()
    {
        var root = TestTree.Root();
        var placeholder = new PlaceholderNode("proc", root, SkipReason.VirtualFileSystem, null, null);

        _details.Show(placeholder, 0, isScanRunning: false);

        Assert.Equal("Mount point (not scanned)", _details.Subtitle);
        Assert.Contains("virtual file system", Values()["Reason"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Refreshing the same kind of selection updates values without recreating the rows.
    /// </summary>
    [Fact]
    public void Show_UpdatesValuesInPlace()
    {
        var root = TestTree.Root();
        _details.Show(root, 1, isScanRunning: true);
        var first = _details.Properties[0];

        TestTree.Publish(root);
        root.CompleteOne();
        _details.Show(root, 1, isScanRunning: false);

        Assert.Same(first, _details.Properties[0]);
        Assert.Equal("Complete", Values()["Status"]);
    }

    /// <summary>
    /// Clearing the selection empties the pane.
    /// </summary>
    [Fact]
    public void Show_WithNothingClearsThePane()
    {
        _details.Show(TestTree.Root(), 1, isScanRunning: false);
        _details.Show(null, 0, isScanRunning: false);

        Assert.False(_details.HasSelection);
        Assert.Empty(_details.Properties);
        Assert.Null(_details.Node);
    }

    /// <summary>
    /// Gets the displayed values by label.
    /// </summary>
    /// <returns>The values.</returns>
    private Dictionary<string, string> Values() =>
        _details.Properties.ToDictionary(static property => property.Label, static property => property.Value);
}
