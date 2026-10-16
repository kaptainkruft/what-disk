namespace WhatDisk.Scanning;

/// <summary>
/// Live counters describing the progress of a scan. Updated by workers and safe to read from any thread.
/// </summary>
public sealed class ScanStatistics
{
    /// <summary>The number of files found.</summary>
    private long _files;

    /// <summary>The number of directories read.</summary>
    private long _directories;

    /// <summary>The number of bytes counted.</summary>
    private long _bytes;

    /// <summary>The number of directories that could not be read completely.</summary>
    private long _errors;

    /// <summary>The number of directories deliberately not traversed (mount points, virtual file systems).</summary>
    private long _skipped;

    /// <summary>The path of the directory most recently started by any worker.</summary>
    private string? _currentPath;

    /// <summary>
    /// Gets the number of files found so far.
    /// </summary>
    public long FileCount => Volatile.Read(ref _files);

    /// <summary>
    /// Gets the number of directories read so far.
    /// </summary>
    public long DirectoryCount => Volatile.Read(ref _directories);

    /// <summary>
    /// Gets the number of bytes counted so far.
    /// </summary>
    public long ByteCount => Volatile.Read(ref _bytes);

    /// <summary>
    /// Gets the number of directories that could not be read completely.
    /// </summary>
    public long ErrorCount => Volatile.Read(ref _errors);

    /// <summary>
    /// Gets the number of directories that were deliberately not traversed in place.
    /// </summary>
    public long SkippedCount => Volatile.Read(ref _skipped);

    /// <summary>
    /// Gets the path of the directory most recently started, for progress display.
    /// </summary>
    public string? CurrentPath => Volatile.Read(ref _currentPath);

    /// <summary>
    /// Records that a directory has been read.
    /// </summary>
    /// <param name="files">The number of files it contained.</param>
    /// <param name="bytes">The number of bytes counted for those files.</param>
    internal void RecordDirectory(long files, long bytes)
    {
        Interlocked.Increment(ref _directories);
        if (files != 0)
        {
            Interlocked.Add(ref _files, files);
        }

        if (bytes != 0)
        {
            Interlocked.Add(ref _bytes, bytes);
        }
    }

    /// <summary>
    /// Records a directory that could not be read completely.
    /// </summary>
    internal void RecordError() => Interlocked.Increment(ref _errors);

    /// <summary>
    /// Records a directory that was deliberately not traversed in place.
    /// </summary>
    internal void RecordSkipped() => Interlocked.Increment(ref _skipped);

    /// <summary>
    /// Records the directory a worker has started reading.
    /// </summary>
    /// <param name="path">The directory's path.</param>
    internal void SetCurrentPath(string path) => Volatile.Write(ref _currentPath, path);
}
