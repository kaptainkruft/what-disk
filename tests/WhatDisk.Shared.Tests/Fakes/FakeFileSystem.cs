using System.Collections.Concurrent;
using WhatDisk.Scanning;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// An in-memory directory tree that implements <see cref="IFileSystemEnumerator"/> for deterministic scanner tests.
/// Configure it before scanning; reads are thread-safe.
/// </summary>
internal sealed class FakeFileSystem : IFileSystemEnumerator
{
    /// <summary>The directories, keyed by absolute path.</summary>
    private readonly ConcurrentDictionary<string, FakeDirectory> _directories = new(StringComparer.Ordinal);

    /// <summary>Identities returned by <see cref="GetIdentity"/>, keyed by absolute path.</summary>
    private readonly ConcurrentDictionary<string, FileIdentity> _identities = new(StringComparer.Ordinal);

    /// <summary>Paths that have been read, in the order the reads started.</summary>
    private readonly ConcurrentQueue<string> _readLog = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeFileSystem"/> class with an empty root directory.
    /// </summary>
    /// <param name="root">The absolute path of the root directory; defaults to a path under the temp directory.</param>
    public FakeFileSystem(string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "whatdisk-fake");
        Directory(Root);
    }

    /// <summary>
    /// Gets the absolute path of the root directory.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the paths that have been read so far.
    /// </summary>
    public IReadOnlyCollection<string> ReadLog => _readLog;

    /// <inheritdoc/>
    public string Name => "Fake";

    /// <summary>
    /// Gets or sets an action invoked at the start of every directory read, for example to block a worker.
    /// </summary>
    public Action<string, CancellationToken>? OnRead { get; set; }

    /// <summary>
    /// Resolves a path relative to <see cref="Root"/>.
    /// </summary>
    /// <param name="relative">A path relative to the root, using '/' separators; empty for the root.</param>
    /// <returns>The absolute path.</returns>
    public string PathOf(string relative) =>
        relative.Length == 0 ? Root : Path.Join(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Ensures a directory (and its ancestors) exists.
    /// </summary>
    /// <param name="relativeOrAbsolute">The directory, relative to the root or absolute.</param>
    /// <param name="attributes">Attributes reported for the directory's entry in its parent.</param>
    /// <param name="identity">The identity reported for the directory's entry in its parent.</param>
    /// <returns>This instance, for chaining.</returns>
    public FakeFileSystem Directory(string relativeOrAbsolute, EntryAttributes attributes = EntryAttributes.None, FileIdentity identity = default)
    {
        var path = ToAbsolute(relativeOrAbsolute);
        _directories.GetOrAdd(path, static _ => new FakeDirectory());
        if (!WhatDisk.Platform.PathUtilities.IsStrictDescendant(path, Root, StringComparison.Ordinal))
        {
            // The root itself, or an independent directory outside it, has no parent entry to maintain.
            return this;
        }

        var parentPath = Path.GetDirectoryName(path)!;
        Directory(parentPath);
        var name = Path.GetFileName(path);
        var parent = _directories[parentPath];
        var entry = new FileSystemEntryInfo(name, EntryType.Directory, 0, FileSystemEntryInfo.UnknownSize, DateTime.MinValue, attributes, identity);
        lock (parent)
        {
            var index = parent.Entries.FindIndex(existing => existing.Name == name);
            if (index < 0)
            {
                parent.Entries.Add(entry);
            }
            else if (attributes != EntryAttributes.None || identity.IsKnown)
            {
                parent.Entries[index] = entry;
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a file, creating its directory if necessary.
    /// </summary>
    /// <param name="relative">The file path relative to the root.</param>
    /// <param name="logicalSize">The file's logical size.</param>
    /// <param name="allocatedSize">The allocated size, or <see cref="FileSystemEntryInfo.UnknownSize"/>.</param>
    /// <param name="identity">The file's identity.</param>
    /// <param name="linkCount">The number of hard links.</param>
    /// <param name="type">The entry type.</param>
    /// <returns>This instance, for chaining.</returns>
    public FakeFileSystem File(
        string relative,
        long logicalSize,
        long allocatedSize = FileSystemEntryInfo.UnknownSize,
        FileIdentity identity = default,
        uint linkCount = 0,
        EntryType type = EntryType.File)
    {
        var path = ToAbsolute(relative);
        var parentPath = Path.GetDirectoryName(path)!;
        Directory(parentPath);
        var parent = _directories[parentPath];
        lock (parent)
        {
            parent.Entries.Add(new FileSystemEntryInfo(
                Path.GetFileName(path),
                type,
                logicalSize,
                allocatedSize,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                EntryAttributes.None,
                identity,
                linkCount));
        }

        return this;
    }

    /// <summary>
    /// Makes reading a directory fail, after returning any entries listed in <paramref name="entriesBeforeFailure"/>.
    /// </summary>
    /// <param name="relative">The directory, relative to the root.</param>
    /// <param name="exception">The exception to throw.</param>
    /// <param name="entriesBeforeFailure">How many of the directory's entries are returned before the failure.</param>
    /// <returns>This instance, for chaining.</returns>
    public FakeFileSystem Fail(string relative, Exception exception, int entriesBeforeFailure = 0)
    {
        var path = ToAbsolute(relative);
        Directory(path);
        var directory = _directories[path];
        directory.Failure = exception;
        directory.EntriesBeforeFailure = entriesBeforeFailure;
        return this;
    }

    /// <summary>
    /// Sets the identity returned by <see cref="GetIdentity"/> and <see cref="ReadDirectory"/> for a path.
    /// </summary>
    /// <param name="relativeOrAbsolute">The path.</param>
    /// <param name="identity">The identity.</param>
    /// <returns>This instance, for chaining.</returns>
    public FakeFileSystem Identify(string relativeOrAbsolute, FileIdentity identity)
    {
        _identities[ToAbsolute(relativeOrAbsolute)] = identity;
        return this;
    }

    /// <inheritdoc/>
    public FileIdentity GetIdentity(string path) => _identities.GetValueOrDefault(path);

    /// <inheritdoc/>
    public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken)
    {
        _readLog.Enqueue(path);
        OnRead?.Invoke(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_directories.TryGetValue(path, out var directory))
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{path}'.");
        }

        FileSystemEntryInfo[] snapshot;
        lock (directory)
        {
            snapshot = [.. directory.Entries];
        }

        var limit = directory.Failure is null ? snapshot.Length : Math.Min(directory.EntriesBeforeFailure, snapshot.Length);
        for (var index = 0; index < limit; index++)
        {
            entries.Add(snapshot[index]);
        }

        if (directory.Failure is not null)
        {
            throw directory.Failure;
        }

        return GetIdentity(path);
    }

    /// <summary>
    /// Converts a relative path to an absolute one; absolute paths are returned unchanged.
    /// </summary>
    /// <param name="relativeOrAbsolute">The path.</param>
    /// <returns>The absolute path.</returns>
    private string ToAbsolute(string relativeOrAbsolute) =>
        Path.IsPathRooted(relativeOrAbsolute) ? relativeOrAbsolute : PathOf(relativeOrAbsolute);

    /// <summary>
    /// A directory of the fake file system.
    /// </summary>
    private sealed class FakeDirectory
    {
        /// <summary>
        /// Gets the entries of the directory.
        /// </summary>
        public List<FileSystemEntryInfo> Entries { get; } = [];

        /// <summary>
        /// Gets or sets the exception thrown when the directory is read.
        /// </summary>
        public Exception? Failure { get; set; }

        /// <summary>
        /// Gets or sets how many entries are returned before <see cref="Failure"/> is thrown.
        /// </summary>
        public int EntriesBeforeFailure { get; set; }
    }
}
