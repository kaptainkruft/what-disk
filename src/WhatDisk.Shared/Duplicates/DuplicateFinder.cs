using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Hashing;
using Microsoft.Win32.SafeHandles;
using WhatDisk.Scanning;

namespace WhatDisk.Duplicates;

/// <summary>
/// Finds files with the same contents, reading as little as it can:
/// <list type="number">
/// <item>files are grouped by length, from the scan alone, without touching the disk;</item>
/// <item>files up to <see cref="FullHashLimit"/> (1 MiB), which are quick to read, are hashed whole with XXH128, and
/// files with the same length and hash form a set confirmed by <see cref="DuplicateComparison.Hash"/>;</item>
/// <item>larger files are compared by <see cref="SampleCount"/> samples of <see cref="SampleLength"/> bytes, spread
/// evenly from the start to the end, and files whose samples are all equal form a set matched by
/// <see cref="DuplicateComparison.Samples"/>. Such a set can be confirmed later with <see cref="VerifyAsync"/>.</item>
/// </list>
/// Empty files, files stored only in the cloud (reading them would download them) and extra hard links to a file
/// already compared are left out. Files that cannot be read, or whose length changed since the scan, are reported as
/// failures and left out.
/// </summary>
public sealed class DuplicateFinder
{
    /// <summary>
    /// The largest file, in bytes, that is hashed whole during the search rather than sampled: 1 MiB.
    /// </summary>
    public const long FullHashLimit = 1024 * 1024;

    /// <summary>
    /// The number of samples taken from each file larger than <see cref="FullHashLimit"/>.
    /// </summary>
    public const int SampleCount = 64;

    /// <summary>
    /// The length of each sample, in bytes: 4 KiB.
    /// </summary>
    public const int SampleLength = 4096;

    /// <summary>The size of the buffer each worker hashes through.</summary>
    private const int HashBufferLength = 1024 * 1024;

    /// <summary>The reason given for a file whose length changed after the scan.</summary>
    private const string ChangedReason = "Changed since the scan";

    /// <summary>Gets the identity of a file, so that hard links to the same data are compared once.</summary>
    private readonly Func<string, FileIdentity> _identify;

