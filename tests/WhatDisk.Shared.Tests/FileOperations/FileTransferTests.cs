using System.Runtime.Versioning;
using WhatDisk.FileOperations;

namespace WhatDisk.Tests.FileOperations;

/// <summary>
/// Tests moving, copying and deleting real files in a temporary directory.
/// </summary>
public sealed class FileTransferTests : IDisposable
{
    /// <summary>A temporary directory deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-transfer-");

    /// <summary>The engine under test.</summary>
    private readonly FileTransfer _transfer = new();

    /// <summary>
    /// Copying a folder copies its whole tree and leaves the original in place.
    /// </summary>
    /// <returns>A task that completes when the copy has finished.</returns>
    [Fact]
    public async Task Copy_CopiesWholeTrees()
    {
        var source = MakeTree("src/photos");
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;
        var reports = new List<TransferProgress>();

        var results = await _transfer.TransferAsync([source], destination, TransferMode.Copy, sameVolume: true, NoConflicts, new SynchronousProgress(reports), TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(TransferOutcome.Completed, result.Outcome);
        Assert.Equal(Path.Join(destination, "photos"), result.Destination);
        Assert.Equal("one", File.ReadAllText(Path.Join(destination, "photos", "a.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Join(destination, "photos", "inner", "b.txt")));
        Assert.True(Directory.Exists(source));
        Assert.Equal(6, reports[^1].BytesCopied);
    }

    /// <summary>
    /// Moving renames on the same volume, and copies then removes the original across volumes.
    /// </summary>
    /// <param name="sameVolume">Whether the destination is on the same volume.</param>
    /// <returns>A task that completes when the move has finished.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Move_RemovesTheOriginal(bool sameVolume)
    {
        var source = MakeTree("src/photos");
        var file = Path.Join(_temp.FullName, "src", "loose.txt");
        await File.WriteAllTextAsync(file, "loose", TestContext.Current.CancellationToken);
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;

        var results = await _transfer.TransferAsync([source, file], destination, TransferMode.Move, sameVolume, NoConflicts, null, TestContext.Current.CancellationToken);

        Assert.All(results, static result => Assert.Equal(TransferOutcome.Completed, result.Outcome));
        Assert.False(Directory.Exists(source));
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Join(destination, "photos", "inner", "b.txt")));
        Assert.Equal("loose", File.ReadAllText(Path.Join(destination, "loose.txt")));
    }

    /// <summary>
    /// Each conflict choice is honoured: replace, keep both (numbered like the Finder), skip.
    /// </summary>
    /// <returns>A task that completes when the operations have finished.</returns>
    [Fact]
    public async Task Conflicts_AreResolvedAsChosen()
    {
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;
        await File.WriteAllTextAsync(Path.Join(destination, "report.pdf"), "old", TestContext.Current.CancellationToken);
        var source = Path.Join(Directory.CreateDirectory(Path.Join(_temp.FullName, "src")).FullName, "report.pdf");
        await File.WriteAllTextAsync(source, "new", TestContext.Current.CancellationToken);
        var conflicts = new List<TransferConflict>();

        var kept = await _transfer.TransferAsync([source], destination, TransferMode.Copy, true, Answer(ConflictResolution.KeepBoth, conflicts), null, TestContext.Current.CancellationToken);
        var keptAgain = await _transfer.TransferAsync([source], destination, TransferMode.Copy, true, Answer(ConflictResolution.KeepBoth, conflicts), null, TestContext.Current.CancellationToken);
        var skipped = await _transfer.TransferAsync([source], destination, TransferMode.Copy, true, Answer(ConflictResolution.Skip, conflicts), null, TestContext.Current.CancellationToken);
        var replaced = await _transfer.TransferAsync([source], destination, TransferMode.Copy, true, Answer(ConflictResolution.Replace, conflicts), null, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Join(destination, "report 2.pdf"), kept[0].Destination);
        Assert.Equal(Path.Join(destination, "report 3.pdf"), keptAgain[0].Destination);
        Assert.Equal(TransferOutcome.Skipped, skipped[0].Outcome);
        Assert.True(replaced[0].Replaced);
        Assert.Equal("new", File.ReadAllText(Path.Join(destination, "report.pdf")));
        Assert.Equal(4, conflicts.Count);
        Assert.Equal(Path.Join(destination, "report.pdf"), conflicts[0].ExistingPath);
    }

    /// <summary>
    /// "Apply to all" answers every later conflict; "Stop" cancels the rest.
    /// </summary>
    /// <returns>A task that completes when the operations have finished.</returns>
    [Fact]
    public async Task Conflicts_ApplyToAllAndStop()
    {
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Join(_temp.FullName, "src")).FullName;
        string[] sources = [Path.Join(sourceDirectory, "a.txt"), Path.Join(sourceDirectory, "b.txt"), Path.Join(sourceDirectory, "c.txt")];
        foreach (var path in sources)
        {
            await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Join(destination, Path.GetFileName(path)), "y", TestContext.Current.CancellationToken);
        }

        var asked = 0;
        var all = await _transfer.TransferAsync(sources, destination, TransferMode.Copy, true, _ =>
        {
            asked++;
            return Task.FromResult(new ConflictDecision(ConflictResolution.Skip, ApplyToAll: true));
        }, null, TestContext.Current.CancellationToken);
        var stopped = await _transfer.TransferAsync(sources, destination, TransferMode.Copy, true, _ => Task.FromResult(new ConflictDecision(ConflictResolution.Stop)), null, TestContext.Current.CancellationToken);

        Assert.Equal(1, asked);
        Assert.All(all, static result => Assert.Equal(TransferOutcome.Skipped, result.Outcome));
        Assert.All(stopped, static result => Assert.Equal(TransferOutcome.Cancelled, result.Outcome));
    }

