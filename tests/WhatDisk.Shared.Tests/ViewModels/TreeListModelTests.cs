using System.Collections.Specialized;
using System.Globalization;
using WhatDisk.Formatting;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the flattened, sorted tree rows.
/// </summary>
public sealed class TreeListModelTests
{
    /// <summary>The model under test.</summary>
    private readonly TreeListModel _model = new(new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));

    /// <summary>The root of the test tree.</summary>
    private readonly RootNode _root = TestTree.Root();

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeListModelTests"/> class with a small tree:
    /// root { small/ (10 bytes), large/ (300 bytes), file.bin (50 bytes) }.
    /// </summary>
    public TreeListModelTests()
    {
        var small = TestTree.Dir(_root, "small");
        var large = TestTree.Dir(_root, "large");
        TestTree.Publish(_root, small, large, TestTree.File(_root, "file.bin", 50));
        TestTree.Publish(small, TestTree.File(small, "s.txt", 10));
        TestTree.Publish(large, TestTree.File(large, "l1.bin", 200), TestTree.File(large, "l2.bin", 100));
    }

    /// <summary>
    /// Expanding a row shows its children after it, largest first.
    /// </summary>
    [Fact]
    public void Expand_ShowsChildrenLargestFirst()
    {
        _model.SetRoots([_root]);
        _model.RootRows[0].IsExpanded = true;

        Assert.Equal(["/test", "large", "file.bin", "small"], Names());
        Assert.Equal(1, _model.Rows[1].Depth);
    }

    /// <summary>
    /// Collapsing a row hides all of its descendants but remembers their expansion.
    /// </summary>
    [Fact]
    public void Collapse_HidesDescendantsAndRemembersTheirExpansion()
    {
        _model.SetRoots([_root]);
        var root = _model.RootRows[0];
        root.IsExpanded = true;
        _model.Rows[1].IsExpanded = true;
        Assert.Equal(6, _model.Rows.Count);

        root.IsExpanded = false;
        Assert.Equal(["/test"], Names());

        root.IsExpanded = true;
        Assert.Equal(["/test", "large", "l1.bin", "l2.bin", "file.bin", "small"], Names());
    }

    /// <summary>
    /// Values are formatted, and percentages are relative to the volume and to the parent.
    /// </summary>
    [Fact]
    public void Refresh_ComputesPercentagesAndTexts()
    {
        _model.SetRoots([_root]);
        _model.RootRows[0].IsExpanded = true;
        var large = _model.Rows[1];
        var file = _model.Rows[2];

        Assert.Equal(0.3, large.PercentOfDisk, precision: 9);
        Assert.Equal(300d / 360, large.PercentOfParent, precision: 9);
        Assert.Equal("300 bytes", large.SizeText);
        Assert.Equal("30.0%", large.PercentOfDiskText);
        Assert.Equal("2", large.FilesText);
        Assert.Equal("0", large.FoldersText);
        Assert.Equal(string.Empty, file.FilesText);
        Assert.Equal(1, _model.RootRows[0].PercentOfParent);
    }

    /// <summary>
    /// When the scan makes a sibling larger, refreshing moves it with a minimal change.
    /// </summary>
    [Fact]
    public void Refresh_ReordersSiblingsAsTheirSizesChange()
    {
        _model.SetRoots([_root]);
        _model.RootRows[0].IsExpanded = true;
        var changes = new List<NotifyCollectionChangedAction>();
        _model.Rows.CollectionChanged += (_, e) => changes.Add(e.Action);

        TestTree.Grow((DirectoryNode)_root.Children[0], 1000);
        _model.Refresh();

        Assert.Equal(["/test", "small", "large", "file.bin"], Names());
        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add], changes);
    }

    /// <summary>
    /// Children read after a directory was expanded appear at the next refresh.
    /// </summary>
    [Fact]
    public void Refresh_ShowsChildrenPublishedAfterExpansion()
    {
        var root = TestTree.Root("/late");
        _model.SetRoots([root]);
        _model.RootRows[0].IsExpanded = true;
        Assert.Single(_model.Rows);
        Assert.True(_model.RootRows[0].IsExpandable);

        TestTree.Publish(root, TestTree.File(root, "arrived.txt", 5));
        _model.Refresh();

        Assert.Equal(["/late", "arrived.txt"], Names());
    }

    /// <summary>
    /// An empty directory is not expandable once it has been read.
    /// </summary>
    [Fact]
    public void Refresh_MarksEmptyDirectoriesAsNotExpandable()
    {
        var root = TestTree.Root("/empty");
        _model.SetRoots([root]);
        Assert.True(_model.RootRows[0].IsExpandable);

        TestTree.Publish(root);
        _model.Refresh();

        Assert.False(_model.RootRows[0].IsExpandable);
    }

    /// <summary>
    /// Sorting by name orders siblings alphabetically; toggling reverses.
    /// </summary>
    [Fact]
    public void SortOrder_CanOrderByName()
    {
        _model.SetRoots([_root]);
        _model.RootRows[0].IsExpanded = true;

        _model.SortOrder = _model.SortOrder.Toggle(TreeSortColumn.Name);
        _model.Refresh();
        Assert.Equal(["/test", "file.bin", "large", "small"], Names());

        _model.SortOrder = _model.SortOrder.Toggle(TreeSortColumn.Name);
        _model.Refresh();
        Assert.Equal(["/test", "small", "large", "file.bin"], Names());
    }

    /// <summary>
    /// Revealing a node expands its ancestors and returns its row.
    /// </summary>
    [Fact]
    public void Reveal_ExpandsAncestors()
    {
        _model.SetRoots([_root]);
        var target = ((DirectoryNode)_root.Children[1]).Children[1];

        var row = _model.Reveal(target);

        Assert.NotNull(row);
        Assert.Same(target, row.Node);
        Assert.Contains(row, _model.Rows);
        Assert.Same(row, _model.FindVisible(target));
    }

    /// <summary>
    /// Reconciliation events surround every change to the rows.
    /// </summary>
    [Fact]
    public void Refresh_RaisesReconciliationEvents()
    {
        var events = new List<string>();
        _model.Reconciling += (_, _) => events.Add("start");
        _model.Reconciled += (_, _) => events.Add("end");

        _model.SetRoots([_root]);

        Assert.Equal(["start", "end"], events);
    }

    /// <summary>
    /// Several top-level roots share 100% of the "% of parent" column.
    /// </summary>
    [Fact]
    public void SetRoots_SplitsPercentOfParentAcrossRoots()
    {
        var other = TestTree.Root("/other");
        TestTree.Publish(other, TestTree.File(other, "x", 40));

        _model.SetRoots([_root, other]);

        Assert.Equal(400, _model.RootsTotal);
        Assert.Equal(0.9, _model.RootRows[0].PercentOfParent, precision: 9);
        Assert.Equal(0.1, _model.RootRows[1].PercentOfParent, precision: 9);
    }

    /// <summary>
    /// Clearing removes every row.
    /// </summary>
    [Fact]
    public void Clear_RemovesEveryRow()
    {
        _model.SetRoots([_root]);
        _model.Clear();

        Assert.Empty(_model.Rows);
        Assert.Empty(_model.RootRows);
    }

    /// <summary>
    /// Gets the names of the visible rows.
    /// </summary>
    /// <returns>The names in display order.</returns>
    private string[] Names() => [.. _model.Rows.Select(static row => row.Name)];
}
