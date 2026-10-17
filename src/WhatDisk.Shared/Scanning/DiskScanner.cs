using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// Starts multi-threaded scans using the platform's directory enumerator and volume information.
/// </summary>
/// <param name="enumerator">Reads directories.</param>
/// <param name="volumes">Describes the mounted volumes.</param>
/// <param name="timeProvider">Supplies the current time.</param>
public sealed class DiskScanner(IFileSystemEnumerator enumerator, IVolumeService volumes, TimeProvider timeProvider) : IDiskScanner
{
    /// <inheritdoc/>
    public ScanSession Start(ScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mountTable = volumes.GetMountTable();
        var roots = ScanPlanner.CreateRoots(request, mountTable, enumerator, volumes);
        var session = new ScanSession(request, roots, mountTable, enumerator, timeProvider);
        session.Start();
        return session;
    }

    /// <inheritdoc/>
    public ScanSession StartInto(DirectoryNode parent, string name, IEnumerable<RootNode> knownRoots)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(knownRoots);
        var path = Path.Join(parent.FullPath, name);
        var directory = new DirectoryNode(name, parent, parent.Volume, enumerator.GetIdentity(path), null) { IsCompletionBoundary = true };
        parent.InsertChild(directory);
        var session = new ScanSession(new ScanRequest([path]), [], volumes.GetMountTable(), enumerator, timeProvider);
        session.StartInto(directory, path, knownRoots);
        return session;
    }
}
