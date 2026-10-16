using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A scan in progress (or finished). Dedicated worker threads read directories depth-first from a shared queue and
/// build the scan tree, which the user interface may read at any time.
/// </summary>
public sealed class ScanSession : IDisposable
{
    /// <summary>Reads directories.</summary>
    private readonly IFileSystemEnumerator _enumerator;

    /// <summary>Detects and resolves file system boundaries.</summary>
    private readonly VolumeBoundaryResolver _boundaries;

    /// <summary>Ensures each multiply-linked file is counted once.</summary>
    private readonly HardLinkRegistry _hardLinks = new();

    /// <summary>The directories waiting to be read.</summary>
    private readonly WorkQueue<WorkItem> _queue = new();

    /// <summary>Signals cancellation to the workers.</summary>
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Completes when the last worker exits.</summary>
    private readonly TaskCompletionSource<ScanStatus> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The individual errors recorded, up to <see cref="ScanOptions.MaxRecordedErrors"/>.</summary>
    private readonly ConcurrentQueue<ScanErrorRecord> _errors = new();

    /// <summary>Top-level roots that cover an entire volume, keyed by volume identifier.</summary>
    private readonly ConcurrentDictionary<string, RootNode> _fullRoots = new(StringComparer.Ordinal);

    /// <summary>Guards changes to <see cref="_roots"/>.</summary>
    private readonly Lock _rootsGate = new();

    /// <summary>Supplies the current time.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>How paths are compared on this platform.</summary>
    private readonly StringComparison _pathComparison;

    /// <summary>The top-level roots; replaced (never mutated) when a root is discovered.</summary>
    private RootNode[] _roots;

    /// <summary>The number of workers that have not exited.</summary>
    private int _activeWorkers;

    /// <summary>The number of errors recorded in <see cref="_errors"/>.</summary>
    private int _recordedErrors;

    /// <summary>The current <see cref="ScanStatus"/>, stored as an integer for atomic access.</summary>
    private int _status = (int)ScanStatus.Running;

    /// <summary>When the scan finished, in UTC ticks; zero while running.</summary>
    private long _completedAtTicks;

