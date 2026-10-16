using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.ViewModels;

/// <summary>
/// The selection details pane: everything known about the selected item.
/// </summary>
/// <param name="sizes">Formats byte counts.</param>
/// <param name="platform">Supplies platform wording.</param>
public sealed partial class SelectionDetailsViewModel(ByteSizeFormatter sizes, PlatformCapabilities platform) : ObservableObject
{
    /// <summary>Formats byte counts.</summary>
    private readonly ByteSizeFormatter _sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));

    /// <summary>Supplies platform wording.</summary>
    private readonly PlatformCapabilities _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    /// <summary>
    /// Gets the labelled values describing the selection.
    /// </summary>
    public ObservableCollection<DetailPropertyViewModel> Properties { get; } = [];

    /// <summary>
    /// Gets the selected node.
    /// </summary>
    [ObservableProperty]
    public partial FileSystemNode? Node { get; private set; }

    /// <summary>
    /// Gets the selected item's name.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a description of the kind of item selected.
    /// </summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether an item is selected.
    /// </summary>
    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    /// <summary>
    /// Shows the details of a node, or clears the pane.
    /// </summary>
    /// <param name="node">The selected node, or <see langword="null"/>.</param>
    /// <param name="percentOfParent">The node's share of its parent, from 0 to 1.</param>
    /// <param name="isScanRunning">Whether the scan is still running.</param>
    public void Show(FileSystemNode? node, double percentOfParent, bool isScanRunning)
    {
        Node = node;
        HasSelection = node is not null;
        if (node is null)
        {
            Title = string.Empty;
            Subtitle = string.Empty;
            Properties.Clear();
            return;
        }

        Title = node.Name;
        Subtitle = DescribeKind(node);
        Apply(Describe(node, percentOfParent, isScanRunning));
    }

    /// <summary>
    /// Describes the kind of a node in words.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The description.</returns>
    private string DescribeKind(FileSystemNode node) => node switch
    {
        RootNode { IsVolumeRoot: true } => "Volume",
        PlaceholderNode { Reason: SkipReason.VirtualFileSystem or SkipReason.NetworkVolume } => "Mount point (not scanned)",
        PlaceholderNode => "Mount point",
        DirectoryNode => _platform.DirectoryNoun,
        _ => node.Kind switch
        {
            NodeKind.SymbolicLink => "Symbolic link",
            NodeKind.Special => "Special file",
            _ => "File",
        },
    };

    /// <summary>
    /// Builds the labelled values for a node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="percentOfParent">The node's share of its parent.</param>
    /// <param name="isScanRunning">Whether the scan is still running.</param>
    /// <returns>The label and value pairs, in display order.</returns>
    private List<(string Label, string Value)> Describe(FileSystemNode node, double percentOfParent, bool isScanRunning)
    {
        List<(string Label, string Value)> rows = [("Path", node.FullPath)];
        if (node is PlaceholderNode placeholder)
        {
            rows.Add(("Reason", DescribeSkip(placeholder)));
            if (placeholder.TargetVolume is { } target)
            {
                rows.Add(("Volume", DescribeVolume(target)));
            }

            return rows;
        }

        var volume = node.Volume;
        rows.Add(("Size on disk", _sizes.Format(node.Size)));
        rows.Add(("Logical size", _sizes.Format(node.LogicalSize)));
        if (volume.TotalBytes > 0)
        {
            rows.Add(("Share of volume", DisplayFormat.Percent((double)node.Size / volume.TotalBytes)));
        }

        rows.Add(("Share of parent", DisplayFormat.Percent(percentOfParent)));
        if (node is DirectoryNode directory)
        {
            rows.Add(("Files", DisplayFormat.Count(directory.FileCount)));
            rows.Add(("Folders", DisplayFormat.Count(directory.DirectoryCount)));
            rows.Add(("Status", DescribeStatus(directory, isScanRunning)));
        }
        else if (node is FileNode file)
        {
            rows.Add(("Attributes", DescribeAttributes(file)));
        }

        if (node.LastWriteTimeUtc is { } modified)
        {
            rows.Add(("Modified", DisplayFormat.Timestamp(modified)));
        }

        rows.Add(("Volume", DescribeVolume(volume)));
        if (volume.TotalBytes > 0)
        {
            rows.Add(("Volume space", $"{_sizes.Format(volume.AvailableBytes)} free of {_sizes.Format(volume.TotalBytes)}"));
        }

        if (!string.IsNullOrEmpty(volume.FileSystem))
        {
            rows.Add(("File system", volume.FileSystem));
        }

        return rows;
    }

    /// <summary>
    /// Updates <see cref="Properties"/> in place when the labels are unchanged, otherwise rebuilds it.
    /// </summary>
    /// <param name="rows">The new label and value pairs.</param>
    private void Apply(List<(string Label, string Value)> rows)
    {
        var sameLabels = rows.Count == Properties.Count
            && rows.Select(static row => row.Label).SequenceEqual(Properties.Select(static property => property.Label));
        if (sameLabels)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                Properties[index].Value = rows[index].Value;
            }

            return;
        }

        Properties.Clear();
        foreach (var (label, value) in rows)
        {
            Properties.Add(new DetailPropertyViewModel(label, value));
        }
    }

    /// <summary>
    /// Describes a volume by name and mount point.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <returns>The description.</returns>
    private static string DescribeVolume(VolumeInfo volume) =>
        string.Equals(volume.Name, volume.MountPoint, StringComparison.Ordinal) ? volume.Name : $"{volume.Name} ({volume.MountPoint})";

    /// <summary>
    /// Describes the scan state of a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="isScanRunning">Whether the scan is still running.</param>
    /// <returns>The description.</returns>
    private static string DescribeStatus(DirectoryNode directory, bool isScanRunning) => directory switch
    {
        { Error: { } error } => $"Could not be read completely: {error.Message}",
        { IsComplete: true } => "Complete",
        _ when isScanRunning => "Scanning…",
        _ => "Incomplete – the scan was stopped",
    };

    /// <summary>
    /// Explains why a directory was not traversed in place.
    /// </summary>
    /// <param name="placeholder">The placeholder.</param>
    /// <returns>The explanation.</returns>
    private static string DescribeSkip(PlaceholderNode placeholder) => placeholder.Reason switch
    {
        SkipReason.SeparateVolume when placeholder.LinkedRoot is { } root => $"Another volume is mounted here; it is listed separately as “{root.Name}”.",
        SkipReason.SeparateVolume => "Another volume is mounted here.",
        SkipReason.CountedElsewhere => "This location shows data that is already counted elsewhere in the scan.",
        SkipReason.VirtualFileSystem => "A virtual file system is mounted here. It does not hold stored data, so it was not scanned.",
        SkipReason.NetworkVolume => "A network volume is mounted here. Select it directly to scan it.",
        _ => "Not scanned.",
    };

    /// <summary>
    /// Lists the notable attributes of a file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A comma-separated list, or "None".</returns>
    private static string DescribeAttributes(FileNode file)
    {
        List<string> parts = [];
        if (file.IsHardLinkDuplicate)
        {
            parts.Add("hard link (counted once elsewhere)");
        }

        var attributes = file.Attributes;
        if (attributes.HasFlag(EntryAttributes.Hidden))
        {
            parts.Add("hidden");
        }

        if (attributes.HasFlag(EntryAttributes.System))
        {
            parts.Add("system");
        }

        if (attributes.HasFlag(EntryAttributes.ReadOnly))
        {
            parts.Add("read-only");
        }

        if (attributes.HasFlag(EntryAttributes.Compressed))
        {
            parts.Add("compressed");
        }

        if (attributes.HasFlag(EntryAttributes.Sparse))
        {
            parts.Add("sparse");
        }

        if (attributes.HasFlag(EntryAttributes.Offline))
        {
            parts.Add("stored in the cloud");
        }

        return parts.Count == 0 ? "None" : string.Join(", ", parts);
    }
}
