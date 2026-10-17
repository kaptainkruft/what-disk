using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Tests keyboard navigation of the results tree with real key presses.
/// </summary>
public sealed class TreeKeyboardTests
{
    /// <summary>
    /// Right expands a collapsed folder, then moves to its first child; Left moves back to the folder, then collapses it.
    /// </summary>
    /// <returns>A task that completes when the keys have been processed.</returns>
    [AvaloniaFact]
    public async Task ArrowKeys_ExpandCollapseAndMoveThroughTheTree()
    {
        var (window, viewModel) = await ShowScannedWindowAsync();
        var photos = viewModel.Tree.Rows.Single(static row => row.Name == "photos");
        Select(window, viewModel, photos);

        Press(window, Key.Right);
        Assert.True(photos.IsExpanded);
        Assert.Same(photos, viewModel.SelectedRow);

        Press(window, Key.Right);
        Assert.Equal("a.jpg", viewModel.SelectedRow?.Name);

        Press(window, Key.Left);
        Assert.Same(photos, viewModel.SelectedRow);
        Assert.True(photos.IsExpanded);

        Press(window, Key.Left);
        Assert.False(photos.IsExpanded);
        Assert.Same(photos, viewModel.SelectedRow);

        Press(window, Key.Down);
        Assert.Equal("music", viewModel.SelectedRow?.Name);
        window.Close();
    }

    /// <summary>
    /// When a scan finishes, keyboard focus moves from the path box to the first row, so the arrow keys work at once.
    /// </summary>
    /// <returns>A task that completes when the keys have been processed.</returns>
    [AvaloniaFact]
    public async Task ScanCompletion_FocusesTheFirstRow()
    {
        var fs = new FakeFileSystem();
        fs.File("photos/a.jpg", 3000).File("music/song.mp3", 500);
        var volumes = new FakeVolumeService();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = new MainWindowViewModel(new DiskScanner(fs, volumes, time), volumes, new FakeFolderPicker(), PlatformCapabilities.Generic, time);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = viewModel };
        window.Show();
        var pathBox = window.GetVisualDescendants().OfType<TextBox>().Single();
        pathBox.Focus();
        viewModel.PathText = fs.Root;

        await viewModel.StartScanCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var focused = Assert.IsType<ListBoxItem>(window.FocusManager?.GetFocusedElement());
        var root = viewModel.Tree.Rows[0];
        Assert.Same(root, focused.DataContext);
        Assert.True(root.IsExpanded);

        Press(window, Key.Left);
        Assert.False(root.IsExpanded);
        window.Close();
    }

    /// <summary>
    /// Scans a small tree and shows it in a window.
    /// </summary>
    /// <returns>The window and its view model.</returns>
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowScannedWindowAsync()
    {
        var fs = new FakeFileSystem();
        fs.File("photos/a.jpg", 3000).File("photos/b.jpg", 1000).File("music/song.mp3", 500);
        var volumes = new FakeVolumeService();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = new MainWindowViewModel(new DiskScanner(fs, volumes, time), volumes, new FakeFolderPicker(), PlatformCapabilities.Generic, time)
        {
            PathText = fs.Root,
        };
        await viewModel.StartScanCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    /// Selects a row and gives its list item keyboard focus, as clicking it would.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="viewModel">The view model.</param>
    /// <param name="row">The row.</param>
    private static void Select(Window window, MainWindowViewModel viewModel, TreeRowViewModel row)
    {
        viewModel.SelectedRow = row;
        Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(list.ContainerFromItem(row)?.Focus());
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Presses and releases a key in the window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="key">The key.</param>
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
}