    /// <summary>The unexpected failure that stopped the scan, if any.</summary>
    private Exception? _failure;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanSession"/> class. Call <see cref="Start"/> to begin.
    /// </summary>
    /// <param name="request">What to scan.</param>
    /// <param name="roots">The initial roots, from <see cref="ScanPlanner"/>.</param>
    /// <param name="mountTable">The mount table captured when the scan started.</param>
    /// <param name="enumerator">Reads directories.</param>
    /// <param name="timeProvider">Supplies the current time.</param>
    internal ScanSession(
        ScanRequest request,
        IReadOnlyList<RootNode> roots,
        MountTable mountTable,
        IFileSystemEnumerator enumerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(mountTable);
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Request = request;
        _enumerator = enumerator;
        _timeProvider = timeProvider;
        _pathComparison = mountTable.PathComparison;
        _boundaries = new VolumeBoundaryResolver(mountTable, enumerator, request.Options);
        _roots = [.. roots];
        foreach (var root in _roots.Where(static root => root.IsVolumeRoot))
        {
            _fullRoots.TryAdd(root.Volume.Id, root);
        }

        StartedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Gets what is being scanned.
    /// </summary>
    public ScanRequest Request { get; }

    /// <summary>
    /// Gets the top-level roots. The list grows when volumes are discovered during the scan; each call returns an
    /// immutable snapshot.
    /// </summary>
    public IReadOnlyList<RootNode> Roots => Volatile.Read(ref _roots);

    /// <summary>
    /// Gets the live progress counters.
    /// </summary>
    public ScanStatistics Statistics { get; } = new();

    /// <summary>
    /// Gets the state of the scan.
    /// </summary>
    public ScanStatus Status => (ScanStatus)Volatile.Read(ref _status);

    /// <summary>
    /// Gets a value indicating whether the scan is still running.
    /// </summary>
    public bool IsRunning => Status == ScanStatus.Running;

    /// <summary>
    /// Gets when the scan started.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets when the scan finished, or <see langword="null"/> while it is running.
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _completedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Gets how long the scan has been running, or how long it took once finished.
    /// </summary>
    public TimeSpan Elapsed => (CompletedAt ?? _timeProvider.GetUtcNow()) - StartedAt;

    /// <summary>
    /// Gets a task that completes with the final status when the scan finishes. It never faults.
    /// </summary>
    public Task<ScanStatus> Completion => _completion.Task;

    /// <summary>
    /// Gets the individual errors recorded so far (bounded by <see cref="ScanOptions.MaxRecordedErrors"/>).
    /// </summary>
    public IReadOnlyCollection<ScanErrorRecord> Errors => _errors;

    /// <summary>
    /// Gets the unexpected failure that stopped the scan, if any.
    /// </summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>
    /// Gets the name of the enumerator used to read directories.
    /// </summary>
    public string EnumeratorName => _enumerator.Name;

    /// <summary>
    /// Requests that the scan stop. Results gathered so far remain available.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The scan has already finished and released its resources.
        }
    }

    /// <summary>
    /// Stops the scan if it is still running.
    /// </summary>
    public void Dispose() => Cancel();

    /// <summary>
    /// Queues the roots and starts the worker threads.
    /// </summary>
    internal void Start()
    {
        foreach (var root in _roots)
        {
            _queue.Add(new WorkItem(root, root.RootPath));
        }

        _queue.CompleteIfIdle();
        var workerCount = Request.Options.MaxDegreeOfParallelism;
        _activeWorkers = workerCount;
        for (var index = 0; index < workerCount; index++)
        {
            var thread = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = $"WhatDisk scan worker {index + 1}",
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
        }
    }

    /// <summary>
    /// The body of a worker thread: reads directories until the queue completes or the scan is cancelled.
    /// </summary>
    private void RunWorker()
    {
        var context = new WorkerContext();
        try
        {
            while (_queue.TryTake(out var item, _cancellation.Token))
            {
                // An item interrupted by cancellation is deliberately never marked done, so the queue cannot
                // complete and the scan is reported as cancelled rather than completed.
                ProcessDirectory(item, context);
                _queue.MarkDone();
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Cancellation is a normal way for a scan to end.
        }
#pragma warning disable CA1031 // A worker must never take down the process; the failure is reported through the session.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Interlocked.CompareExchange(ref _failure, exception, null);
            Cancel();
        }
        finally
        {
            if (Interlocked.Decrement(ref _activeWorkers) == 0)
            {
                Finish();
            }
        }
    }

    /// <summary>
    /// Reads one directory, creates nodes for its entries, updates the totals and queues its subdirectories.
    /// </summary>
    /// <param name="item">The directory to read.</param>
    /// <param name="context">The worker's reusable buffers.</param>
    private void ProcessDirectory(WorkItem item, WorkerContext context)
    {
        var (node, path) = item;
        var entries = context.Entries;
        entries.Clear();
        context.SubDirectories.Clear();
        Statistics.SetCurrentPath(path);

        NodeError? error = null;
        try
        {
            var actualIdentity = _enumerator.ReadDirectory(path, entries, _cancellation.Token);
            if (actualIdentity.IsKnown)
            {
                node.Identity = actualIdentity;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any failure to read one directory is recorded against it and the scan continues.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            error = ScanErrorClassifier.Classify(exception);
        }

        var children = new FileSystemNode[entries.Count];
        long filesSize = 0, filesLogicalSize = 0, fileCount = 0;
        var span = CollectionsMarshal.AsSpan(entries);
        for (var index = 0; index < span.Length; index++)
        {
            ref readonly var entry = ref span[index];
            if (entry.Type == EntryType.Directory)
            {
                children[index] = CreateDirectoryChild(node, path, in entry, context);
                continue;
            }

            var file = CreateFileChild(node, in entry);
            children[index] = file;
            filesSize += file.Size;
            filesLogicalSize += file.IsHardLinkDuplicate ? 0 : file.LogicalSize;
            fileCount++;
        }

        node.PublishChildren(children);
        if (error is not null)
        {
            node.SetError(error);
            RecordError(path, error);
        }

        var subDirectories = context.SubDirectories;
        node.AddToTotals(filesSize, filesLogicalSize, fileCount, subDirectories.Count);
        node.AddPendingChildren(subDirectories.Count);
        foreach (var (child, childPath) in subDirectories)
        {
            _queue.Add(new WorkItem(child, childPath));
        }

        Statistics.RecordDirectory(fileCount, filesSize);
        node.CompleteOne();
    }

    /// <summary>
    /// Creates the node for a directory entry, deciding whether it is traversed in place, skipped or scanned as a
    /// separate root.
    /// </summary>
    /// <param name="parent">The directory being read.</param>
    /// <param name="parentPath">The path of the directory being read.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="context">The worker's reusable buffers; traversable directories are added to it.</param>
    /// <returns>The node for the entry.</returns>
    private FileSystemNode CreateDirectoryChild(DirectoryNode parent, string parentPath, in FileSystemEntryInfo entry, WorkerContext context)
    {
        var childPath = Path.Join(parentPath, entry.Name);
        if (_boundaries.IsBoundary(in entry, parent.Identity, childPath))
        {
            var decision = _boundaries.Resolve(in entry, childPath, parent.Volume, FindFullRoot);
            switch (decision.Action)
            {
                case BoundaryAction.Skip:
                    Statistics.RecordSkipped();
                    return new PlaceholderNode(entry.Name, parent, decision.Reason, decision.Volume, decision.LinkedRoot);

                case BoundaryAction.NewRoot:
                    Statistics.RecordSkipped();
                    var root = AddDiscoveredRoot(childPath, decision.Volume!, decision.IsVolumeRoot);
                    return new PlaceholderNode(entry.Name, parent, SkipReason.SeparateVolume, decision.Volume, root);
            }
        }

        var lastWrite = entry.LastWriteTimeUtc == DateTime.MinValue ? (DateTime?)null : entry.LastWriteTimeUtc;
        var directory = new DirectoryNode(entry.Name, parent, parent.Volume, entry.Identity, lastWrite);
        context.SubDirectories.Add((directory, childPath));
        return directory;
    }

    /// <summary>
    /// Creates the node for a file, symbolic link or special file, counting additional hard links only once.
    /// </summary>
    /// <param name="parent">The directory being read.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The node for the entry.</returns>
    private FileNode CreateFileChild(DirectoryNode parent, in FileSystemEntryInfo entry)
    {
        var kind = entry.Type switch
        {
            EntryType.File => NodeKind.File,
            EntryType.SymbolicLink => NodeKind.SymbolicLink,
            _ => NodeKind.Special,
        };

        var isDuplicate = kind == NodeKind.File
            && entry.LinkCount > 1
            && entry.Identity.IsKnown
            && !_hardLinks.TryRegister(entry.Identity);

        return new FileNode(
            entry.Name,
            parent,
            kind,
            isDuplicate ? 0 : entry.EffectiveSize,
            Math.Max(0, entry.LogicalSize),
            entry.LastWriteTimeUtc,
            entry.Attributes,
            isDuplicate);
    }

    /// <summary>
    /// Finds the top-level root that covers an entire volume.
    /// </summary>
    /// <param name="volumeId">The volume identifier.</param>
    /// <returns>The root, or <see langword="null"/>.</returns>
    private RootNode? FindFullRoot(string volumeId) => _fullRoots.GetValueOrDefault(volumeId);

    /// <summary>
    /// Adds a root discovered beneath another root and queues it for scanning, unless an equivalent root exists.
    /// </summary>
    /// <param name="path">The path of the discovered directory.</param>
    /// <param name="volume">The volume it belongs to.</param>
    /// <param name="isVolumeRoot">Whether the directory is the root of the volume.</param>
    /// <returns>The new or existing root.</returns>
    private RootNode AddDiscoveredRoot(string path, VolumeInfo volume, bool isVolumeRoot)
    {
        var identity = _enumerator.GetIdentity(path);
        RootNode root;
        lock (_rootsGate)
        {
            var existing = Array.Find(_roots, candidate =>
                (isVolumeRoot && candidate.IsVolumeRoot && candidate.Volume.Id == volume.Id)
                || (identity.IsKnown && candidate.Identity == identity)
                || string.Equals(candidate.RootPath, path, _pathComparison));
            if (existing is not null)
            {
                return existing;
            }

            root = new RootNode(path, volume, identity, isVolumeRoot, isDiscovered: true);
            Volatile.Write(ref _roots, [.. _roots, root]);
            if (isVolumeRoot)
            {
                _fullRoots.TryAdd(volume.Id, root);
            }
        }

        _queue.Add(new WorkItem(root, path));
        return root;
    }

    /// <summary>
    /// Records an error, keeping at most <see cref="ScanOptions.MaxRecordedErrors"/> individual records.
    /// </summary>
    /// <param name="path">The path that failed.</param>
    /// <param name="error">The failure.</param>
    private void RecordError(string path, NodeError error)
    {
        Statistics.RecordError();
        if (Interlocked.Increment(ref _recordedErrors) <= Request.Options.MaxRecordedErrors)
        {
            _errors.Enqueue(new ScanErrorRecord(path, error));
        }
    }

    /// <summary>
    /// Publishes the final status once the last worker has exited, and releases resources.
    /// </summary>
    private void Finish()
    {
        var status = Failure is not null
            ? ScanStatus.Failed
            : _queue.IsCompleted ? ScanStatus.Completed : ScanStatus.Cancelled;
        Volatile.Write(ref _completedAtTicks, _timeProvider.GetUtcNow().UtcTicks);
        Volatile.Write(ref _status, (int)status);
        _queue.Dispose();
        _cancellation.Dispose();
        _completion.TrySetResult(status);
    }

    /// <summary>
    /// A directory waiting to be read.
    /// </summary>
    /// <param name="Node">The node that receives the directory's contents.</param>
    /// <param name="Path">The directory's absolute path.</param>
    private readonly record struct WorkItem(DirectoryNode Node, string Path);

    /// <summary>
    /// Buffers reused by a worker for every directory it reads.
    /// </summary>
    private sealed class WorkerContext
    {
        /// <summary>
        /// Gets the entries of the directory being read.
        /// </summary>
        public List<FileSystemEntryInfo> Entries { get; } = new(capacity: 256);

        /// <summary>
        /// Gets the subdirectories to queue once the directory has been read.
        /// </summary>
        public List<(DirectoryNode Node, string Path)> SubDirectories { get; } = new(capacity: 64);
    }
}
