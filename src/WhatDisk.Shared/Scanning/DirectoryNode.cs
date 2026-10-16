using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A directory in the scan tree. Its totals include everything beneath it and grow while the scan progresses; its
/// children are published once, when the directory has been read.
/// </summary>
public class DirectoryNode : FileSystemNode
{
    /// <summary>State value: the directory has not been read yet.</summary>
    private const int StatePending = 0;

    /// <summary>State value: the directory's own entries have been read.</summary>
    private const int StateEnumerated = 1;

    /// <summary>State value: the directory and every directory beneath it have been read.</summary>
    private const int StateComplete = 2;

    /// <summary>The volume the directory is stored on.</summary>
    private readonly VolumeInfo _volume;

    /// <summary>The children, published once by the worker that reads the directory.</summary>
    private FileSystemNode[]? _children;

    /// <summary>The failure that prevented the directory from being read completely, if any.</summary>
    private NodeError? _error;

    /// <summary>The counted size of everything beneath the directory.</summary>
    private long _size;

    /// <summary>The logical size of everything beneath the directory.</summary>
    private long _logicalSize;

    /// <summary>The number of files beneath the directory.</summary>
    private long _fileCount;

    /// <summary>The number of directories beneath the directory.</summary>
    private long _directoryCount;

    /// <summary>The directory itself plus the number of child directories whose subtrees are not yet complete.</summary>
    private int _pending = 1;

    /// <summary>One of <see cref="StatePending"/>, <see cref="StateEnumerated"/> or <see cref="StateComplete"/>.</summary>
    private int _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryNode"/> class.
    /// </summary>
    /// <param name="name">The directory's file name.</param>
    /// <param name="parent">The containing directory, or <see langword="null"/> for a root.</param>
    /// <param name="volume">The volume the directory is stored on.</param>
    /// <param name="identity">The directory's identity, when known.</param>
    /// <param name="lastWriteTimeUtc">When the directory was last modified, when known.</param>
    internal DirectoryNode(string name, DirectoryNode? parent, VolumeInfo volume, FileIdentity identity, DateTime? lastWriteTimeUtc)
        : base(name, parent)
    {
        ArgumentNullException.ThrowIfNull(volume);
        _volume = volume;
        Identity = identity;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.Directory;

    /// <inheritdoc/>
    public override bool IsContainer => true;

    /// <inheritdoc/>
    public override long Size => Volatile.Read(ref _size);

    /// <inheritdoc/>
    public override long LogicalSize => Volatile.Read(ref _logicalSize);

    /// <inheritdoc/>
    public override long FileCount => Volatile.Read(ref _fileCount);

    /// <inheritdoc/>
    public override long DirectoryCount => Volatile.Read(ref _directoryCount);

    /// <inheritdoc/>
    public override VolumeInfo Volume => _volume;

    /// <inheritdoc/>
    public override DateTime? LastWriteTimeUtc { get; }

    /// <summary>
    /// Gets the identity of the directory. It is replaced with the identity of the directory actually opened (which
    /// differs for firmlinks) when the directory is read.
    /// </summary>
    public FileIdentity Identity { get; internal set; }

    /// <summary>
    /// Gets the children of the directory, or an empty list until the directory has been read.
    /// </summary>
    public IReadOnlyList<FileSystemNode> Children => Volatile.Read(ref _children) ?? [];

    /// <summary>
    /// Gets a value indicating whether the directory's own entries have been read.
    /// </summary>
    public bool IsEnumerated => Volatile.Read(ref _state) >= StateEnumerated;

    /// <summary>
    /// Gets a value indicating whether the directory and everything beneath it have been read.
    /// </summary>
    public bool IsComplete => Volatile.Read(ref _state) == StateComplete;

    /// <summary>
    /// Gets the failure that prevented the directory from being read completely, if any.
    /// </summary>
    public NodeError? Error => Volatile.Read(ref _error);

    /// <summary>
    /// Records a failure to read the directory.
    /// </summary>
    /// <param name="error">The failure.</param>
    internal void SetError(NodeError error) => Volatile.Write(ref _error, error);

    /// <summary>
    /// Publishes the directory's children and marks its own entries as read. Called once, by the reading worker.
    /// </summary>
    /// <param name="children">The children; the array must not be modified afterwards.</param>
    internal void PublishChildren(FileSystemNode[] children)
    {
        Volatile.Write(ref _children, children);
        Interlocked.CompareExchange(ref _state, StateEnumerated, StatePending);
    }

    /// <summary>
    /// Adds the totals of newly read entries to this directory and every ancestor.
    /// </summary>
    /// <param name="size">Counted bytes to add.</param>
    /// <param name="logicalSize">Logical bytes to add.</param>
    /// <param name="files">Files to add.</param>
    /// <param name="directories">Directories to add.</param>
    internal void AddToTotals(long size, long logicalSize, long files, long directories)
    {
        if ((size | logicalSize | files | directories) == 0)
        {
            return;
        }

        for (var node = this; node is not null; node = node.Parent)
        {
            if (size != 0)
            {
                Interlocked.Add(ref node._size, size);
            }

            if (logicalSize != 0)
            {
                Interlocked.Add(ref node._logicalSize, logicalSize);
            }

            if (files != 0)
            {
                Interlocked.Add(ref node._fileCount, files);
            }

            if (directories != 0)
            {
                Interlocked.Add(ref node._directoryCount, directories);
            }
        }
    }

    /// <summary>
    /// Registers child directories whose subtrees must complete before this directory is complete. Must be called
    /// before the children are queued for reading.
    /// </summary>
    /// <param name="count">The number of child directories.</param>
    internal void AddPendingChildren(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _pending, count);
        }
    }

    /// <summary>
    /// Marks one outstanding unit of work (the directory itself or one child subtree) as finished, completing this
    /// directory and, transitively, its ancestors when nothing remains outstanding.
    /// </summary>
    internal void CompleteOne()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (Interlocked.Decrement(ref node._pending) != 0)
            {
                return;
            }

            Volatile.Write(ref node._state, StateComplete);
        }
    }
}
