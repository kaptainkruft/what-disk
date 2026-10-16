using Avalonia.Collections;
using WhatDisk.Formatting;
using WhatDisk.Scanning;

namespace WhatDisk.ViewModels;

/// <summary>
/// The flattened, sorted list of visible tree rows. Expanded rows are followed by their children; every refresh
/// re-reads the nodes, re-sorts siblings and applies the minimal set of range changes to <see cref="Rows"/>.
/// </summary>
/// <param name="sizes">Formats byte counts.</param>
public sealed class TreeListModel(ByteSizeFormatter sizes) : ITreeRowOwner
{
    /// <summary>The visible rows, bound to the view.</summary>
    private readonly ObservableRangeList<TreeRowViewModel> _rows = [];

    /// <summary>The top-level rows, sorted.</summary>
    private readonly List<TreeRowViewModel> _rootRows = [];

    /// <summary>Formats byte counts.</summary>
    private readonly ByteSizeFormatter _sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));

    /// <summary>Orders siblings.</summary>
    private TreeRowComparer _comparer = new(TreeSortOrder.Default);

    /// <summary>Whether expansion callbacks are being ignored while rows are expanded programmatically.</summary>
    private bool _suspendExpansionCallbacks;

    /// <summary>
    /// Raised before <see cref="Rows"/> is changed by a refresh.
    /// </summary>
    public event EventHandler? Reconciling;

    /// <summary>
    /// Raised after <see cref="Rows"/> has been changed by a refresh.
    /// </summary>
    public event EventHandler? Reconciled;

    /// <summary>
    /// Gets the visible rows in display order.
    /// </summary>
    public IAvaloniaReadOnlyList<TreeRowViewModel> Rows => _rows;

    /// <summary>
    /// Gets the top-level rows in display order.
    /// </summary>
    public IReadOnlyList<TreeRowViewModel> RootRows => _rootRows;

    /// <summary>
    /// Gets or sets how siblings are ordered. Takes effect at the next <see cref="Refresh"/>.
    /// </summary>
    public TreeSortOrder SortOrder
    {
        get => _comparer.Order;
        set => _comparer = new TreeRowComparer(value);
    }

    /// <summary>
    /// Gets the total size of all top-level nodes, which is 100% for their "% of parent" column.
    /// </summary>
    public long RootsTotal { get; private set; }

    /// <summary>
    /// Replaces the set of top-level nodes, keeping the rows (and expansion state) of nodes that remain, then refreshes.
    /// </summary>
    /// <param name="roots">The top-level nodes.</param>
    public void SetRoots(IReadOnlyList<FileSystemNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var existing = _rootRows.ToDictionary<TreeRowViewModel, FileSystemNode>(static row => row.Node, ReferenceEqualityComparer.Instance);
        _rootRows.Clear();
        foreach (var root in roots)
        {
            _rootRows.Add(existing.TryGetValue(root, out var row) ? row : new TreeRowViewModel(root, null, this));
        }

        Refresh();
    }

    /// <summary>
    /// Removes every row.
    /// </summary>
    public void Clear()
    {
        _rootRows.Clear();
        RootsTotal = 0;
        Reconciling?.Invoke(this, EventArgs.Empty);
        _rows.Clear();
        Reconciled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-reads every visible node, picks up newly read children of expanded rows, re-sorts and updates
    /// <see cref="Rows"/>.
    /// </summary>
    public void Refresh()
    {
        RootsTotal = _rootRows.Sum(static row => row.Node.Size);
        foreach (var root in _rootRows)
        {
            RefreshSubtree(root, RootsTotal);
        }

        _rootRows.Sort(_comparer);
        var desired = new List<TreeRowViewModel>(_rows.Count + 16);
        foreach (var root in _rootRows)
        {
            Flatten(root, desired);
        }

        Reconciling?.Invoke(this, EventArgs.Empty);
        try
        {
            ListReconciler.Apply(_rows, desired);
        }
        finally
        {
            Reconciled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Expands every ancestor of a node so that its row becomes visible, then refreshes.
    /// </summary>
    /// <param name="node">The node to reveal.</param>
    /// <returns>The node's row, or <see langword="null"/> when the node is not part of the tree.</returns>
    public TreeRowViewModel? Reveal(FileSystemNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var chain = new Stack<FileSystemNode>(node.SelfAndAncestors);

        var row = _rootRows.Find(candidate => ReferenceEquals(candidate.Node, chain.Pop()));
        _suspendExpansionCallbacks = true;
        try
        {
            while (row is not null && chain.Count > 0)
            {
                row.IsExpanded = true;
                _ = SyncChildren(row);
                var next = chain.Pop();
                row = row.ChildRows?.Find(candidate => ReferenceEquals(candidate.Node, next));
            }
        }
        finally
        {
            _suspendExpansionCallbacks = false;
        }

        Refresh();
        return row;
    }

    /// <summary>
    /// Finds the visible row of a node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The row, or <see langword="null"/> when the node is not visible.</returns>
    public TreeRowViewModel? FindVisible(FileSystemNode node)
    {
        foreach (var row in _rows)
        {
            if (ReferenceEquals(row.Node, node))
            {
                return row;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    void ITreeRowOwner.OnExpansionChanged(TreeRowViewModel row)
    {
        if (!_suspendExpansionCallbacks)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Adds a row and its visible descendants to <paramref name="output"/> in display order.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="output">Receives the rows.</param>
    private static void Flatten(TreeRowViewModel row, List<TreeRowViewModel> output)
    {
        output.Add(row);
        if (row is { IsExpanded: true, ChildRows: { } children })
        {
            foreach (var child in children)
            {
                Flatten(child, output);
            }
        }
    }

    /// <summary>
    /// Refreshes a row and, when it is expanded, its children, re-sorting them only when a sort key changed.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="parentSize">The size used as 100% for the row's "% of parent".</param>
    /// <returns><see langword="true"/> when one of the row's own sort keys changed.</returns>
    private bool RefreshSubtree(TreeRowViewModel row, long parentSize)
    {
        var changed = row.RefreshValues(parentSize, _sizes);
        if (!row.IsExpanded)
        {
            return changed;
        }

        var childrenChanged = SyncChildren(row);
        if (row.ChildRows is not { } children)
        {
            return changed;
        }

        var size = row.Node.Size;
        foreach (var child in children)
        {
            childrenChanged |= RefreshSubtree(child, size);
        }

        if (childrenChanged || !ReferenceEquals(row.ChildrenSortedWith, _comparer))
        {
            children.Sort(_comparer);
            row.ChildrenSortedWith = _comparer;
        }

        return changed;
    }

    /// <summary>
    /// Creates rows for a node's children when they have been published since the rows were last built, reusing
    /// existing rows so that their expansion state is kept.
    /// </summary>
    /// <param name="row">The row whose children to synchronise.</param>
    /// <returns><see langword="true"/> when the child rows were rebuilt.</returns>
    private bool SyncChildren(TreeRowViewModel row)
    {
        if (row.Node is not DirectoryNode directory)
        {
            return false;
        }

        var children = directory.Children;
        if (ReferenceEquals(children, row.ChildSource))
        {
            return false;
        }

        var existing = row.ChildRows?.ToDictionary<TreeRowViewModel, FileSystemNode>(static child => child.Node, ReferenceEqualityComparer.Instance);
        var rows = new List<TreeRowViewModel>(children.Count);
        foreach (var child in children)
        {
            rows.Add(existing is not null && existing.TryGetValue(child, out var reused) ? reused : new TreeRowViewModel(child, row, this));
        }

        row.ChildRows = rows;
        row.ChildSource = children;
        return true;
    }
}
