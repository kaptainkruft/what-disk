using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Tests the tree's mouse, keyboard, context menu and drag-and-drop handling in a headless window, scanning real files
/// in a temporary directory.
/// </summary>
public sealed class TreeInteractionTests : IDisposable
{
    /// <summary>The scanned directory, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-ui-");

    /// <summary>The scanned directory's path with symbolic links resolved.</summary>
    private readonly string _base;

    /// <summary>The volumes.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>Records the shell actions.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>Answers the questions.</summary>
    private readonly ScriptedDialogService _dialogs = new();

    /// <summary>The window under test.</summary>
    private MainWindow? _window;

    /// <summary>The view model under test.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeInteractionTests"/> class with <c>a.txt</c>,
    /// <c>b/c.txt</c>, <c>b/inner/g.txt</c> and <c>d/e.txt</c>.
    /// </summary>
    public TreeInteractionTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", "/");
        Write("a.txt", 40);
        Write("b/c.txt", 30);
        Write("b/inner/g.txt", 20);
        Write("d/e.txt", 10);
    }

    /// <summary>
    /// Enter does nothing; Space previews the selection; Option+Right and Option+Left expand and collapse everything
    /// beneath the selected folder.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Keys_QuickLookAndExpandAll_EnterDoesNothing()
    {
        var (window, viewModel) = await ShowAsync();
        var b = Row(viewModel, "b");
        Focus(window, b);

        Press(window, Key.Enter);
        Assert.False(b.IsExpanded);

        Press(window, Key.Space);
        Assert.Equal([$"quick look: {PathOf("b")}"], _shell.Calls);

        Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Contains(viewModel.Tree.Rows, static row => row.Name == "g.txt");

        Press(window, Key.Left, RawInputModifiers.Alt);
        Assert.False(b.IsExpanded);
        Assert.DoesNotContain(viewModel.Tree.Rows, static row => row.Name == "c.txt");
    }

    /// <summary>
    /// Where no menu bar takes the shortcuts, the tree runs them itself: Command+Backspace moves to the Trash.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task CommandShortcuts_RunFromTheTree()
    {
        var (window, viewModel) = await ShowAsync();
        Focus(window, Row(viewModel, "a.txt"));

        Press(window, Key.C, ToRaw(ItemShortcuts.CommandKey | KeyModifiers.Alt));
        Press(window, Key.Back, ToRaw(ItemShortcuts.CommandKey));
        await Eventually.True(() => !viewModel.IsOperationRunning && !File.Exists(PathOf("a.txt")), "the file is in the Trash");

        Assert.Equal([$"copy text: {PathOf("a.txt")}", $"trash: {PathOf("a.txt")}"], _shell.Calls);
    }

    /// <summary>
    /// The item commands follow keyboard focus: focusing the path box disables them, so its own copy and delete keys
    /// work, and focusing the tree enables them again.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ItemCommands_FollowKeyboardFocus()
    {
        var (window, viewModel) = await ShowAsync();
        var a = Row(viewModel, "a.txt");
        Focus(window, a);
        Assert.True(viewModel.IsTreeActive);
        Assert.True(viewModel.CopyItemsCommand.CanExecute(null));

        window.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsTreeActive);
        Assert.False(viewModel.CopyItemsCommand.CanExecute(null));

        Focus(window, a);
        Assert.True(viewModel.IsTreeActive);
    }

    /// <summary>
    /// Shift+F10 opens the context menu with the item actions and the applications for Open With; with nothing
    /// selected it does not open.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ContextMenu_OpensFromTheKeyboard()
    {
        var (window, viewModel) = await ShowAsync();
        Focus(window, Row(viewModel, "a.txt"));
        var list = List(window);

        Press(window, Key.F10, RawInputModifiers.Shift);

        var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        Assert.True(menu.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Select(static item => item.Header as string).ToList();
        Assert.Equal(
            ["Open", "Open With", "Show in Finder", "Quick Look", "Get Info", "Copy", "Copy Path", "Expand All Beneath", "Collapse All Beneath", "Rescan", "Move to Trash", "Delete Immediately…"],
            headers);
        var openWith = menu.Items.OfType<MenuItem>().Single(static item => item.Header as string == "Open With");
        Assert.Equal(["Preview (default)", "TextEdit"], openWith.Items.OfType<MenuItem>().Select(static item => item.Header as string));
        Assert.Equal(ItemShortcuts.MoveToTrash, menu.Items.OfType<MenuItem>().Single(static item => item.Header as string == "Move to Trash").InputGesture);
        menu.Close();

        viewModel.Selection.Clear();
        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.False(menu.IsOpen);
    }

    /// <summary>
    /// Shift-click selects a range, the command key toggles rows, and right-clicking an unselected row selects just it.
    /// Pressing a row of a multiple selection keeps the selection until release, so it can be dragged.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Mouse_SelectsRanges()
    {
        var (window, viewModel) = await ShowAsync();
        var rows = viewModel.Tree.Rows.Skip(1).Take(3).ToArray();

        Click(window, rows[0]);
        Click(window, rows[2], RawInputModifiers.Shift);
        Assert.Equal(rows, viewModel.SelectedRows);

        var point = PointOf(window, rows[1]);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, viewModel.Selection.Count);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([rows[1]], viewModel.SelectedRows);

        Click(window, viewModel.Tree.Rows[0], button: MouseButton.Right);
        Assert.Equal([viewModel.Tree.Rows[0]], viewModel.SelectedRows);
        List(window).ContextMenu?.Close();
    }

    /// <summary>
    /// Dragging rows over a folder highlights it and dropping moves them there; a refused target is not highlighted.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task DragAndDrop_HighlightsAndMoves()
    {
        var (window, viewModel) = await ShowAsync();
        var a = Row(viewModel, "a.txt");
        var c = Row(viewModel, "b");
        var d = Row(viewModel, "d");
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TreeListView.RowsFormat, new[] { a }));
        const DragDropEffects Allowed = DragDropEffects.Move | DragDropEffects.Copy;

        window.DragDrop(PointOf(window, a), RawDragEventType.DragEnter, data, Allowed, RawInputModifiers.None);
        window.DragDrop(PointOf(window, a), RawDragEventType.DragOver, data, Allowed, RawInputModifiers.None);
        Assert.False(a.IsDropTarget);

        window.DragDrop(PointOf(window, d), RawDragEventType.DragOver, data, Allowed, RawInputModifiers.None);
        Assert.True(d.IsDropTarget);
        window.DragDrop(PointOf(window, c), RawDragEventType.DragOver, data, Allowed, RawInputModifiers.None);
        Assert.False(d.IsDropTarget);
        Assert.True(c.IsDropTarget);

        window.DragDrop(PointOf(window, d), RawDragEventType.DragOver, data, Allowed, RawInputModifiers.None);
        window.DragDrop(PointOf(window, d), RawDragEventType.Drop, data, Allowed, RawInputModifiers.None);
        Assert.False(d.IsDropTarget);
        await Eventually.True(() => !viewModel.IsOperationRunning && File.Exists(PathOf("d/a.txt")), "the file has moved");
        Assert.Contains(a.Node, ((DirectoryNode)d.Node).Children);
    }

    /// <summary>
    /// Closes the window and deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        _window?.Close();
        _viewModel?.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Converts key modifiers to raw input modifiers.
    /// </summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>The raw modifiers.</returns>
    private static RawInputModifiers ToRaw(KeyModifiers modifiers) => (RawInputModifiers)(int)modifiers;

    /// <summary>
    /// Finds the tree's list.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The list.</returns>
    private static ListBox List(Window window) => window.GetVisualDescendants().OfType<ListBox>().Single(static list => list.Name == "RowsList");

    /// <summary>
    /// Gets a point inside a row, in window coordinates.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="row">The row.</param>
    /// <returns>The point.</returns>
    private static Point PointOf(Window window, TreeRowViewModel row)
    {
        var container = List(window).ContainerFromItem(row) ?? throw new InvalidOperationException($"{row.Name} has no container.");
        return container.TranslatePoint(new Point(120, container.Bounds.Height / 2), window) ?? throw new InvalidOperationException("No point.");
    }

    /// <summary>
    /// Clicks a row.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="row">The row.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="button">The mouse button.</param>
    private static void Click(Window window, TreeRowViewModel row, RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
    {
        var point = PointOf(window, row);
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Selects a row and gives it keyboard focus.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="row">The row.</param>
    private void Focus(Window window, TreeRowViewModel row)
    {
        _viewModel!.SelectedRow = row;
        Dispatcher.UIThread.RunJobs();
        Assert.True(List(window).ContainerFromItem(row)?.Focus());
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Presses and releases a key.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Scans the temporary directory and shows the results in a window.
    /// </summary>
    /// <returns>The window and its view model.</returns>
    private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync()
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
        };
        _viewModel = viewModel;
        await viewModel.StartScanCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = viewModel };
        _window = window;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    /// Makes a node's row visible and returns it.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="relative">The path relative to the scanned directory.</param>
    /// <returns>The row.</returns>
    private static TreeRowViewModel Row(MainWindowViewModel viewModel, string relative)
    {
        var node = viewModel.Tree.RootRows[0].Node;
        foreach (var name in relative.Split('/'))
        {
            node = ((DirectoryNode)node).Children.Single(child => child.Name == name);
        }

        viewModel.SelectNodeCommand.Execute(node);
        Dispatcher.UIThread.RunJobs();
        return viewModel.Tree.FindVisible(node) ?? throw new InvalidOperationException($"{relative} is not visible.");
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
    private void Write(string relative, int bytes)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }
}
