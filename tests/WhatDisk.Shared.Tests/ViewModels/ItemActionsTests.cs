using Avalonia.Headless.XUnit;
using WhatDisk.FileOperations;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the actions on selected items (deleting, dragging, rescanning, shell actions) against real files in a
/// temporary directory, with a recording shell and scripted dialogs.
/// </summary>
public sealed class ItemActionsTests : IDisposable
{
    /// <summary>The scanned directory, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-actions-");

    /// <summary>The scanned directory's path with symbolic links resolved, as the scanner reports it.</summary>
    private readonly string _base;

    /// <summary>The volumes: the whole file system, plus a second volume mounted at <c>vol2</c>.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>Records the shell actions.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>Answers the questions.</summary>
    private readonly ScriptedDialogService _dialogs = new();

    /// <summary>The view model, once scanned.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemActionsTests"/> class with a small tree:
    /// <c>a.txt</c>, <c>b/c.txt</c>, <c>d/e.txt</c> and the second volume <c>vol2/f.txt</c>.
    /// </summary>
    public ItemActionsTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", "/");
        _volumes.Mount("vol2", PathOf("vol2"));
        Write("a.txt");
        Write("b/c.txt");
        Write("d/e.txt");
        Write("vol2/f.txt");
    }

    /// <summary>
    /// Deleting a small selection does not ask; the item disappears from disk and from the tree, and the totals drop.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task MoveToTrash_SmallSelection_DoesNotAsk()
    {
        var viewModel = await ScanAsync();
        var root = (DirectoryNode)viewModel.Tree.RootRows[0].Node;
        var files = root.FileCount;
        Select(viewModel, "a.txt");

        await viewModel.MoveToTrashCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Confirmations);
        Assert.Equal([$"trash: {PathOf("a.txt")}"], _shell.Calls);
        Assert.False(File.Exists(PathOf("a.txt")));
        Assert.DoesNotContain(root.Children, static child => child.Name == "a.txt");
        Assert.Equal(files - 1, root.FileCount);
        Assert.StartsWith("Moved 1 item to the Trash", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(viewModel.Tree.Rows, static row => row.Name == "a.txt");
    }

    /// <summary>
    /// Deleting more than 10 files or more than 10 MB asks first, for both kinds of delete; declining leaves everything
    /// in place and confirming deletes.
    /// </summary>
    /// <param name="permanently">Whether to delete immediately rather than move to the Trash.</param>
    /// <param name="large">Whether the folder is one large file rather than many small ones.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Delete_AboveTheThreshold_Asks(bool permanently, bool large)
    {
        if (large)
        {
            Write("big/huge.bin", 11 * 1024 * 1024);
        }
        else
        {
            for (var index = 0; index < 11; index++)
            {
                Write($"big/{index}.txt");
            }
        }

        var viewModel = await ScanAsync();
        Select(viewModel, "big");
        var command = permanently ? viewModel.DeleteImmediatelyCommand : viewModel.MoveToTrashCommand;

        _dialogs.Confirm = false;
        await command.ExecuteAsync(null);
        var request = Assert.Single(_dialogs.Confirmations);
        Assert.Equal(permanently, request.IsDestructive);
        Assert.Contains("“big”", request.Title, StringComparison.Ordinal);
        Assert.True(Directory.Exists(PathOf("big")));
        Assert.Empty(_shell.Calls);

        _dialogs.Confirm = true;
        await command.ExecuteAsync(null);
        Assert.False(Directory.Exists(PathOf("big")));
        Assert.Equal(permanently ? 0 : 1, _shell.Calls.Count);
        Assert.DoesNotContain(viewModel.Tree.Rows, static row => row.Name == "big");
    }

    /// <summary>
    /// Ten small files are under the threshold and delete without asking.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task DeleteImmediately_TenSmallFiles_DoesNotAsk()
    {
        for (var index = 0; index < 10; index++)
        {
            Write($"ten/{index}.txt");
        }

        var viewModel = await ScanAsync();
        Select(viewModel, "ten");

        await viewModel.DeleteImmediatelyCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Confirmations);
        Assert.False(Directory.Exists(PathOf("ten")));
        Assert.Empty(_shell.Calls);
    }

    /// <summary>
    /// Item commands are enabled only while the tree is active, and top-level items can be opened but not deleted.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Commands_FollowTheActivePaneAndTheSelection()
    {
        var viewModel = await ScanAsync();
        Select(viewModel, "a.txt");
        Assert.True(viewModel.OpenCommand.CanExecute(null));
        Assert.True(viewModel.MoveToTrashCommand.CanExecute(null));

        viewModel.IsTreeActive = false;
        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.False(viewModel.CopyItemsCommand.CanExecute(null));
        Assert.False(viewModel.MoveToTrashCommand.CanExecute(null));

        viewModel.IsTreeActive = true;
        viewModel.SelectedRow = viewModel.Tree.RootRows[0];
        Assert.True(viewModel.OpenCommand.CanExecute(null));
        Assert.False(viewModel.MoveToTrashCommand.CanExecute(null));
        Assert.False(viewModel.DeleteImmediatelyCommand.CanExecute(null));

        _shell.Capabilities = ShellCapabilities.Open;
        Select(viewModel, "a.txt");
        Assert.False(viewModel.MoveToTrashCommand.CanExecute(null));
        Assert.True(viewModel.DeleteImmediatelyCommand.CanExecute(null));
        Assert.False(viewModel.RevealCommand.CanExecute(null));
    }

    /// <summary>
    /// Shell actions receive the topmost selected items only: a folder and a file inside it give just the folder.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ShellActions_UseTheTopmostSelectedItems()
    {
        var viewModel = await ScanAsync();
        Select(viewModel, "b", "b/c.txt", "a.txt");

        viewModel.RevealCommand.Execute(null);
        viewModel.CopyPathCommand.Execute(null);
        viewModel.OpenWithCommand.Execute(viewModel.GetOpenWithChoices()[1]);

        var revealed = _shell.Calls[0]["reveal: ".Length..].Split('|');
        Assert.Equal([PathOf("a.txt"), PathOf("b")], revealed.Order(StringComparer.Ordinal));
        var copied = _shell.Calls[1]["copy text: ".Length..].Split('\n');
        Assert.Equal([PathOf("a.txt"), PathOf("b")], copied.Order(StringComparer.Ordinal));
        Assert.StartsWith("open with TextEdit: ", _shell.Calls[2], StringComparison.Ordinal);
        Assert.Equal("Copied 2 paths to the clipboard", viewModel.StatusText);
        Assert.Equal("3 items", viewModel.Details.Title);
    }

    /// <summary>
    /// Dropping moves within a volume and copies across volumes; the modifiers override that; dropping onto the
    /// item itself, a descendant, a file, its own folder (for a move) or dragging a top-level item is refused.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task GetDropMode_FollowsTheRules()
    {
        var viewModel = await ScanAsync();
        var a = Reveal(viewModel, "a.txt");
        var b = Reveal(viewModel, "b");
        var c = Reveal(viewModel, "b/c.txt");
        var d = Reveal(viewModel, "d");
        var root = viewModel.Tree.RootRows[0];
        var vol2 = viewModel.Tree.RootRows.Single(row => row.Node.Volume.Id == "vol2");

        Assert.Equal(TransferMode.Move, viewModel.GetDropMode([a], d, false, false));
        Assert.Equal(TransferMode.Copy, viewModel.GetDropMode([a], d, true, false));
        Assert.Equal(TransferMode.Copy, viewModel.GetDropMode([a], vol2, false, false));
        Assert.Equal(TransferMode.Move, viewModel.GetDropMode([a], vol2, false, true));
        Assert.Equal(TransferMode.Move, viewModel.GetDropMode([c], root, false, false));
        Assert.Null(viewModel.GetDropMode([a], root, false, false));
        Assert.Equal(TransferMode.Copy, viewModel.GetDropMode([a], root, true, false));
        Assert.Null(viewModel.GetDropMode([b], b, false, false));
        Assert.Null(viewModel.GetDropMode([root], d, false, false));
        Assert.Null(viewModel.GetDropMode([a], c, false, false));
        Assert.Null(viewModel.GetDropMode([a], null, false, false));
        Assert.False(viewModel.CanDrag([root]));
        Assert.True(viewModel.CanDrag([a, c]));
    }

    /// <summary>
    /// Dragging a file onto a folder of the same volume moves it on disk and in the tree, keeping the totals right.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Drop_MovesWithinTheVolume()
    {
        var viewModel = await ScanAsync();
        var a = Reveal(viewModel, "a.txt");
        var d = Reveal(viewModel, "d");
        var root = (DirectoryNode)viewModel.Tree.RootRows[0].Node;
        var (rootSize, rootFiles) = (root.Size, root.FileCount);

        await viewModel.DropAsync([a], d, false, false);

        Assert.True(File.Exists(PathOf("d/a.txt")));
        Assert.False(File.Exists(PathOf("a.txt")));
        var folder = (DirectoryNode)d.Node;
        Assert.Contains(a.Node, folder.Children);
        Assert.Same(folder, a.Node.Parent);
        Assert.Equal(2, folder.FileCount);
        Assert.Equal((rootSize, rootFiles), (root.Size, root.FileCount));
        Assert.StartsWith("Moved 1 item to “d”", viewModel.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dragging a folder onto another volume copies it; the copy is scanned into the tree and the original stays.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Drop_CopiesAcrossVolumes()
    {
        var viewModel = await ScanAsync();
        var b = Reveal(viewModel, "b");
        var vol2 = viewModel.Tree.RootRows.Single(row => row.Node.Volume.Id == "vol2");

        await viewModel.DropAsync([b], vol2, false, false);

        Assert.True(File.Exists(PathOf("vol2/b/c.txt")));
        Assert.True(File.Exists(PathOf("b/c.txt")));
        var copy = Assert.IsAssignableFrom<DirectoryNode>(Assert.Single(((DirectoryNode)vol2.Node).Children, static child => child.Name == "b"));
        Assert.Equal(1, copy.FileCount);
        Assert.Equal(2, vol2.Node.FileCount);
        Assert.Contains(b.Node, ((DirectoryNode)viewModel.Tree.RootRows[0].Node).Children);
    }

    /// <summary>
    /// A name already taken at the destination is resolved by the dialog: Keep Both adds a numbered copy and Skip
    /// leaves both folders alone.
    /// </summary>
    /// <param name="resolution">The answer to the conflict.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData(ConflictResolution.KeepBoth)]
    [InlineData(ConflictResolution.Skip)]
    public async Task Drop_AsksAboutConflicts(ConflictResolution resolution)
    {
        Write("d/a.txt");
        _dialogs.Conflict = new ConflictDecision(resolution);
        var viewModel = await ScanAsync();
        var a = Reveal(viewModel, "a.txt");
        var d = Reveal(viewModel, "d");

        await viewModel.DropAsync([a], d, false, false);

        Assert.Single(_dialogs.Conflicts);
        var names = ((DirectoryNode)d.Node).Children.Select(static child => child.Name).Order(StringComparer.Ordinal);
        if (resolution == ConflictResolution.KeepBoth)
        {
            Assert.Equal(["a 2.txt", "a.txt", "e.txt"], names);
            Assert.True(File.Exists(PathOf("d/a 2.txt")));
            Assert.False(File.Exists(PathOf("a.txt")));
        }
        else
        {
            Assert.Equal(["a.txt", "e.txt"], names);
            Assert.True(File.Exists(PathOf("a.txt")));
        }
    }

    /// <summary>
    /// Rescanning a folder reads it again, picking up changes made outside the app, and keeps it selected.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Rescan_ReadsTheFolderAgain()
    {
        var viewModel = await ScanAsync();
        Select(viewModel, "b");
        Write("b/new.txt");
        Write("b/sub/deeper.txt");

        await viewModel.RescanCommand.ExecuteAsync(null);

        var b = Find(viewModel, "b");
        Assert.Equal(3, b.FileCount);
        Assert.Same(b, viewModel.SelectedRow?.Node);
        Assert.Equal("Rescanned 1 folder", viewModel.StatusText);
    }

    /// <summary>
    /// Select All selects every row, and Expand All Beneath opens every folder below the selection.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task SelectAllAndExpandAll()
    {
        Write("b/x/y/z.txt");
        var viewModel = await ScanAsync();
        viewModel.SelectedRow = viewModel.Tree.RootRows[0];

        viewModel.ExpandAllCommand.Execute(null);
        Assert.Contains(viewModel.Tree.Rows, static row => row.Name == "z.txt");

        viewModel.SelectAllCommand.Execute(null);
        Assert.Equal(viewModel.Tree.Rows.Count, viewModel.Selection.Count);

        viewModel.SelectedRow = viewModel.Tree.RootRows[0];
        viewModel.CollapseAllCommand.Execute(null);
        Assert.Equal(viewModel.Tree.RootRows.Count, viewModel.Tree.Rows.Count);
        Assert.False(Reveal(viewModel, "b/x").IsExpanded);
    }

    /// <summary>
    /// Deletes the temporary directory and stops the view model.
    /// </summary>
    public void Dispose()
    {
        _viewModel?.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Scans the temporary directory.
    /// </summary>
    /// <returns>The view model, with the tree active.</returns>
    private async Task<MainWindowViewModel> ScanAsync()
    {
        var viewModel = new MainWindowViewModel(
            new DiskScanner(new ManagedFileSystemEnumerator(), _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            PlatformCapabilities.Generic,
            TimeProvider.System,
            shell: _shell,
            dialogs: _dialogs)
        {
            PathText = _temp.FullName,
            IsTreeActive = true,
        };
        _viewModel = viewModel;
        await viewModel.StartScanCommand.ExecuteAsync(null);
        Assert.Equal(ScanStatus.Completed, viewModel.Session?.Status);
        return viewModel;
    }

    /// <summary>
    /// Finds a node of the first tree by its path relative to the scanned directory.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="relative">The relative path, with forward slashes.</param>
    /// <returns>The node.</returns>
    private static FileSystemNode Find(MainWindowViewModel viewModel, string relative)
    {
        var node = viewModel.Tree.RootRows[0].Node;
        foreach (var name in relative.Split('/'))
        {
            node = ((DirectoryNode)node).Children.Single(child => child.Name == name);
        }

        return node;
    }

    /// <summary>
    /// Makes a node's row visible and returns it.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="relative">The relative path.</param>
    /// <returns>The row.</returns>
    private static TreeRowViewModel Reveal(MainWindowViewModel viewModel, string relative)
    {
        var node = Find(viewModel, relative);
        viewModel.SelectNodeCommand.Execute(node);
        return viewModel.Tree.FindVisible(node) ?? throw new InvalidOperationException($"{relative} is not visible.");
    }

    /// <summary>
    /// Selects exactly the given items.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="relatives">The relative paths.</param>
    private static void Select(MainWindowViewModel viewModel, params string[] relatives)
    {
        var rows = relatives.Select(relative => Reveal(viewModel, relative)).ToList();
        using (viewModel.Selection.BatchUpdate())
        {
            viewModel.Selection.Clear();
            foreach (var row in rows)
            {
                viewModel.Selection.Select(viewModel.Tree.IndexOf(row));
            }
        }
    }

    /// <summary>
    /// Gets the absolute path of an item in the scanned directory.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <returns>The absolute path.</returns>
    private string PathOf(string relative) => Path.Join(_base, relative);

    /// <summary>
    /// Writes a file, creating its folders.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="bytes">The file's length.</param>
    private void Write(string relative, int bytes = 10)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }
}
