using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WhatDisk.Scanning;

namespace WhatDisk.Duplicates;

/// <summary>
/// A size-only estimate of the space duplicates take in part of the scan tree: among files longer than a minimum,
/// files of the same length are counted as potential copies, and the space beyond one copy of each length is
/// potentially wasted. No file is read, so files of the same length with different contents count too; Tools › Find
/// Duplicates compares contents.
/// </summary>
/// <param name="WastedBytes">The space the files of each group take beyond the smallest one, summed over the groups.</param>
/// <param name="Groups">The number of lengths shared by two or more files.</param>
/// <param name="Files">The number of files in those groups.</param>
public readonly record struct PotentialDuplicateEstimate(long WastedBytes, int Groups, int Files);

/// <summary>
/// Groups the files of part of the scan tree by length to estimate the space duplicates take (see
/// <see cref="PotentialDuplicateEstimate"/>). Like the file-type totals, it runs on a background thread, even while the
/// scan is running, and caches the groups of large completed directories, so only the parts still being read are
/// walked again.
/// </summary>
/// <remarks>
/// Only regular files longer than the minimum count. Extra hard links the scan recognised, cloud-only files and empty
/// files are left out, as the duplicate search leaves them out. Space is counted on disk (allocated size), as the
/// tree's Size column counts it.
/// </remarks>
/// <param name="minimumLength">Files must be longer than this many bytes to count.</param>
public sealed class PotentialDuplicateAggregator(long minimumLength)
{
    /// <summary>
    /// The number of files beneath a completed directory from which its groups are cached.
    /// </summary>
    public const int CacheThreshold = 256;

    /// <summary>The groups of completed directories, released with the scan tree.</summary>
    private readonly ConditionalWeakTable<DirectoryNode, Dictionary<long, Group>> _completed = [];

    /// <summary>
    /// Gets the length a file must exceed to count, in bytes.
    /// </summary>
    public long MinimumLength { get; } = minimumLength >= 0 ? minimumLength : throw new ArgumentOutOfRangeException(nameof(minimumLength));

    /// <summary>
    /// Estimates the space duplicates take in and beneath the given nodes. Safe to call from a background thread while
    /// the scan is running.
    /// </summary>
    /// <param name="nodes">The nodes to include.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The estimate.</returns>
    public PotentialDuplicateEstimate Estimate(IEnumerable<FileSystemNode> nodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var groups = new Dictionary<long, Group>();
        foreach (var node in nodes)
        {
            Add(node, groups, cancellationToken);
        }

        long wasted = 0;
        int sets = 0, files = 0;
        foreach (var group in groups.Values)
        {
            if (group.Count > 1)
            {
                wasted += group.Total - group.Smallest;
                sets++;
                files += group.Count;
            }
        }

        return new PotentialDuplicateEstimate(wasted, sets, files);
    }

    /// <summary>
    /// Forgets the cached groups of a directory and its ancestors, after items beneath it were added or removed.
    /// </summary>
    /// <param name="directory">The directory whose contents changed.</param>
    public void Invalidate(DirectoryNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        for (DirectoryNode? current = directory; current is not null; current = current.Parent)
        {
            _completed.Remove(current);
        }
    }

    /// <summary>
    /// Adds one file's length and size to the groups.
    /// </summary>
    /// <param name="groups">The groups by length.</param>
    /// <param name="length">The file's length.</param>
    /// <param name="group">The file (or files) to add.</param>
    private static void Accumulate(Dictionary<long, Group> groups, long length, Group group)
    {
        ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, length, out var exists);
        existing = exists
            ? new Group(existing.Count + group.Count, existing.Total + group.Total, Math.Min(existing.Smallest, group.Smallest))
            : group;
    }

    /// <summary>
    /// Adds one set of groups to another.
    /// </summary>
    /// <param name="source">The groups to add.</param>
    /// <param name="target">The groups to add to.</param>
    private static void Merge(Dictionary<long, Group> source, Dictionary<long, Group> target)
    {
        foreach (var (length, group) in source)
        {
            Accumulate(target, length, group);
        }
    }

    /// <summary>
    /// Adds one node to the groups.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    private void Add(FileSystemNode node, Dictionary<long, Group> groups, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case FileNode { Kind: NodeKind.File, IsHardLinkDuplicate: false } file
                when file.LogicalSize > MinimumLength && (file.Attributes & EntryAttributes.Offline) == 0:
                Accumulate(groups, file.LogicalSize, new Group(1, file.Size, file.Size));
                break;
            case DirectoryNode directory:
                AddDirectory(directory, groups, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Adds a directory's contents to the groups, using or filling the cache for large completed directories.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    private void AddDirectory(DirectoryNode directory, Dictionary<long, Group> groups, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed.TryGetValue(directory, out var cached))
        {
            Merge(cached, groups);
            return;
        }

        if (directory.IsComplete && directory.FileCount >= CacheThreshold)
        {
            var own = new Dictionary<long, Group>();
            foreach (var child in directory.Children)
            {
                Add(child, own, cancellationToken);
            }

            _completed.AddOrUpdate(directory, own);
            Merge(own, groups);
            return;
        }

        foreach (var child in directory.Children)
        {
            Add(child, groups, cancellationToken);
        }
    }

    /// <summary>
    /// The files of one length.
    /// </summary>
    /// <param name="Count">The number of files.</param>
    /// <param name="Total">Their sizes on disk, summed.</param>
    /// <param name="Smallest">The smallest size on disk among them: the copy that would be kept.</param>
    private readonly record struct Group(int Count, long Total, long Smallest);
}
