using WhatDisk.Platform;

namespace WhatDisk.FileOperations;

/// <summary>
/// Moves and copies files and folders, asking how to resolve name conflicts, reporting progress and honouring
/// cancellation. Symbolic links are copied as links, never followed.
/// </summary>
/// <param name="pathComparison">How paths are compared on this platform.</param>
public sealed class FileTransfer(StringComparison pathComparison = StringComparison.Ordinal)
{
    /// <summary>Options that list every entry of a directory and surface errors.</summary>
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

    /// <summary>
    /// Moves or copies items into a directory.
    /// </summary>
    /// <param name="sources">The items to move or copy.</param>
    /// <param name="destinationDirectory">The directory to put them in.</param>
    /// <param name="mode">Whether to move or copy.</param>
    /// <param name="sameVolume">Whether the destination is on the same volume as the sources, so a move is a rename.</param>
    /// <param name="resolveConflict">Asks how to handle a name that is already taken.</param>
    /// <param name="progress">Receives progress, if wanted.</param>
    /// <param name="cancellationToken">Stops the operation; a partially copied item is removed.</param>
    /// <returns>One result per item, in order.</returns>
    public async Task<IReadOnlyList<TransferResult>> TransferAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        TransferMode mode,
        bool sameVolume,
        Func<TransferConflict, Task<ConflictDecision>> resolveConflict,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        ArgumentNullException.ThrowIfNull(resolveConflict);

