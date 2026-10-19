using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Settings;
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
        _volumes.QueriedVolume = _volumes.Mount("root", Path.GetPathRoot(_base)!);
        Write("a.txt", 40);
        Write("b/c.txt", 30);
        Write("b/inner/g.txt", 20);
        Write("d/e.txt", 10);
    }

    /// <summary>
    /// Enter does nothing; the platform's expand-all and collapse-all shortcuts open and close everything beneath the
    /// selected folder (on Windows, numeric-keypad * expands too).
    /// </summary>
    /// <param name="platformName">The conventions to follow: "macOS" or "Windows".</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task Keys_ExpandAndCollapseAll_EnterDoesNothing(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        var keys = viewModel.Platform.Shortcuts;
        var b = Row(viewModel, "b");
        Focus(window, b);

        Press(window, Key.Enter);
        Assert.False(b.IsExpanded);

        Press(window, keys.ExpandAll);
        Assert.Contains(viewModel.Tree.Rows, static row => row.Name == "g.txt");

        Press(window, keys.CollapseAll);
        Assert.False(b.IsExpanded);
        Assert.DoesNotContain(viewModel.Tree.Rows, static row => row.Name == "c.txt");

        if (keys.ExpandAllAlternate is { } alternate)
        {
            Press(window, alternate);
            Assert.Contains(viewModel.Tree.Rows, static row => row.Name == "g.txt");
        }

        Assert.Empty(_shell.Calls);
    }

    /// <summary>
    /// Space toggles Quick Look on macOS; Windows has no previews, so Space does nothing there.
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task Space_PreviewsOnMacOSOnly(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        Focus(window, Row(viewModel, "b"));

        Press(window, Key.Space);

        Assert.Equal(platformName == "macOS" ? [$"quick look: {PathOf("b")}"] : [], _shell.Calls);
    }

    /// <summary>
    /// Where no native menu bar takes the shortcuts (here, and on Windows), the tree runs them itself, once each: the
    /// platform's Copy Path, information and delete-to-Trash shortcuts (⌥⌘C, ⌘I, ⌘⌫ on macOS; Ctrl+Shift+C,
    /// Alt+Enter, Delete on Windows).
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task CommandShortcuts_RunFromTheTree(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        var keys = viewModel.Platform.Shortcuts;
        Focus(window, Row(viewModel, "a.txt"));

        Press(window, keys.CopyPath);
        Press(window, keys.GetInfo);
        Press(window, keys.MoveToTrash);
        await Eventually.True(() => !viewModel.IsOperationRunning && !File.Exists(PathOf("a.txt")), "the file is in the Trash");

        Assert.Equal([$"copy text: {PathOf("a.txt")}", $"info: {PathOf("a.txt")}", $"trash: {PathOf("a.txt")}"], _shell.Calls);
        Assert.StartsWith($"Moved 1 item to the {viewModel.Platform.TrashName}", viewModel.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Find Duplicates in Selection (⌘D, Ctrl+D) compares the selected folders as one set, so copies between them are
    /// found; with one folder selected, a copy outside it is not. Each search's results open in a new tab, and going
    /// back to the Scan tab gives the tree focus again.
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task FindDuplicatesInSelection_ComparesTheSelectedFolders(string platformName)
    {
        Write("b/movie.bin", 5000);
        Write("d/movie copy.bin", 5000);
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        var keys = viewModel.Platform.Shortcuts;

        Focus(window, Row(viewModel, "b"));
        Press(window, keys.FindDuplicatesInSelection);
        await Eventually.True(() => viewModel.DuplicateTabs().Count == 1 && !viewModel.IsOperationRunning, "the search of b has finished");
        Assert.Empty(viewModel.DuplicateTabs()[0].Sets);
        Assert.Equal($"Duplicates in {PathOf("b")}", viewModel.DuplicateTabs()[0].Title);
        Assert.Same(viewModel.DuplicateTabs()[0], viewModel.SelectedTab);
        Assert.False(viewModel.IsTreeActive);

        // The results opened in their own tab; back on the Scan tab, the tree has focus again.
        viewModel.SelectedTab = viewModel.ScanTab;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsTreeActive);
        viewModel.Selection.Select(viewModel.Tree.IndexOf(viewModel.Tree.Rows.Single(static row => row.Name == "d")));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, viewModel.Selection.Count);
        Press(window, keys.FindDuplicatesInSelection);
        await Eventually.True(() => viewModel.DuplicateTabs().Count == 2 && !viewModel.IsOperationRunning, "the search of b and d has finished");

        var results = viewModel.DuplicateTabs()[1];
        Assert.Equal([PathOf("b/movie.bin"), PathOf("d/movie copy.bin")], Assert.Single(results.Sets).Paths);
        Assert.Contains(" and ", results.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// Find Duplicates in Selection is disabled for a single file, which cannot hold duplicates on its own; Find
    /// Duplicates in Scanned Files searches everything, and on Windows its shortcut (Ctrl+Shift+D) works from
    /// anywhere in the window.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicatesInScannedFiles_SearchesEverything()
    {
        Write("b/movie.bin", 5000);
        Write("d/movie copy.bin", 5000);
        var (window, viewModel) = await ShowAsync(TestPlatforms.Windows);
        Focus(window, Row(viewModel, "a.txt"));
        Assert.False(viewModel.FindDuplicatesInSelectionCommand.CanExecute(null));

        window.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        Press(window, viewModel.Platform.Shortcuts.FindDuplicatesInScan);
        await Eventually.True(() => viewModel.DuplicateTabs().Count == 1 && !viewModel.IsOperationRunning, "the search has finished");

        Assert.Equal([PathOf("b/movie.bin"), PathOf("d/movie copy.bin")], Assert.Single(viewModel.DuplicateTabs()[0].Sets).Paths);
    }

    /// <summary>
    /// The permanent-delete and rescan shortcuts work too: Shift+Delete and F5 on Windows, ⌥⌘⌫ and ⇧⌘R on macOS.
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task DeleteAndRescanShortcuts_RunFromTheTree(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        var keys = viewModel.Platform.Shortcuts;
        Focus(window, Row(viewModel, "d"));
        Write("d/new.txt", 5);

        Press(window, keys.Rescan);
        await Eventually.True(() => !viewModel.IsOperationRunning && viewModel.StatusText == "Rescanned 1 folder", "the folder has been rescanned");
        Focus(window, viewModel.SelectedRow!);
        Assert.Equal(2, viewModel.SelectedRow?.Node.FileCount);

        Press(window, keys.DeletePermanently);
        await Eventually.True(() => !viewModel.IsOperationRunning && !Directory.Exists(PathOf("d")), "the folder is deleted");
        Assert.Empty(_shell.Calls);
    }

    /// <summary>
    /// The item commands follow keyboard focus: focusing the path box disables them, so its own copy and delete keys
    /// work, and focusing the tree enables them again. Moving into the window's menu bar, its drop-downs or the context
    /// menu keeps them enabled.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ItemCommands_FollowKeyboardFocus()
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Windows);
        var a = Row(viewModel, "a.txt");
        Focus(window, a);
        Assert.True(viewModel.IsTreeActive);
        Assert.True(viewModel.CopyItemsCommand.CanExecute(null));

        var fileMenu = window.GetVisualDescendants().OfType<MenuItem>().First(static item => item.Header as string == "_File");
        Assert.True(fileMenu.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsTreeActive);

        // Hovering or arrowing into the drop-down focuses its items, which are in a popup outside the menu bar's
        // visual tree; Win32 moves focus there, the headless platform does not, so the focus change is raised directly.
        fileMenu.Open();
        Dispatcher.UIThread.RunJobs();
        var open = fileMenu.GetLogicalChildren().OfType<MenuItem>().First();
        open.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent) { NewFocusedElement = open });
        Assert.True(viewModel.IsTreeActive);
        Assert.True(viewModel.CopyItemsCommand.CanExecute(null));
        fileMenu.Close();

        Focus(window, a);
        Press(window, Key.F10, RawInputModifiers.Shift);
        var contextMenu = Assert.IsType<ContextMenu>(List(window).ContextMenu);
        var copy = contextMenu.Items.OfType<MenuItem>().First(static item => item.Header as string == "Copy");
        copy.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent) { NewFocusedElement = copy });
        Assert.True(viewModel.IsTreeActive);
        Assert.True(copy.IsEffectivelyEnabled);
        contextMenu.Close();

        window.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsTreeActive);
        Assert.False(viewModel.CopyItemsCommand.CanExecute(null));

        Focus(window, a);
        Assert.True(viewModel.IsTreeActive);
    }

    /// <summary>
    /// Shift+F10 opens the context menu with the item actions in the platform's words and the applications for Open
    /// With; actions the platform lacks (Quick Look on Windows) are hidden; with nothing selected it does not open.
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task ContextMenu_OpensFromTheKeyboard(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        Focus(window, Row(viewModel, "a.txt"));
        var list = List(window);

        Press(window, Key.F10, RawInputModifiers.Shift);

        var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        Assert.True(menu.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Where(static item => item.IsVisible).Select(static item => item.Header as string).ToList();
        string[] expected = platformName == "macOS"
            ? ["Open", "Open With", "Show in Finder", "Quick Look", "Get Info", "Copy", "Copy Path", "Expand All Beneath", "Collapse All Beneath", "Rescan", "Find Duplicates in Selection", "Move to Trash", "Delete Immediately\u2026"]
            : ["Open", "Open With", "Show in File Explorer", "Properties", "Copy", "Copy Path", "Expand All Beneath", "Collapse All Beneath", "Rescan", "Find Duplicates in Selection", "Move to Recycle Bin", "Delete Permanently\u2026"];
        Assert.Equal(expected, headers);
        var openWith = menu.Items.OfType<MenuItem>().Single(static item => item.Header as string == "Open With");
        Assert.Equal(["Preview (default)", "TextEdit"], openWith.Items.OfType<MenuItem>().Select(static item => item.Header as string));
        var trash = menu.Items.OfType<MenuItem>().Single(item => item.Header as string == $"Move to {viewModel.Platform.TrashName}");
        Assert.Equal(viewModel.Platform.Shortcuts.MoveToTrash, trash.InputGesture);
        menu.Close();

        viewModel.Selection.Clear();
        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.False(menu.IsOpen);
    }

    /// <summary>
    /// With the full Windows context menu turned on, the context menu ends with Show More Options, which opens the
    /// system's menu for the selection once the list's menu has closed; it is disabled for items in different folders.
    /// Shift+right-click then opens the system's menu, with the extended commands, instead of the list's. With the
    /// setting off, the menu is unchanged and Shift+right-click opens the list's menu.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task SystemMenu_FollowsTheSetting()
    {
        _shell.Capabilities |= ShellCapabilities.SystemMenu;
        var (window, viewModel) = await ShowAsync(TestPlatforms.Windows);
        var a = Row(viewModel, "a.txt");
        Focus(window, a);
        var menu = Assert.IsType<ContextMenu>(List(window).ContextMenu);

        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.True(menu.IsOpen);
        Assert.DoesNotContain("Show More Options", VisibleHeaders(menu));
        menu.Close();
        Click(window, a, RawInputModifiers.Shift, MouseButton.Right);
        Assert.True(menu.IsOpen);
        menu.Close();

        viewModel.Settings.Set(SettingsCatalog.General.SystemContextMenu, true);
        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.Equal("Show More Options", VisibleHeaders(menu)[^1]);
        var showMore = menu.Items.OfType<MenuItem>().Single(static item => item.Header as string == "Show More Options");
        Assert.True(showMore.IsEnabled);
        showMore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(_shell.Calls);
        menu.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([$"system menu: {PathOf("a.txt")}"], _shell.Calls);

        var c = Row(viewModel, "b/c.txt");
        viewModel.Selection.Clear();
        viewModel.Selection.Select(viewModel.Tree.IndexOf(a));
        viewModel.Selection.Select(viewModel.Tree.IndexOf(c));
        Dispatcher.UIThread.RunJobs();
        Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.True(menu.IsOpen);
        Assert.False(showMore.IsEnabled);
        menu.Close();

        viewModel.SelectedRow = a;
        Dispatcher.UIThread.RunJobs();
        Click(window, a, RawInputModifiers.Shift, MouseButton.Right);
        Assert.False(menu.IsOpen);
        Assert.Equal($"system menu (extended): {PathOf("a.txt")}", _shell.Calls[^1]);
    }

    /// <summary>
    /// The window's menus are declared once, as Avalonia's documentation describes, and the same on every platform:
    /// File, Edit, View and Tools in the platform's words with its shortcuts (Find Duplicates enabled after the scan),
    /// and nothing changes them afterwards. Items for one platform are hidden on the other: Exit, Settings in Tools and
    /// the Help menu only where the menus are drawn in the window (Windows), Quick Look only on macOS. macOS drops the
    /// access key markers itself. Where the menus are drawn in the window, macOS calls Exit "Quit WhatDisk" (⌘Q).
    /// </summary>
    /// <param name="platformName">The conventions to follow.</param>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaTheory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public async Task MenuBar_FollowsThePlatform(string platformName)
    {
        var (window, viewModel) = await ShowAsync(TestPlatforms.Named(platformName));
        var platform = viewModel.Platform;
        var bar = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window));

        Assert.Equal(platform.HasApplicationMenu ? ["_File", "_Edit", "_View", "_Tools"] : ["_File", "_Edit", "_View", "_Tools", "_Help"], Visible(bar));
        var file = Items(bar, "_File");
        var tools = Items(bar, "_Tools");
        string[] expectedTools = ["Find Duplicates in Scanned Files", "Find Duplicates in Selection"];
        Assert.Equal(platform.HasApplicationMenu ? expectedTools : [.. expectedTools, "Settings\u2026"], Visible(Menu(bar, "_Tools")));
        Assert.Same(viewModel.FindDuplicatesInScanCommand, tools["Find Duplicates in Scanned Files"].Command);
        Assert.Equal(platform.Shortcuts.FindDuplicatesInScan, tools["Find Duplicates in Scanned Files"].Gesture);
        Assert.Same(viewModel.FindDuplicatesInSelectionCommand, tools["Find Duplicates in Selection"].Command);
        Assert.Equal(platform.Shortcuts.FindDuplicatesInSelection, tools["Find Duplicates in Selection"].Gesture);
        Assert.Equal(platform.Shortcuts.Settings, tools["Settings\u2026"].Gesture);
        Assert.True(viewModel.FindDuplicatesInScanCommand.CanExecute(null));
        Assert.Equal(platform.Shortcuts.Reveal, file[$"Show in {platform.FileManagerName}"].Gesture);
        Assert.Equal(platform.Shortcuts.GetInfo, file[platform.InfoCommandName].Gesture);
        Assert.Equal(platform.Shortcuts.MoveToTrash, file[$"Move to {platform.TrashName}"].Gesture);
        Assert.Equal(platform.Shortcuts.DeletePermanently, file[$"{platform.PermanentDeleteName}\u2026"].Gesture);
        Assert.Equal(platform.Shortcuts.Rescan, file["Rescan"].Gesture);
        Assert.Equal(platform.Shortcuts.CloseTab, file["Close Tab"].Gesture);
        var exit = file[platformName == "macOS" ? "Quit WhatDisk" : "E_xit"];
        Assert.Equal(!platform.HasApplicationMenu, exit.IsVisible);
        Assert.Equal(platform.Shortcuts.Quit, exit.Gesture);
        Assert.Equal(platformName == "macOS", file["Quick Look"].IsVisible);
        Assert.DoesNotContain("Open With", file.Keys);

        // Headless windows have no native menu bar, so the menus are drawn in the window, as on Windows.
        var inWindow = window.GetVisualDescendants().OfType<NativeMenuBar>().Single();
        Assert.True(inWindow.IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<MenuItem>(), static item => item.Header as string == "_File");

        static NativeMenu Menu(NativeMenu bar, string title) =>
            bar.Items.OfType<NativeMenuItem>().Single(item => item.Header == title).Menu!;

        static Dictionary<string, NativeMenuItem> Items(NativeMenu bar, string title) =>
            Menu(bar, title).Items.Where(static item => item is not NativeMenuItemSeparator).OfType<NativeMenuItem>().ToDictionary(static item => item.Header!, StringComparer.Ordinal);

        static List<string?> Visible(NativeMenu menu) =>
            [.. menu.Items.Where(static item => item is not NativeMenuItemSeparator).OfType<NativeMenuItem>().Where(static item => item.IsVisible).Select(static item => item.Header)];
    }

    /// <summary>
    /// With the macOS menu bar turned off, the window draws the menus with Quit WhatDisk (⌘Q), Settings and Help in
    /// them, as on Windows, and ⌘Q quits.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task MenuBar_InTheWindowOnMacOS()
    {
        var (window, _) = await ShowAsync(TestPlatforms.MacOS with { HasApplicationMenu = false });
        var bar = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window));
        var file = bar.Items.OfType<NativeMenuItem>().First().Menu!.Items.OfType<NativeMenuItem>().ToList();

        Assert.Equal(["_File", "_Edit", "_View", "_Tools", "_Help"], bar.Items.OfType<NativeMenuItem>().Where(static item => item.IsVisible).Select(static item => item.Header));
        var quit = file.Single(static item => item.Header == "Quit WhatDisk");
        Assert.True(quit.IsVisible);
        Assert.Equal(ShortcutScheme.MacOS.Quit, quit.Gesture);
        Assert.True(bar.Items.OfType<NativeMenuItem>().Single(static item => item.Header == "_Tools").Menu!.Items.OfType<NativeMenuItem>().Single(static item => item.Header == "Settings\u2026").IsVisible);

        // The window closes on the key press, so no key release follows.
        window.KeyPress(Key.Q, RawInputModifiers.Meta, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }

    /// <summary>
    /// Where the menus are exported to a native menu bar (macOS), the in-window menu bar is hidden and takes no space.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task MenuBar_IsHiddenWhenExportedNatively()
    {
        var (window, _) = await ShowAsync(TestPlatforms.MacOS);
        var inWindow = window.GetVisualDescendants().OfType<NativeMenuBar>().Single();
        Assert.True(inWindow.Bounds.Height > 0);

        window.SetValue(NativeMenu.IsNativeMenuExportedProperty, true);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, inWindow.Bounds.Height);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<MenuItem>(), static item => item.IsEffectivelyVisible);
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
    /// Presses and releases a shortcut.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="gesture">The shortcut.</param>
    private static void Press(Window window, KeyGesture gesture) => Press(window, gesture.Key, ToRaw(gesture.KeyModifiers));

    /// <summary>
    /// Converts key modifiers to raw input modifiers.
    /// </summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>The raw modifiers.</returns>
    private static RawInputModifiers ToRaw(KeyModifiers modifiers) => (RawInputModifiers)(int)modifiers;

    /// <summary>
    /// Gets the headers of a menu's visible items.
    /// </summary>
    /// <param name="menu">The menu.</param>
    /// <returns>The headers, in order.</returns>
    private static List<string?> VisibleHeaders(ContextMenu menu) =>
        [.. menu.Items.OfType<MenuItem>().Where(static item => item.IsVisible).Select(static item => item.Header as string)];

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
    /// <param name="platform">The platform conventions, or <see langword="null"/> for the generic ones.</param>
    /// <returns>The window and its view model.</returns>
    private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowAsync(PlatformCapabilities? platform = null)
    {
        platform ??= PlatformCapabilities.Generic;
        if (platform.Shortcuts.QuickLook is null)
        {
            // A platform without a preview shortcut (Windows) has no previews.
            _shell.Capabilities &= ~ShellCapabilities.QuickLook;
        }

        var viewModel = new MainWindowViewModel(
            new DiskScanner(new ManagedFileSystemEnumerator(), _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            platform,
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
    private string PathOf(string relative) => Path.Join(_base, relative.Replace('/', Path.DirectorySeparatorChar));

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