    /// <summary>The number of files read at once.</summary>
    private readonly int _parallelism;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateFinder"/> class.
    /// </summary>
    /// <param name="identify">
    /// Gets a file's identity (device and file number), or <see langword="null"/> when the platform cannot tell; files
    /// with the same identity are hard links to the same data and only the first is compared.
    /// </param>
    /// <param name="parallelism">
    /// The number of files read at once, or zero for the default: up to four, which keeps a solid-state disk busy
    /// without making a spinning disk seek between too many files.
    /// </param>
    public DuplicateFinder(Func<string, FileIdentity>? identify = null, int parallelism = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parallelism);
        _identify = identify ?? (static _ => FileIdentity.Unknown);
        _parallelism = parallelism > 0 ? parallelism : Math.Clamp(Environment.ProcessorCount, 1, 4);
    }

    /// <summary>
    /// Gets the interval between progress reports.
    /// </summary>
    public static TimeSpan ProgressInterval { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Finds the duplicate files in scanned trees.
    /// </summary>
    /// <param name="roots">The top-level items of the scan.</param>
    /// <param name="progress">Receives progress reports, at most every <see cref="ProgressInterval"/>; may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>The sets of duplicates.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DuplicateSearchResult> FindAsync(IEnumerable<FileSystemNode> roots, IProgress<DuplicateProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var state = new SearchState(progress);
        return Task.Run(() => CompareAsync(GroupTree([.. roots], state, cancellationToken), state, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Finds the duplicates among the given files.
    /// </summary>
    /// <param name="candidates">The files, with the lengths they are expected to have.</param>
    /// <param name="progress">Receives progress reports, at most every <see cref="ProgressInterval"/>; may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>The sets of duplicates.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DuplicateSearchResult> FindAsync(IEnumerable<DuplicateCandidate> candidates, IProgress<DuplicateProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var state = new SearchState(progress);
        var groups = new Dictionary<long, List<DuplicateCandidate>>();
        foreach (var candidate in candidates)
        {
            if (candidate.Length <= 0)
            {
                state.EmptyFiles++;
                continue;
            }

            state.FilesConsidered++;
            AddToGroup(groups, candidate.Length, candidate);
        }

        return Task.Run(() => CompareAsync([.. groups.Values.Where(static group => group.Count > 1)], state, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Hashes the whole of each file of a set, to confirm that files matched by samples are identical. The files split
    /// into groups by hash; a file whose hash matches no other is in no group.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="progress">
    /// Receives the number of bytes hashed so far, at most every <see cref="ProgressInterval"/> and once at the end;
    /// may be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    /// <returns>The groups of identical files, and the files that could not be read.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DuplicateVerification> VerifyAsync(DuplicateSet set, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        return Task.Run(
            async () =>
            {
                var files = set.Paths.Select(path => new Member(path, set.Length)).ToArray();
                var failures = new ConcurrentBag<DuplicateFailure>();
                var hashed = 0L;
                var lastReport = 0L;
                var options = new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = cancellationToken };
                await Parallel.ForEachAsync(files, options, (file, token) =>
                {
                    TryRead(file, FileOptions.SequentialScan, failures, handle =>
                    {
                        file.Hash = HashContents(handle, file.Length, read => Report(Interlocked.Add(ref hashed, read), force: false), token);
                        return file.Hash is not null;
                    });
                    return ValueTask.CompletedTask;
                });
                Report(Interlocked.Read(ref hashed), force: true);
                DuplicateHashGroup[] groups =
                [
                    .. files
                        .Where(static file => file.Hash is not null)
                        .GroupBy(static file => file.Hash!.Value)
                        .Where(static group => group.Skip(1).Any())
                        .Select(static group => new DuplicateHashGroup(FormatHash(group.Key), SortPaths(group.Select(static file => file.Path))))
                        .OrderByDescending(static group => group.Paths.Count)
                        .ThenBy(static group => group.Paths[0], StringComparer.Ordinal),
                ];
                return new DuplicateVerification(groups, [.. failures.OrderBy(static failure => failure.Path, StringComparer.Ordinal)]);

                void Report(long bytes, bool force)
                {
                    var now = Environment.TickCount64;
                    var last = Volatile.Read(ref lastReport);
                    if (progress is not null
                        && (force || (now - last >= (long)ProgressInterval.TotalMilliseconds && Interlocked.CompareExchange(ref lastReport, now, last) == last)))
                    {
                        progress.Report(bytes);
                    }
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets where the samples of a file are taken: <see cref="SampleCount"/> offsets spread evenly from the start of
    /// the file to the last whole sample at its end. Samples of a file no longer than <see cref="SampleLength"/> are
    /// the whole file.
    /// </summary>
    /// <param name="length">The file's length.</param>
    /// <returns>The offset of each sample.</returns>
    internal static long[] SampleOffsets(long length)
    {
        var last = Math.Max(0, length - SampleLength);
        var offsets = new long[SampleCount];
        for (var index = 0; index < SampleCount; index++)
        {
            offsets[index] = (long)((Int128)last * index / (SampleCount - 1));
        }

        return offsets;
    }

    /// <summary>
    /// Reads a file's samples, one after another.
    /// </summary>
    /// <param name="handle">The open file.</param>
    /// <param name="length">The file's length.</param>
    /// <returns>The samples, or <see langword="null"/> when the file ended early.</returns>
    internal static byte[]? ReadSamples(SafeFileHandle handle, long length)
    {
        var size = (int)Math.Min(SampleLength, length);
        var samples = new byte[SampleCount * size];
        var offsets = SampleOffsets(length);
        for (var index = 0; index < offsets.Length; index++)
        {
            if (!ReadExactly(handle, samples.AsSpan(index * size, size), offsets[index]))
            {
                return null;
            }
        }

        return samples;
    }

    /// <summary>
    /// Hashes the whole of a file with XXH128.
    /// </summary>
    /// <param name="handle">The open file.</param>
    /// <param name="length">The file's length.</param>
    /// <param name="onRead">Called with the number of bytes after each read; may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The hash, or <see langword="null"/> when the file ended early or grew while it was read.</returns>
    internal static UInt128? HashContents(SafeFileHandle handle, long length, Action<int>? onRead, CancellationToken cancellationToken)
    {
        var hasher = new XxHash128();
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Clamp(length, 1, HashBufferLength));
        try
        {
            var offset = 0L;
            while (offset < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
                if (read == 0)
                {
                    return null;
                }

                hasher.Append(buffer.AsSpan(0, read));
                offset += read;
                onRead?.Invoke(read);
            }

            return RandomAccess.GetLength(handle) == length ? hasher.GetCurrentHashAsUInt128() : null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Formats a hash as XXH128's canonical 32 lower-case hexadecimal digits.
    /// </summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The digits.</returns>
    internal static string FormatHash(UInt128 hash) => hash.ToString("x32", CultureInfo.InvariantCulture);

    /// <summary>
    /// Lists the regular files of scanned trees and groups them by length, keeping only the lengths shared by more
    /// than one file.
    /// </summary>
    /// <param name="roots">The top-level items.</param>
    /// <param name="state">Counts what is left out and reports progress.</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    /// <returns>The groups.</returns>
    private static List<List<DuplicateCandidate>> GroupTree(FileSystemNode[] roots, SearchState state, CancellationToken cancellationToken)
    {
        state.Begin(DuplicateSearchStage.Grouping, 0);
        var groups = new Dictionary<long, List<FileNode>>();
        var pending = new Stack<FileSystemNode>(roots);
        var seen = 0;
        while (pending.TryPop(out var node))
        {
            if (node is DirectoryNode directory)
            {
                foreach (var child in directory.Children)
                {
                    pending.Push(child);
                }
            }
            else if (node is FileNode { Kind: NodeKind.File } file)
            {
                if (++seen % 4096 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.Total = seen;
                    state.Report();
                }

                if (file.IsHardLinkDuplicate)
                {
                    state.HardLinks++;
                }
                else if (file.LogicalSize <= 0)
                {
                    state.EmptyFiles++;
                }
                else if ((file.Attributes & EntryAttributes.Offline) != 0)
                {
                    state.OfflineFiles++;
                }
                else
                {
                    state.FilesConsidered++;
                    AddToGroup(groups, file.LogicalSize, file);
                }
            }
        }

        // Paths are built only for the files that share a length, usually a small part of the scan.
        return [.. groups.Values.Where(static group => group.Count > 1).Select(static group => group.ConvertAll(static file => new DuplicateCandidate(file.FullPath, file.LogicalSize)))];
    }

    /// <summary>
    /// Adds an item to the group for its length.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="groups">The groups by length.</param>
    /// <param name="length">The item's length.</param>
    /// <param name="item">The item.</param>
    private static void AddToGroup<T>(Dictionary<long, List<T>> groups, long length, T item)
    {
        if (!groups.TryGetValue(length, out var group))
        {
            group = [];
            groups.Add(length, group);
        }

        group.Add(item);
    }

    /// <summary>
    /// Reads bytes until a buffer is full.
    /// </summary>
    /// <param name="handle">The open file.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where to start reading.</param>
    /// <returns><see langword="true"/> when the buffer was filled; <see langword="false"/> when the file ended first.</returns>
    private static bool ReadExactly(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            var read = RandomAccess.Read(handle, buffer, offset);
            if (read == 0)
            {
                return false;
            }

            buffer = buffer[read..];
            offset += read;
        }

        return true;
    }

    /// <summary>
    /// Opens a file and reads it, recording a failure when it cannot be opened or read, or no longer has the expected
    /// length.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="options">How the file will be read.</param>
    /// <param name="failures">Receives failures.</param>
    /// <param name="read">Reads the open file; returns <see langword="false"/> when it changed while it was read.</param>
    /// <returns><see langword="true"/> when the file was read.</returns>
    private static bool TryRead(Member file, FileOptions options, ConcurrentBag<DuplicateFailure> failures, Func<SafeFileHandle, bool> read)
    {
        string reason;
        try
        {
            // Other programs may keep the file open, even for writing, and may delete or rename it meanwhile.
            using var handle = File.OpenHandle(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, options);
            if (RandomAccess.GetLength(handle) == file.Length && read(handle))
            {
                return true;
            }

            reason = ChangedReason;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            reason = "No longer exists";
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Access denied";
        }
        catch (IOException exception)
        {
            reason = exception.Message;
        }

        failures.Add(new DuplicateFailure(file.Path, reason));
        return false;
    }

    /// <summary>
    /// Sorts paths for display: ignoring case, then by case so the order is stable.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <returns>The sorted paths.</returns>
    private static string[] SortPaths(IEnumerable<string> paths) =>
        [.. paths.Order(StringComparer.OrdinalIgnoreCase).ThenBy(static path => path, StringComparer.Ordinal)];

    /// <summary>
    /// Compares the files in each length group (larger files by samples, smaller ones by hash) and numbers the sets
    /// found.
    /// </summary>
    /// <param name="groups">The files, grouped by length.</param>
    /// <param name="state">Counts and reports progress.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <returns>The result.</returns>
    private async Task<DuplicateSearchResult> CompareAsync(List<List<DuplicateCandidate>> groups, SearchState state, CancellationToken cancellationToken)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = cancellationToken };
        var large = groups.Where(static group => group[0].Length > FullHashLimit).ToList();
        var small = groups.Where(static group => group[0].Length <= FullHashLimit).ToList();
        var matches = new ConcurrentBag<(Member[] Files, DuplicateComparison Comparison)>();

        state.Begin(DuplicateSearchStage.Sampling, large.Sum(static group => group.Count));
        await Parallel.ForEachAsync(large, options, (group, token) =>
        {
            foreach (var match in SampleGroup(group, state, token))
            {
                matches.Add((match, DuplicateComparison.Samples));
            }

            return ValueTask.CompletedTask;
        });

        state.Begin(DuplicateSearchStage.Hashing, small.Sum(static group => group.Count), small.Sum(static group => group[0].Length * group.Count));
        await Parallel.ForEachAsync(small, options, (group, token) =>
        {
            foreach (var match in HashGroup(group, state, token))
            {
                matches.Add((match, DuplicateComparison.Hash));
            }

            return ValueTask.CompletedTask;
        });
        state.Report(force: true);

        var sets = matches
            .Select(static match => (match.Files[0].Length, match.Files[0].Hash, match.Comparison, Paths: SortPaths(match.Files.Select(static file => file.Path))))
            .OrderByDescending(static set => set.Length * (set.Paths.Length - 1))
            .ThenByDescending(static set => set.Length)
            .ThenBy(static set => set.Paths[0], StringComparer.Ordinal)
            .Select(static (set, index) => new DuplicateSet(index + 1, set.Length, set.Hash is { } hash ? FormatHash(hash) : null, set.Paths, set.Comparison))
            .ToArray();
        return new DuplicateSearchResult(
            sets,
            state.FilesConsidered,
            groups.Sum(static group => group.Count),
            state.HashedFiles,
            state.SampledFiles,
            state.EmptyFiles,
            state.OfflineFiles,
            state.HardLinks,
            [.. state.Failures.OrderBy(static failure => failure.Path, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Lists the files of one length group to read, leaving out a file listed twice and hard links to a file already
    /// listed, and counting those as finished.
    /// </summary>
    /// <param name="group">The files, all of the same length.</param>
    /// <param name="state">Counts hard links and progress.</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    /// <returns>The files to read, in path order.</returns>
    private List<Member> Distinct(List<DuplicateCandidate> group, SearchState state, CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<FileIdentity>();
        var files = new List<Member>(group.Count);
        foreach (var candidate in group.OrderBy(static candidate => candidate.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.Add(candidate.Path))
            {
                state.CompleteFile();
            }
            else if (Identify(candidate.Path) is { IsKnown: true } identity && !identities.Add(identity))
            {
                Interlocked.Increment(ref state.HardLinks);
                state.CompleteFile();
            }
            else
            {
                files.Add(new Member(candidate.Path, candidate.Length));
            }
        }

        return files;
    }

    /// <summary>
    /// Reads the samples of the files in one length group and keeps the files whose samples match another file's.
    /// </summary>
    /// <param name="group">The files, all of the same length.</param>
    /// <param name="state">Counts and reports progress.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The files whose samples match, grouped by samples.</returns>
    private Member[][] SampleGroup(List<DuplicateCandidate> group, SearchState state, CancellationToken cancellationToken)
    {
        var read = new List<Member>(group.Count);
        foreach (var file in Distinct(group, state, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref state.SampledFiles);
            if (TryRead(file, FileOptions.RandomAccess, state.Failures, handle => (file.Samples = ReadSamples(handle, file.Length)) is not null))
            {
                read.Add(file);
            }

            state.CompleteFile();
        }

        return [.. read.GroupBy(static file => file.Samples!, ByteArrayComparer.Instance).Where(static match => match.Skip(1).Any()).Select(static match => match.ToArray())];
    }

    /// <summary>
    /// Hashes the whole of each file in one length group and keeps the files whose hash matches another file's.
    /// </summary>
    /// <param name="group">The files, all of the same length.</param>
    /// <param name="state">Counts and reports progress.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The files whose hashes match, grouped by hash.</returns>
    private Member[][] HashGroup(List<DuplicateCandidate> group, SearchState state, CancellationToken cancellationToken)
    {
        var hashed = new List<Member>(group.Count);
        foreach (var file in Distinct(group, state, cancellationToken))
        {
            Interlocked.Increment(ref state.HashedFiles);
            if (TryRead(file, FileOptions.SequentialScan, state.Failures, handle => (file.Hash = HashContents(handle, file.Length, state.AddBytes, cancellationToken)) is not null))
            {
                hashed.Add(file);
            }

            state.CompleteFile();
        }

        return [.. hashed.GroupBy(static file => file.Hash!.Value).Where(static match => match.Skip(1).Any()).Select(static match => match.ToArray())];
    }

    /// <summary>
    /// Gets a file's identity, treating a failure as unknown.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The identity.</returns>
    private FileIdentity Identify(string path)
    {
        try
        {
            return _identify(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileIdentity.Unknown;
        }
    }

    /// <summary>
    /// A file being compared.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="length">The file's length.</param>
    private sealed class Member(string path, long length)
    {
        /// <summary>
        /// Gets the file's path.
        /// </summary>
        public string Path { get; } = path;

        /// <summary>
        /// Gets the file's length.
        /// </summary>
        public long Length { get; } = length;

        /// <summary>
        /// Gets or sets the file's samples, once read.
        /// </summary>
        public byte[]? Samples { get; set; }

        /// <summary>
        /// Gets or sets the hash of the file's contents, once read.
        /// </summary>
        public UInt128? Hash { get; set; }
    }

    /// <summary>
    /// Compares byte arrays by their contents.
    /// </summary>
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        /// <summary>
        /// Gets the comparer.
        /// </summary>
        public static ByteArrayComparer Instance { get; } = new();

        /// <inheritdoc/>
        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        /// <inheritdoc/>
        public int GetHashCode(byte[] obj) => (int)XxHash3.HashToUInt64(obj);
    }

    /// <summary>
    /// The counts and progress of one search, shared by the workers.
    /// </summary>
    /// <param name="progress">Receives progress reports.</param>
    private sealed class SearchState(IProgress<DuplicateProgress>? progress)
    {
        /// <summary>The non-empty files grouped by length.</summary>
        public int FilesConsidered;

        /// <summary>The files hashed whole.</summary>
        public int HashedFiles;

        /// <summary>The files compared by samples.</summary>
        public int SampledFiles;

        /// <summary>The empty files left out.</summary>
        public int EmptyFiles;

        /// <summary>The cloud-only files left out.</summary>
        public int OfflineFiles;

        /// <summary>The extra hard links left out.</summary>
        public int HardLinks;

        /// <summary>The files in the current stage.</summary>
        public int Total;

        /// <summary>The files that could not be compared.</summary>
        public readonly ConcurrentBag<DuplicateFailure> Failures = [];

        /// <summary>The current stage.</summary>
        private DuplicateSearchStage _stage;

        /// <summary>The files finished in the current stage.</summary>
        private int _done;

        /// <summary>The bytes hashed so far.</summary>
        private long _bytesDone;

        /// <summary>The bytes to hash.</summary>
        private long _bytesTotal;

        /// <summary>When progress was last reported, from <see cref="Environment.TickCount64"/>.</summary>
        private long _lastReport;

        /// <summary>
        /// Starts a stage and reports it.
        /// </summary>
        /// <param name="stage">The stage.</param>
        /// <param name="total">The files in the stage.</param>
        /// <param name="bytesTotal">The bytes to read in the stage, when known.</param>
        public void Begin(DuplicateSearchStage stage, int total, long bytesTotal = 0)
        {
            _stage = stage;
            Total = total;
            _done = 0;
            _bytesDone = 0;
            _bytesTotal = bytesTotal;
            Report(force: true);
        }

        /// <summary>
        /// Counts a file as finished in the current stage.
        /// </summary>
        public void CompleteFile()
        {
            Interlocked.Increment(ref _done);
            Report();
        }

        /// <summary>
        /// Counts bytes hashed.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        public void AddBytes(int bytes)
        {
            Interlocked.Add(ref _bytesDone, bytes);
            Report();
        }

        /// <summary>
        /// Reports progress, unless it was reported less than <see cref="ProgressInterval"/> ago.
        /// </summary>
        /// <param name="force">Whether to report regardless of when progress was last reported.</param>
        public void Report(bool force = false)
        {
            if (progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var last = Volatile.Read(ref _lastReport);
            if (!force && (now - last < (long)ProgressInterval.TotalMilliseconds || Interlocked.CompareExchange(ref _lastReport, now, last) != last))
            {
                return;
            }

            Volatile.Write(ref _lastReport, now);
            progress.Report(new DuplicateProgress(_stage, Volatile.Read(ref _done), Total, Interlocked.Read(ref _bytesDone), _bytesTotal));
        }
    }
}