        var results = new List<TransferResult>(sources.Count);
        ConflictDecision? remembered = null;
        long bytesCopied = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            var source = PathUtilities.TrimTrailingSeparator(sources[index]);
            var name = Path.GetFileName(source);
            progress?.Report(new TransferProgress(index, sources.Count, bytesCopied, name));
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new TransferResult(source, null, TransferOutcome.Cancelled));
                continue;
            }

            if (PathUtilities.IsSameOrDescendant(destinationDirectory, source, pathComparison))
            {
                results.Add(new TransferResult(source, null, TransferOutcome.Failed, Error: "A folder cannot be moved or copied into itself."));
                continue;
            }

            var destination = Path.Join(destinationDirectory, name);
            if (mode == TransferMode.Move && string.Equals(destination, source, pathComparison))
            {
                results.Add(new TransferResult(source, destination, TransferOutcome.Skipped));
                continue;
            }

            var replaced = false;
            if (Exists(destination))
            {
                var decision = remembered ?? await resolveConflict(new TransferConflict(source, destination, IsRealDirectory(source)));
                if (decision.ApplyToAll)
                {
                    remembered = decision;
                }

                switch (decision.Resolution)
                {
                    case ConflictResolution.Stop:
                        for (; index < sources.Count; index++)
                        {
                            results.Add(new TransferResult(sources[index], null, TransferOutcome.Cancelled));
                        }

                        return results;
                    case ConflictResolution.Skip:
                        results.Add(new TransferResult(source, null, TransferOutcome.Skipped));
                        continue;
                    case ConflictResolution.KeepBoth:
                        destination = UniqueName(destinationDirectory, name, IsRealDirectory(source));
                        break;
                    default:
                        replaced = true;
                        break;
                }
            }

            try
            {
                var copied = await Task.Run(
                    () => TransferOne(source, destination, mode, sameVolume, replaced, bytes => progress?.Report(new TransferProgress(index, sources.Count, bytesCopied + bytes, name)), cancellationToken),
                    CancellationToken.None);
                bytesCopied += copied;
                results.Add(new TransferResult(source, destination, TransferOutcome.Completed, replaced));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(new TransferResult(source, null, TransferOutcome.Cancelled));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                results.Add(new TransferResult(source, null, TransferOutcome.Failed, Error: exception.Message));
            }
        }

        progress?.Report(new TransferProgress(sources.Count, sources.Count, bytesCopied, null));
        return results;
    }

    /// <summary>
    /// Finds a free name in a directory, numbering the way the Finder does: "Report 2.pdf", "Report 3.pdf", or
    /// "Photos 2" for a folder.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="name">The name that is taken.</param>
    /// <param name="isDirectory">Whether the item is a directory (whose name keeps any dots intact).</param>
    /// <returns>A path in <paramref name="directory"/> that does not exist.</returns>
    public static string UniqueName(string directory, string name, bool isDirectory)
    {
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        for (var number = 2; ; number++)
        {
            var candidate = Path.Join(directory, $"{stem} {number}{extension}");
            if (!Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Deletes an item for good. A symbolic link is removed without touching what it points to.
    /// </summary>
    /// <param name="path">The item.</param>
    public static void Delete(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || !IsRealDirectory(path))
        {
            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(path);
            }
            else
            {
                info.Delete();
            }

            return;
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Determines whether something (including a broken link) exists at a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> when the path is taken.</returns>
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>
    /// Determines whether a path is a directory that is not a symbolic link.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a real directory.</returns>
    private static bool IsRealDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && info.LinkTarget is null;
    }

    /// <summary>
    /// Moves or copies one item.
    /// </summary>
    /// <param name="source">The item.</param>
    /// <param name="destination">Where it goes.</param>
    /// <param name="mode">Whether to move or copy.</param>
    /// <param name="sameVolume">Whether a move can be a rename.</param>
    /// <param name="replace">Whether to delete an existing item at the destination first.</param>
    /// <param name="onBytes">Receives the number of bytes copied so far for this item.</param>
    /// <param name="cancellationToken">Stops the copy.</param>
    /// <returns>The number of bytes copied (zero for a rename).</returns>
    private static long TransferOne(string source, string destination, TransferMode mode, bool sameVolume, bool replace, Action<long> onBytes, CancellationToken cancellationToken)
    {
        if (!Exists(source))
        {
            throw new FileNotFoundException("The item no longer exists.", source);
        }

        if (replace)
        {
            Delete(destination);
        }

        if (mode == TransferMode.Move && sameVolume)
        {
            try
            {
                Rename(source, destination);
                return 0;
            }
            catch (IOException)
            {
                // The volumes differ after all (for example through a firmlink); fall back to copying.
            }
        }

        long copied = 0;
        try
        {
            CopyEntry(source, destination, ref copied, onBytes, cancellationToken);
        }
        catch
        {
            if (Exists(destination))
            {
                Delete(destination);
            }

            throw;
        }

        if (mode == TransferMode.Move)
        {
            Delete(source);
        }

        return copied;
    }

    /// <summary>
    /// Renames an item on the same volume.
    /// </summary>
    /// <param name="source">The item.</param>
    /// <param name="destination">Its new path.</param>
    private static void Rename(string source, string destination)
    {
        if (IsRealDirectory(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    /// <summary>
    /// Copies a file, link or directory tree.
    /// </summary>
    /// <param name="source">The item.</param>
    /// <param name="destination">The copy's path.</param>
    /// <param name="copied">The running total of bytes copied.</param>
    /// <param name="onBytes">Receives the running total after each file.</param>
    /// <param name="cancellationToken">Stops the copy between files.</param>
    private static void CopyEntry(string source, string destination, ref long copied, Action<long> onBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new FileInfo(source);
        if (info.LinkTarget is { } target)
        {
            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateSymbolicLink(destination, target);
            }
            else
            {
                File.CreateSymbolicLink(destination, target);
            }

            return;
        }

        if ((info.Attributes & FileAttributes.Directory) == 0)
        {
            File.Copy(source, destination, overwrite: false);
            copied += info.Length;
            onBytes(copied);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var child in Directory.EnumerateFileSystemEntries(source, "*", AllEntries))
        {
            CopyEntry(child, Path.Join(destination, Path.GetFileName(child)), ref copied, onBytes, cancellationToken);
        }
    }
}
