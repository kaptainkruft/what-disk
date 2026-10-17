namespace WhatDisk.Scanning;

/// <summary>
/// Starts scans.
/// </summary>
public interface IDiskScanner
{
    /// <summary>
    /// Starts scanning in the background.
    /// </summary>
    /// <param name="request">What to scan.</param>
    /// <returns>The running scan session.</returns>
    ScanSession Start(ScanRequest request);

    /// <summary>
    /// Scans a directory into an existing, finished tree: a new node is added to <paramref name="parent"/> and
    /// filled in the background, its totals flowing up to every ancestor. Used after copying or moving a folder and to
    /// rescan one.
    /// </summary>
    /// <param name="parent">The directory that contains the one to scan.</param>
    /// <param name="name">The name of the directory to scan.</param>
    /// <param name="knownRoots">The roots of the tree, to which volumes mounted inside the directory are linked.</param>
    /// <returns>The running session; <see cref="ScanSession.GraftedDirectory"/> is the new node.</returns>
    ScanSession StartInto(DirectoryNode parent, string name, IEnumerable<RootNode> knownRoots);
}
