using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.FileOperations;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.ViewModels;

/// <summary>
/// The actions on selected items: opening, revealing, copying, deleting, moving and copying by drag and drop,
/// rescanning and expanding. Every action works on the whole selection, keeping only the topmost of nested items.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Deleting more files than this asks for confirmation first.
    /// </summary>
    public const int DeleteConfirmationFiles = 10;

    /// <summary>The file manager's actions.</summary>
    private readonly IShellService _shell;

    /// <summary>Asks the user to confirm and to resolve conflicts.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>Describes items added to the tree.</summary>
    private readonly IFileSystemEnumerator _enumerator;

    /// <summary>Moves and copies items.</summary>
    private readonly FileTransfer _transfer;

    /// <summary>Cancels the running file operation.</summary>
    private CancellationTokenSource? _operationCancellation;

    /// <summary>
    /// Gets a value indicating whether a file operation (deleting, moving, copying or rescanning) is running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
    public partial bool IsOperationRunning { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tree has keyboard focus (set by the view). Item commands are only
    /// enabled then, so their shortcuts do not take keys such as ⌘C or ⌘⌫ away from the path box.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTreeActive { get; set; }

    /// <summary>
    /// Gets the file manager actions the platform supports.
    /// </summary>
    public ShellCapabilities ShellCapabilities => _shell.Capabilities;

    /// <summary>
    /// Gets the size above which deleting asks for confirmation first: 10 MB in the platform's units.
    /// </summary>
    public long DeleteConfirmationBytes => Platform.ByteUnits == ByteUnitConvention.PowersOf1024 ? 10L * 1024 * 1024 : 10_000_000;

    /// <summary>
    /// Lists the applications that can open the selected items, the default first.
    /// </summary>
    /// <returns>The applications, or an empty list.</returns>
    public IReadOnlyList<ApplicationChoice> GetOpenWithChoices()
    {
        if (!Supports(ShellCapabilities.OpenWith) || SelectedRow is not { } row)
        {
            return [];
        }

        try
        {
            return _shell.GetApplications(row.Node.FullPath);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// Determines whether rows may be dragged: nothing is running and every item may be moved or copied.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns><see langword="true"/> when the rows may be dragged.</returns>
    public bool CanDrag(IReadOnlyList<TreeRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var items = Topmost(rows.Select(static row => row.Node));
        return !IsScanning && !IsOperationRunning && items.Length > 0 && items.All(IsModifiable);
    }

    /// <summary>
    /// Decides what dropping rows onto a row would do: move (the default within a volume), copy (the default across
    /// volumes) or nothing when the drop is not allowed.
    /// </summary>
    /// <param name="rows">The dragged rows.</param>
    /// <param name="target">The row under the pointer.</param>
    /// <param name="copyModifier">Whether the copy modifier (Option on macOS) is held.</param>
    /// <param name="moveModifier">Whether the move modifier (Command on macOS) is held.</param>
    /// <returns>The operation, or <see langword="null"/> when the drop is not allowed.</returns>
    public TransferMode? GetDropMode(IReadOnlyList<TreeRowViewModel> rows, TreeRowViewModel? target, bool copyModifier, bool moveModifier)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (IsScanning || IsOperationRunning || target?.Node is not DirectoryNode destination || destination.Volume.IsReadOnly)
        {
            return null;
        }

        var items = Topmost(rows.Select(static row => row.Node));
        if (items.Length == 0 || !items.All(IsModifiable) || items.Any(item => destination.SelfAndAncestors.Contains(item)))
        {
            return null;
        }

        var sameVolume = items.All(item => item.Volume.Id == destination.Volume.Id);
        var mode = copyModifier ? TransferMode.Copy : moveModifier ? TransferMode.Move : sameVolume ? TransferMode.Move : TransferMode.Copy;
        return mode == TransferMode.Move && items.All(item => ReferenceEquals(item.Parent, destination)) ? null : mode;
    }

    /// <summary>
    /// Moves or copies dragged rows into the folder they were dropped on, then updates the tree.
    /// </summary>
    /// <param name="rows">The dragged rows.</param>
    /// <param name="target">The row they were dropped on.</param>
    /// <param name="copyModifier">Whether the copy modifier (Option on macOS) was held.</param>
    /// <param name="moveModifier">Whether the move modifier (Command on macOS) was held.</param>
    /// <returns>A task that completes when the operation and the tree update have finished.</returns>
    public async Task DropAsync(IReadOnlyList<TreeRowViewModel> rows, TreeRowViewModel target, bool copyModifier, bool moveModifier)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (GetDropMode(rows, target, copyModifier, moveModifier) is not { } mode || target.Node is not DirectoryNode destination)
        {
            return;
        }

        var items = Topmost(rows.Select(static row => row.Node));
        var verb = mode == TransferMode.Move ? "Moving" : "Copying";
        await RunOperationAsync(async cancellationToken =>
        {
            var grafts = new List<ScanSession>();
            var failures = new List<string>();
            var done = 0;
            var progress = new Progress<TransferProgress>(report => StatusText = report.CurrentName is null
                ? $"{verb} {DisplayFormat.Quantity(report.ItemsTotal, "item", "items")}…"
                : $"{verb} {report.ItemsDone + 1} of {report.ItemsTotal}: {report.CurrentName} ({_sizes.Format(report.BytesCopied)} copied)");
            foreach (var group in items.GroupBy(item => item.Volume.Id == destination.Volume.Id))
            {
                FileSystemNode[] batch = [.. group];
                var results = await _transfer.TransferAsync(
                    [.. batch.Select(static item => item.FullPath)], destination.FullPath, mode, group.Key, _dialogs.ResolveConflictAsync, progress, cancellationToken);
                for (var index = 0; index < results.Count; index++)
                {
                    switch (results[index].Outcome)
                    {
                        case TransferOutcome.Completed:
                            ApplyTransfer(batch[index], results[index], destination, mode, group.Key, grafts);
                            done++;
                            break;
                        case TransferOutcome.Failed:
                            failures.Add($"{batch[index].Name}: {results[index].Error}");
                            break;
                    }
                }
            }

            await WaitForGraftsAsync(grafts, cancellationToken);
            var past = mode == TransferMode.Move ? "Moved" : "Copied";
            return Summarise($"{past} {DisplayFormat.Quantity(done, "item", "items")} to “{destination.Name}”", failures);
        });
    }

    /// <summary>
    /// Opens the selected items with their default applications.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => RunShellAction("open the items", () => _shell.Open(SelectedPaths()));

    /// <summary>
    /// Opens the selected items with an application.
    /// </summary>
    /// <param name="application">The application.</param>
    [RelayCommand(CanExecute = nameof(CanOpenWith))]
    private void OpenWith(ApplicationChoice? application)
    {
        if (application is not null)
        {
            RunShellAction($"open the items with {application.Name}", () => _shell.OpenWith(SelectedPaths(), application));
        }
    }

    /// <summary>
    /// Shows the selected items in the file manager.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReveal))]
    private void Reveal() => RunShellAction("show the items", () => _shell.Reveal(SelectedPaths()));

    /// <summary>
    /// Previews the selected items, or closes the preview.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQuickLook))]
    private void QuickLook() => RunShellAction("preview the items", () => _shell.QuickLook(SelectedPaths()));

    /// <summary>
    /// Opens the file manager's information window for the selected items.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGetInfo))]
    private void GetInfo() => RunShellAction("show information", () => _shell.ShowInfo(SelectedPaths()));

    /// <summary>
    /// Copies the selected items so they can be pasted in the file manager.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyItems))]
    private void CopyItems()
    {
        var paths = SelectedPaths();
        RunShellAction("copy the items", () =>
        {
            _shell.CopyItems(paths);
            StatusText = $"Copied {DisplayFormat.Quantity(paths.Count, "item", "items")} to the clipboard";
        });
    }

    /// <summary>
    /// Copies the paths of the selected items as text, one per line.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private void CopyPath()
    {
        var paths = SelectedPaths();
        RunShellAction("copy the paths", () =>
        {
            _shell.CopyText(string.Join(Environment.NewLine, paths));
            StatusText = $"Copied {DisplayFormat.Quantity(paths.Count, "path", "paths")} to the clipboard";
        });
    }

    /// <summary>
    /// Selects every visible row.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll() => Selection.SelectAll();

    /// <summary>
    /// Expands the selected folders and everything beneath them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedFolders))]
    private void ExpandAll()
    {
        foreach (var row in SelectedRows.Where(static row => row.IsExpandable))
        {
            Tree.SetSubtreeExpanded(row, expanded: true);
        }
    }

    /// <summary>
    /// Collapses the selected folders and everything beneath them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedFolders))]
    private void CollapseAll()
    {
        foreach (var row in SelectedRows.Where(static row => row.IsExpandable))
        {
            Tree.SetSubtreeExpanded(row, expanded: false);
        }
    }

    /// <summary>
    /// Reads the selected folders again; selecting a top-level item rescans everything.
    /// </summary>
    /// <returns>A task that completes when the rescan has finished.</returns>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        var directories = Topmost(SelectedRows.Select(static row => row.Node)).OfType<DirectoryNode>().ToArray();
        if (directories.Any(static directory => directory is RootNode) && _session is { } session)
        {
            await RunScanAsync([.. session.Request.Targets]);
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            StatusText = $"Rescanning {DisplayFormat.Quantity(directories.Length, "folder", "folders")}…";
            var grafts = new List<ScanSession>();
            foreach (var directory in directories)
            {
                var parent = directory.Parent!;
                RemoveFromTree(directory);
                grafts.Add(_scanner.StartInto(parent, directory.Name, _session?.Roots ?? []));
            }

            await WaitForGraftsAsync(grafts, cancellationToken);
            Tree.Refresh();
            var rescanned = grafts.Select(graft => graft.GraftedDirectory is { } node ? Tree.FindVisible(node) : null).OfType<TreeRowViewModel>().ToArray();
            if (rescanned.Length > 0)
            {
                SelectRows(rescanned);
            }

            return $"Rescanned {DisplayFormat.Quantity(directories.Length, "folder", "folders")}";
        });
    }

    /// <summary>
    /// Moves the selected items to the Trash, asking first when they are large or many.
    /// </summary>
    /// <returns>A task that completes when the items have been moved.</returns>
    [RelayCommand(CanExecute = nameof(CanMoveToTrash))]
    private Task MoveToTrashAsync() => DeleteSelectionAsync(permanently: false);

    /// <summary>
    /// Deletes the selected items permanently, asking first when they are large or many.
    /// </summary>
    /// <returns>A task that completes when the items have been deleted.</returns>
    [RelayCommand(CanExecute = nameof(CanDeletePermanently))]
    private Task DeletePermanentlyAsync() => DeleteSelectionAsync(permanently: true);

    /// <summary>
    /// Deletes the selected items, either to the Trash or permanently.
    /// </summary>
    /// <param name="permanently">Whether to delete permanently.</param>
    /// <returns>A task that completes when the items have been deleted.</returns>
    private async Task DeleteSelectionAsync(bool permanently)
    {
        var items = Topmost(SelectedRows.Select(static row => row.Node));
        if (items.Length == 0 || !items.All(IsModifiable))
        {
            return;
        }

        var bytes = items.Sum(static item => item.Size);
        var files = items.Sum(static item => item.FileCount);
        if (bytes > DeleteConfirmationBytes || files > DeleteConfirmationFiles)
        {
            var what = items.Length == 1 ? $"“{items[0].Name}”" : DisplayFormat.Quantity(items.Length, "item", "items");
            var amount = $"{_sizes.Format(bytes)} in {DisplayFormat.Quantity(files, "file", "files")}";
            var request = permanently
                ? new ConfirmationRequest($"Delete {what} {PermanentDeleteAdverb()}?", $"{amount} will be deleted permanently. This can’t be undone.", "Delete", IsDestructive: true)
                : new ConfirmationRequest($"Move {what} to the {Platform.TrashName}?", $"{amount} will be moved to the {Platform.TrashName}, from where it can be restored.", $"Move to {Platform.TrashName}", IsDestructive: false);
            if (!await _dialogs.ConfirmAsync(request))
            {
                return;
            }
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var failures = new List<string>();
            var done = 0;
            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                StatusText = permanently ? $"Deleting “{item.Name}”…" : $"Moving “{item.Name}” to the {Platform.TrashName}…";
                try
                {
                    var path = item.FullPath;
                    if (permanently)
                    {
                        await Task.Run(() => FileTransfer.Delete(path), CancellationToken.None);
                    }
                    else
                    {
                        await _shell.MoveToTrashAsync(path, CancellationToken.None);
                    }

                    RemoveFromTree(item);
                    done++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    failures.Add($"{item.Name}: {exception.Message}");
                }
            }

            var summary = permanently
                ? $"Deleted {DisplayFormat.Quantity(done, "item", "items")} ({_sizes.Format(bytes)})"
                : $"Moved {DisplayFormat.Quantity(done, "item", "items")} to the {Platform.TrashName} ({_sizes.Format(bytes)})";
            return Summarise(summary, failures);
        });
    }

    /// <summary>
    /// Gets the adverb of the platform's permanent-delete command for questions: "immediately" for "Delete
    /// Immediately" on macOS, "permanently" for "Delete Permanently" on Windows.
    /// </summary>
    /// <returns>The adverb.</returns>
    private string PermanentDeleteAdverb() =>
        Platform.PermanentDeleteName.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, .., var last] ? last.ToLowerInvariant() : "permanently";

    /// <summary>
    /// Runs a file operation: marks the view model busy, allows Stop to cancel it, then refreshes every pane and
    /// shows the operation's summary in the status bar.
    /// </summary>
    /// <param name="operation">The operation; returns the summary to show.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    private async Task RunOperationAsync(Func<CancellationToken, Task<string>> operation)
    {
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsOperationRunning = true;
        string summary;
        try
        {
            summary = await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            summary = "Stopped";
        }
        finally
        {
            _operationCancellation = null;
            IsOperationRunning = false;
        }

        _chartedScope = default;
        Refresh();
        StatusText = summary;
    }

    /// <summary>
    /// Waits for folders being scanned into the tree, refreshing the panes meanwhile.
    /// </summary>
    /// <param name="grafts">The scans.</param>
    /// <param name="cancellationToken">Stops the scans.</param>
    /// <returns>A task that completes when every scan has finished.</returns>
    private async Task WaitForGraftsAsync(List<ScanSession> grafts, CancellationToken cancellationToken)
    {
        if (grafts.Count == 0)
        {
            return;
        }

        using var registration = cancellationToken.Register(() => grafts.ForEach(static graft => graft.Cancel()));
        using var refreshCancellation = new CancellationTokenSource();
        var refreshLoop = RunRefreshLoopAsync(refreshCancellation.Token);
        try
        {
            await Task.WhenAll(grafts.Select(static graft => graft.Completion));
        }
        finally
        {
            await refreshCancellation.CancelAsync();
            await refreshLoop;
            grafts.ForEach(static graft => graft.Dispose());
        }
    }

    /// <summary>
    /// Updates the tree after one item was moved or copied.
    /// </summary>
    /// <param name="item">The item's node.</param>
    /// <param name="result">What happened to it.</param>
    /// <param name="destination">The folder it went to.</param>
    /// <param name="mode">Whether it was moved or copied.</param>
    /// <param name="sameVolume">Whether it stayed on its volume.</param>
    /// <param name="grafts">Receives scans of folders that must be read at their new place.</param>
    private void ApplyTransfer(FileSystemNode item, TransferResult result, DirectoryNode destination, TransferMode mode, bool sameVolume, List<ScanSession> grafts)
    {
        var path = result.Destination!;
        var name = Path.GetFileName(path);
        if (result.Replaced && destination.Children.FirstOrDefault(child => string.Equals(child.Name, name, Platform.PathComparison) && !ReferenceEquals(child, item)) is { } replaced)
        {
            RemoveFromTree(replaced);
        }

        if (mode == TransferMode.Move && sameVolume && name == item.Name && item.Parent is { } oldParent)
        {
            // A rename within the volume: the node and everything beneath it simply change folder.
            oldParent.RemoveChild(item);
            _fileTypeTotals.Invalidate(oldParent);
            item.Parent = destination;
            destination.InsertChild(item);
            _fileTypeTotals.Invalidate(destination);
            return;
        }

        if (mode == TransferMode.Move)
        {
            RemoveFromTree(item);
        }

        if (item is DirectoryNode)
        {
            grafts.Add(_scanner.StartInto(destination, name, _session?.Roots ?? []));
        }
        else if (_enumerator.GetEntry(path) is { } entry)
        {
            destination.InsertChild(FileNode.FromEntry(destination, entry));
        }

        _fileTypeTotals.Invalidate(destination);
    }

    /// <summary>
    /// Removes a node from the tree after the item was deleted or moved away.
    /// </summary>
    /// <param name="node">The node.</param>
    private void RemoveFromTree(FileSystemNode node)
    {
        if (node.Parent is { } parent)
        {
            parent.RemoveChild(node);
            _fileTypeTotals.Invalidate(parent);
        }
    }

    /// <summary>
    /// Selects the given rows only.
    /// </summary>
    /// <param name="rows">The rows.</param>
    private void SelectRows(IEnumerable<TreeRowViewModel> rows)
    {
        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            foreach (var row in rows)
            {
                if (Tree.IndexOf(row) is var index and >= 0)
                {
                    Selection.Select(index);
                }
            }
        }
    }

    /// <summary>
    /// Runs a file-manager action, reporting a failure in the status bar.
    /// </summary>
    /// <param name="description">What the action does, for the failure message.</param>
    /// <param name="action">The action.</param>
    private void RunShellAction(string description, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            StatusText = $"Could not {description}: {exception.Message}";
        }
    }

    /// <summary>
    /// Re-evaluates whether each item command is enabled.
    /// </summary>
    private void NotifyItemCommands()
    {
        foreach (var command in new IRelayCommand[]
        {
            OpenCommand, OpenWithCommand, RevealCommand, QuickLookCommand, GetInfoCommand, CopyItemsCommand,
            CopyPathCommand, SelectAllCommand, ExpandAllCommand, CollapseAllCommand, RescanCommand, MoveToTrashCommand,
            DeletePermanentlyCommand, FindDuplicatesCommand,
        })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Re-evaluates the item commands when a file operation starts or ends.
    /// </summary>
    /// <param name="value">Whether an operation is running.</param>
    partial void OnIsOperationRunningChanged(bool value) => NotifyItemCommands();

    /// <summary>
    /// Re-evaluates the item commands when the tree gains or loses focus.
    /// </summary>
    /// <param name="value">Whether the tree has focus.</param>
    partial void OnIsTreeActiveChanged(bool value) => NotifyItemCommands();

    /// <summary>
    /// Gets the paths of the selected items, keeping only the topmost of nested items.
    /// </summary>
    /// <returns>The paths.</returns>
    private List<string> SelectedPaths() => [.. Topmost(SelectedRows.Select(static row => row.Node)).Select(static node => node.FullPath)];

    /// <summary>
    /// Keeps only the nodes that are not beneath another of the nodes, in their original order.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    /// <returns>The topmost nodes.</returns>
    private static FileSystemNode[] Topmost(IEnumerable<FileSystemNode> nodes)
    {
        var all = nodes.ToHashSet<FileSystemNode>(ReferenceEqualityComparer.Instance);
        return [.. all.Where(node => !node.SelfAndAncestors.Skip(1).Any(all.Contains))];
    }

    /// <summary>
    /// Determines whether an item may be deleted, moved or copied: not a top-level item, not a mount point and not on
    /// a read-only volume.
    /// </summary>
    /// <param name="node">The item.</param>
    /// <returns><see langword="true"/> when the item may be changed.</returns>
    private static bool IsModifiable(FileSystemNode node) =>
        node is not (RootNode or PlaceholderNode) && node.Parent is not null && !node.Volume.IsReadOnly;

    /// <summary>
    /// Appends failures, if any, to an operation's summary.
    /// </summary>
    /// <param name="summary">What succeeded.</param>
    /// <param name="failures">What failed, one entry per item.</param>
    /// <returns>The status text.</returns>
    private static string Summarise(string summary, List<string> failures) => failures.Count switch
    {
        0 => summary,
        1 => $"{summary}; 1 item failed – {failures[0]}",
        _ => $"{summary}; {failures.Count} items failed – {failures[0]} …",
    };

    /// <summary>
    /// Determines whether the platform supports a file manager action.
    /// </summary>
    /// <param name="capability">The action.</param>
    /// <returns><see langword="true"/> when supported.</returns>
    private bool Supports(ShellCapabilities capability) => (_shell.Capabilities & capability) == capability;

    /// <summary>
    /// Determines whether there is a selection to act on from the keyboard or a menu.
    /// </summary>
    /// <returns><see langword="true"/> when the tree is active and something is selected.</returns>
    private bool HasActionableSelection() => IsTreeActive && Selection.Count > 0;

    /// <summary>
    /// Determines whether Open is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanOpen() => HasActionableSelection() && Supports(ShellCapabilities.Open);

    /// <summary>
    /// Determines whether Open With is enabled.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanOpenWith(ApplicationChoice? application) => application is not null && HasActionableSelection() && Supports(ShellCapabilities.OpenWith);

    /// <summary>
    /// Determines whether Show in Finder is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanReveal() => HasActionableSelection() && Supports(ShellCapabilities.Reveal);

    /// <summary>
    /// Determines whether Quick Look is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanQuickLook() => HasActionableSelection() && Supports(ShellCapabilities.QuickLook);

    /// <summary>
    /// Determines whether Get Info is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanGetInfo() => HasActionableSelection() && Supports(ShellCapabilities.ShowInfo);

    /// <summary>
    /// Determines whether Copy is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanCopyItems() => HasActionableSelection() && Supports(ShellCapabilities.CopyItems);

    /// <summary>
    /// Determines whether Copy Path is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanCopyPath() => HasActionableSelection() && Supports(ShellCapabilities.CopyText);

    /// <summary>
    /// Determines whether Select All is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanSelectAll() => IsTreeActive && Tree.Rows.Count > 0;

    /// <summary>
    /// Determines whether a selected row is a folder that can be expanded or collapsed.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool HasSelectedFolders() => IsTreeActive && Selection.SelectedItems.Any(static row => row is { IsExpandable: true });

    /// <summary>
    /// Determines whether Rescan is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanRescan() =>
        IsTreeActive && !IsScanning && !IsOperationRunning && _session is not null
        && Selection.SelectedItems.Any(static row => row?.Node is DirectoryNode);

    /// <summary>
    /// Determines whether the selection may be deleted or moved.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanModifySelection()
    {
        if (!HasActionableSelection() || IsScanning || IsOperationRunning)
        {
            return false;
        }

        var items = Topmost(SelectedRows.Select(static row => row.Node));
        return items.Length > 0 && items.All(IsModifiable);
    }

    /// <summary>
    /// Determines whether Move to Trash is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanMoveToTrash() => Supports(ShellCapabilities.Trash) && CanModifySelection();

    /// <summary>
    /// Determines whether Delete Immediately is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanDeletePermanently() => CanModifySelection();
}
