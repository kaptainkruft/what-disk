using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Formatting;
using WhatDisk.Scanning;

namespace WhatDisk.ViewModels;

/// <summary>
/// One row of the tree: a node of the scan tree plus the values displayed for it. Values are captured by
/// <see cref="RefreshValues"/> so that sorting sees a consistent snapshot while the scan keeps updating the nodes.
/// </summary>
public sealed partial class TreeRowViewModel : ObservableObject
{
    /// <summary>
    /// The horizontal indent, in device-independent pixels, for each level of depth.
    /// </summary>
    public const double IndentPerLevel = 16;

    /// <summary>The collection that owns this row.</summary>
    private readonly ITreeRowOwner _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeRowViewModel"/> class.
    /// </summary>
    /// <param name="node">The node shown by the row.</param>
    /// <param name="parent">The row of the containing directory, or <see langword="null"/> for a top-level row.</param>
    /// <param name="owner">The collection that owns the row.</param>
    internal TreeRowViewModel(FileSystemNode node, TreeRowViewModel? parent, ITreeRowOwner owner)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(owner);
        Node = node;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        _owner = owner;
        SecondaryText = DescribeSecondary(node);
        IsExpandable = node is DirectoryNode;
        HasMeasures = node is not PlaceholderNode;
    }

    /// <summary>
    /// Gets the node shown by the row.
    /// </summary>
    public FileSystemNode Node { get; }

    /// <summary>
    /// Gets the row of the containing directory, or <see langword="null"/> for a top-level row.
    /// </summary>
    public TreeRowViewModel? Parent { get; }

    /// <summary>
    /// Gets the depth of the row; top-level rows have depth zero.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Gets the indent of the row's name, in device-independent pixels.
    /// </summary>
    public double IndentWidth => Depth * IndentPerLevel;

    /// <summary>
    /// Gets the name shown in the first column.
    /// </summary>
    public string Name => Node.Name;

    /// <summary>
    /// Gets muted text shown after the name, such as a volume's mount point or why a directory was skipped.
    /// </summary>
    public string? SecondaryText { get; }

    /// <summary>
    /// Gets the kind of node.
    /// </summary>
    public NodeKind Kind => Node.Kind;

    /// <summary>
    /// Gets the absolute path of the node.
    /// </summary>
    public string FullPath => Node.FullPath;

    /// <summary>
    /// Gets a value indicating whether the row shows sizes and percentages (placeholders do not).
    /// </summary>
    public bool HasMeasures { get; }

    /// <summary>
    /// Gets the error message for a directory that could not be read completely.
    /// </summary>
    public string? ErrorText => (Node as DirectoryNode)?.Error?.Message;

    /// <summary>
    /// Gets or sets a value indicating whether the row's children are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether items being dragged would be dropped into this row's folder.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>
    /// Gets a value indicating whether the row can have children.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpandable { get; private set; }

    /// <summary>
    /// Gets the counted size captured at the last refresh.
    /// </summary>
    [ObservableProperty]
    public partial long Size { get; private set; }

    /// <summary>
    /// Gets the number of files captured at the last refresh.
    /// </summary>
    [ObservableProperty]
    public partial long FileCount { get; private set; }

    /// <summary>
    /// Gets the number of folders captured at the last refresh.
    /// </summary>
    [ObservableProperty]
    public partial long FolderCount { get; private set; }

    /// <summary>
    /// Gets the share of the volume's capacity used by the node, from 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double PercentOfDisk { get; private set; }

    /// <summary>
    /// Gets the node's share of its parent's size, from 0 to 1.
    /// </summary>
    [ObservableProperty]
    public partial double PercentOfParent { get; private set; }

    /// <summary>
    /// Gets the formatted size.
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted number of files, or an empty string for leaves.
    /// </summary>
    [ObservableProperty]
    public partial string FilesText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted number of folders, or an empty string for leaves.
    /// </summary>
    [ObservableProperty]
    public partial string FoldersText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted share of the volume.
    /// </summary>
    [ObservableProperty]
    public partial string PercentOfDiskText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted share of the parent.
    /// </summary>
    [ObservableProperty]
    public partial string PercentOfParentText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the directory's subtree has not been completely read.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPending { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the directory could not be read completely.
    /// </summary>
    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>
    /// Gets or sets the rows of the node's children, created when the row is first expanded.
    /// </summary>
    internal List<TreeRowViewModel>? ChildRows { get; set; }

    /// <summary>
    /// Gets or sets the children list from which <see cref="ChildRows"/> was built.
    /// </summary>
    internal IReadOnlyList<FileSystemNode>? ChildSource { get; set; }

    /// <summary>
    /// Gets or sets the comparer the child rows were last sorted with.
    /// </summary>
    internal IComparer<TreeRowViewModel>? ChildrenSortedWith { get; set; }

    /// <summary>
    /// Forgets the formatted size, so that the next <see cref="RefreshValues"/> formats it again.
    /// </summary>
    internal void InvalidateTexts() => SizeText = string.Empty;

    /// <summary>
    /// Captures the node's current values and updates the displayed texts that changed.
    /// </summary>
    /// <param name="parentSize">The size used as 100% for <see cref="PercentOfParent"/>.</param>
    /// <param name="sizes">Formats byte counts.</param>
    /// <returns><see langword="true"/> when a value used for sorting (size, files or folders) changed.</returns>
    internal bool RefreshValues(long parentSize, ByteSizeFormatter sizes)
    {
        var node = Node;
        if (!HasMeasures)
        {
            SizeText = "—";
            return false;
        }

        var previousKey = (Size, FileCount, FolderCount);

        var size = node.Size;
        if (size != Size || SizeText.Length == 0)
        {
            Size = size;
            SizeText = sizes.Format(size);
        }

        var capacity = node.Volume.TotalBytes;
        PercentOfDisk = capacity > 0 ? Math.Clamp((double)size / capacity, 0, 1) : 0;
        PercentOfParent = parentSize > 0 ? Math.Clamp((double)size / parentSize, 0, 1) : 0;
        PercentOfDiskText = capacity > 0 ? DisplayFormat.Percent(PercentOfDisk) : "—";
        PercentOfParentText = DisplayFormat.Percent(PercentOfParent);

        if (node is not DirectoryNode directory)
        {
            FileCount = node.FileCount;
            return previousKey != (Size, FileCount, FolderCount);
        }

        var files = directory.FileCount;
        if (files != FileCount || FilesText.Length == 0)
        {
            FileCount = files;
            FilesText = DisplayFormat.Count(files);
        }

        var folders = directory.DirectoryCount;
        if (folders != FolderCount || FoldersText.Length == 0)
        {
            FolderCount = folders;
            FoldersText = DisplayFormat.Count(folders);
        }

        IsPending = !directory.IsComplete;
        HasError = directory.Error is not null;
        IsExpandable = !directory.IsEnumerated || directory.Children.Count > 0;
        return previousKey != (Size, FileCount, FolderCount);
    }

    /// <summary>
    /// Notifies the owner when the row is expanded or collapsed.
    /// </summary>
    /// <param name="value">The new expansion state.</param>
    partial void OnIsExpandedChanged(bool value) => _owner.OnExpansionChanged(this);

    /// <summary>
    /// Builds the muted text shown after a node's name.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The text, or <see langword="null"/> when there is nothing to add.</returns>
    private static string? DescribeSecondary(FileSystemNode node) => node switch
    {
        RootNode { IsVolumeRoot: true } root when !string.Equals(root.RootPath, root.Name, StringComparison.Ordinal) => root.RootPath,
        RootNode { IsVolumeRoot: false } root when root.Volume.TotalBytes > 0 => $"on {root.Volume.Name}",
        PlaceholderNode { Reason: SkipReason.SeparateVolume, LinkedRoot: { } linked } => $"separate volume – listed as “{linked.Name}”",
        PlaceholderNode { Reason: SkipReason.SeparateVolume } => "separate volume",
        PlaceholderNode { Reason: SkipReason.CountedElsewhere } => "counted elsewhere in this scan",
        PlaceholderNode { Reason: SkipReason.VirtualFileSystem } => "not scanned – virtual file system",
        PlaceholderNode { Reason: SkipReason.NetworkVolume } => "not scanned – network volume",
        FileNode { IsHardLinkDuplicate: true } => "hard link – counted once elsewhere",
        _ => null,
    };
}