    /// <summary>
    /// A folder cannot be moved into itself, and moving an item onto its own folder does nothing.
    /// </summary>
    /// <returns>A task that completes when the operations have finished.</returns>
    [Fact]
    public async Task Move_RefusesImpossibleDestinations()
    {
        var source = MakeTree("src/photos");

        var intoItself = await _transfer.TransferAsync([source], Path.Join(source, "inner"), TransferMode.Move, true, NoConflicts, null, TestContext.Current.CancellationToken);
        var ontoItsParent = await _transfer.TransferAsync([source], Path.GetDirectoryName(source)!, TransferMode.Move, true, NoConflicts, null, TestContext.Current.CancellationToken);

        Assert.Equal(TransferOutcome.Failed, intoItself[0].Outcome);
        Assert.Equal(TransferOutcome.Skipped, ontoItsParent[0].Outcome);
        Assert.True(Directory.Exists(source));
    }

    /// <summary>
    /// Cancelling stops the operation and reports the remaining items as cancelled.
    /// </summary>
    /// <returns>A task that completes when the operation has stopped.</returns>
    [Fact]
    public async Task Cancel_ReportsRemainingItems()
    {
        var source = MakeTree("src/photos");
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var results = await _transfer.TransferAsync([source], destination, TransferMode.Copy, true, NoConflicts, null, cancellation.Token);

        Assert.Equal(TransferOutcome.Cancelled, results[0].Outcome);
        Assert.False(Directory.Exists(Path.Join(destination, "photos")));
    }

    /// <summary>
    /// Links are copied as links, and deleting a link leaves its target alone.
    /// </summary>
    /// <returns>A task that completes when the operations have finished.</returns>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Links_AreCopiedAndDeletedAsLinks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires extra privileges on Windows.");
        var target = MakeTree("src/photos");
        var link = Path.Join(_temp.FullName, "src", "shortcut");
        Directory.CreateSymbolicLink(link, target);
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;

        await _transfer.TransferAsync([link], destination, TransferMode.Copy, true, NoConflicts, null, TestContext.Current.CancellationToken);
        FileTransfer.Delete(link);

