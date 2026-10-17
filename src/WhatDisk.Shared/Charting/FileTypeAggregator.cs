using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.Charting;

/// <summary>
/// Totals the sizes of files by type throughout a part of the scan tree. Directories the platform presents as single
/// items (packages) count as one item of their own type.
/// </summary>
/// <remarks>
/// The totals of a subtree never change once it is complete, so they are cached for large completed directories.
/// While a scan runs, only the parts still being read are walked again; once it has finished, charting any folder
/// merges a handful of cached totals.
/// </remarks>
/// <param name="fileTypes">Identifies packages.</param>
public sealed class FileTypeAggregator(IFileTypeProvider fileTypes)
{
    /// <summary>
    /// The number of files beneath a completed directory from which its totals are cached.
    /// </summary>
    public const int CacheThreshold = 256;

    /// <summary>The totals of completed directories, released with the scan tree.</summary>
    private readonly ConditionalWeakTable<DirectoryNode, Dictionary<string, long>> _completed = [];

    /// <summary>Identifies packages.</summary>
    private readonly IFileTypeProvider _fileTypes = fileTypes ?? throw new ArgumentNullException(nameof(fileTypes));

    /// <summary>
    /// Totals the sizes of the files in and beneath the given nodes, by type. Safe to call from a background thread
    /// while the scan is running.
    /// </summary>
    /// <param name="nodes">The nodes to include.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The total size of each type.</returns>
    public Dictionary<string, long> Aggregate(IEnumerable<FileSystemNode> nodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            Add(node, totals, cancellationToken);
        }

        return totals;
    }

    /// <summary>
    /// Forgets the cached totals of a directory and its ancestors, after items beneath it were added or removed.
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
    /// Adds a size to a type's total.
    /// </summary>
    /// <param name="totals">The totals.</param>
    /// <param name="type">The type key.</param>
    /// <param name="size">The size to add.</param>
    private static void Accumulate(Dictionary<string, long> totals, string type, long size)
    {
        if (size > 0)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(totals, type, out _) += size;
        }
    }

    /// <summary>
    /// Adds one node to the totals.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="totals">The totals.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    private void Add(FileSystemNode node, Dictionary<string, long> totals, CancellationToken cancellationToken)
    {
        switch (node)
        {
            case FileNode { Kind: NodeKind.File } file:
                Accumulate(totals, FileTypes.FromName(file.Name), file.Size);
                break;
            case RootNode root:
                AddDirectory(root, totals, cancellationToken);
                break;
            case DirectoryNode directory when _fileTypes.GetPackageType(directory) is { } package:
                Accumulate(totals, package, directory.Size);
                break;
            case DirectoryNode directory:
                AddDirectory(directory, totals, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Adds a directory's contents to the totals, using or filling the cache for large completed directories.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="totals">The totals.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    private void AddDirectory(DirectoryNode directory, Dictionary<string, long> totals, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed.TryGetValue(directory, out var cached))
        {
            Merge(cached, totals);
            return;
        }

        if (directory.IsComplete && directory.FileCount >= CacheThreshold)
        {
            var own = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var child in directory.Children)
            {
                Add(child, own, cancellationToken);
            }

            _completed.AddOrUpdate(directory, own);
            Merge(own, totals);
            return;
        }

        foreach (var child in directory.Children)
        {
            Add(child, totals, cancellationToken);
        }
    }

    /// <summary>
    /// Adds one set of totals to another.
    /// </summary>
    /// <param name="source">The totals to add.</param>
    /// <param name="target">The totals to add to.</param>
    private static void Merge(Dictionary<string, long> source, Dictionary<string, long> target)
    {
        foreach (var (type, size) in source)
        {
            Accumulate(target, type, size);
        }
    }
}