        Assert.Equal(target, new DirectoryInfo(Path.Join(destination, "shortcut")).LinkTarget);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Join(target, "a.txt")));
    }

    /// <summary>
    /// Deleting removes files and whole folders.
    /// </summary>
    [Fact]
    public void Delete_RemovesFilesAndFolders()
    {
        var tree = MakeTree("doomed");
        var file = Path.Join(tree, "a.txt");

        FileTransfer.Delete(file);
        Assert.False(File.Exists(file));

        FileTransfer.Delete(tree);
        Assert.False(Directory.Exists(tree));
    }

    /// <summary>
    /// Read-only files, and folders without write permission, are deleted too (on Windows, Git repositories are full
    /// of read-only files).
    /// </summary>
    [Fact]
    public void Delete_RemovesReadOnlyItems()
    {
        var tree = MakeTree("locked");
        File.SetAttributes(Path.Join(tree, "a.txt"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Join(tree, "inner", "b.txt"), FileAttributes.ReadOnly);
        new DirectoryInfo(Path.Join(tree, "inner")).Attributes |= FileAttributes.ReadOnly;

        FileTransfer.Delete(tree);

        Assert.False(Directory.Exists(tree));
    }

    /// <summary>
    /// On Windows, a file another program has open cannot be renamed; the move fails cleanly instead of leaving a
    /// copy behind (copying would succeed but removing the original would not).
    /// </summary>
    /// <returns>A task that completes when the move has been attempted.</returns>
    [Fact]
    public async Task Move_OfAnItemInUse_FailsWithoutDuplicating()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows prevents renaming open files.");
        var source = MakeTree("src/photos");
        var file = Path.Join(source, "a.txt");
        var destination = Directory.CreateDirectory(Path.Join(_temp.FullName, "dest")).FullName;

        IReadOnlyList<TransferResult> results;
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            results = await _transfer.TransferAsync([file], destination, TransferMode.Move, sameVolume: true, NoConflicts, null, TestContext.Current.CancellationToken);
        }

        Assert.Equal(TransferOutcome.Failed, Assert.Single(results).Outcome);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Join(destination, "a.txt")));
    }

    /// <summary>
    /// On Windows, a junction is moved as a link (its target is untouched) and is copied as a link where links can be
    /// created; where they cannot, the copy fails without leaving a partial copy.
    /// </summary>
    /// <returns>A task that completes when the transfers have finished.</returns>
    [Fact]
    public async Task Junctions_AreMovedAndCopiedAsLinks()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Junctions exist on Windows only.");
        var target = MakeTree("target");
        var folder = Directory.CreateDirectory(Path.Join(_temp.FullName, "holder")).FullName;
        var junction = Path.Join(folder, "junction");
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", junction, target]) { UseShellExecute = false, CreateNoWindow = true })!)
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var moved = Directory.CreateDirectory(Path.Join(_temp.FullName, "moved")).FullName;
        var move = await _transfer.TransferAsync([junction], moved, TransferMode.Move, sameVolume: true, NoConflicts, null, TestContext.Current.CancellationToken);
        Assert.Equal(TransferOutcome.Completed, Assert.Single(move).Outcome);
        Assert.NotNull(new DirectoryInfo(Path.Join(moved, "junction")).LinkTarget);
        Assert.True(File.Exists(Path.Join(target, "a.txt")));

        var copies = Directory.CreateDirectory(Path.Join(_temp.FullName, "copies")).FullName;
        var copy = Assert.Single(await _transfer.TransferAsync([moved], copies, TransferMode.Copy, sameVolume: true, NoConflicts, null, TestContext.Current.CancellationToken));
        if (copy.Outcome == TransferOutcome.Completed)
        {
            Assert.NotNull(new DirectoryInfo(Path.Join(copies, "moved", "junction")).LinkTarget);
        }
        else
        {
            Assert.Equal(TransferOutcome.Failed, copy.Outcome);
            Assert.False(Directory.Exists(Path.Join(copies, "moved")));
        }

        Assert.True(File.Exists(Path.Join(target, "a.txt")));
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// A conflict handler for operations that must not meet conflicts.
    /// </summary>
    /// <param name="conflict">The unexpected conflict.</param>
    /// <returns>Never returns.</returns>
    private static Task<ConflictDecision> NoConflicts(TransferConflict conflict) =>
        throw new InvalidOperationException($"Unexpected conflict: {conflict.ExistingPath}");

    /// <summary>
    /// Creates a conflict handler that records conflicts and always gives the same answer.
    /// </summary>
    /// <param name="resolution">The answer.</param>
    /// <param name="log">Receives the conflicts.</param>
    /// <returns>The handler.</returns>
    private static Func<TransferConflict, Task<ConflictDecision>> Answer(ConflictResolution resolution, List<TransferConflict> log) => conflict =>
    {
        log.Add(conflict);
        return Task.FromResult(new ConflictDecision(resolution));
    };

    /// <summary>
    /// Creates a folder containing a.txt ("one") and inner/b.txt ("two").
    /// </summary>
    /// <param name="relative">The folder, relative to the temporary directory.</param>
    /// <returns>The folder's path.</returns>
    private string MakeTree(string relative)
    {
        var root = Directory.CreateDirectory(Path.Join(_temp.FullName, relative)).FullName;
        Directory.CreateDirectory(Path.Join(root, "inner"));
        File.WriteAllText(Path.Join(root, "a.txt"), "one");
        File.WriteAllText(Path.Join(root, "inner", "b.txt"), "two");
        return root;
    }

    /// <summary>
    /// A progress receiver that records reports as they are made.
    /// </summary>
    /// <param name="reports">Receives the reports.</param>
    private sealed class SynchronousProgress(List<TransferProgress> reports) : IProgress<TransferProgress>
    {
        /// <inheritdoc/>
        public void Report(TransferProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }
}
