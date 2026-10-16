using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Renders the main window headlessly with real scan results. Set the <c>WHATDISK_SCREENSHOTS</c> environment variable
/// to a directory to keep the rendered frames for review.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// After a scan the tree shows the root and its children, and every pane is laid out with the right pane taking
    /// about a fifth of the width.
    /// </summary>
    /// <param name="variant">The theme variant to render.</param>
    /// <returns>A task that completes when the window has been rendered.</returns>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task MainWindow_ShowsScanResults(string variant)
    {
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var (window, viewModel) = await ShowScannedWindowAsync();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(list.ItemCount >= 5);
        var realized = list.GetRealizedContainers().Count();
        Assert.True(realized >= 5, $"Expected realized rows, found {realized}.");

        var chartPane = window.GetVisualDescendants().OfType<ChartPane>().Single();
        var share = chartPane.Bounds.Width / window.Bounds.Width;
        Assert.InRange(share, 0.15, 0.25);
        Assert.True(viewModel.Chart.HasData);

        Capture(window, $"main-window-{variant.ToLowerInvariant()}.png");
        window.Close();
    }

    /// <summary>
    /// Clicking a column header sorts the tree by that column.
    /// </summary>
    /// <returns>A task that completes when the window has been rendered.</returns>
    [AvaloniaFact]
    public async Task ColumnHeader_SortsTheTree()
    {
        var (window, viewModel) = await ShowScannedWindowAsync();
        var nameHeader = window.GetVisualDescendants().OfType<Button>().First(static button => button.Classes.Contains("column-header"));

        nameHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new TreeSortOrder(TreeSortColumn.Name, false), viewModel.SortOrder);
        window.Close();
    }

    /// <summary>
    /// Selecting a folder updates the chart and details panes on screen.
    /// </summary>
    /// <returns>A task that completes when the window has been rendered.</returns>
    [AvaloniaFact]
    public async Task Selection_ShowsDetails()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var (window, viewModel) = await ShowScannedWindowAsync();

        viewModel.SelectedRow = viewModel.Tree.Rows.First(static row => row.Name == "Projects");
        await viewModel.WaitForChartAsync();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.Equal("Projects", viewModel.Details.Title);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), static text => text.Text == "Size on disk");
        Capture(window, "main-window-selection.png");
        window.Close();
    }

    /// <summary>
    /// Creates a scanned view model over a realistic fake tree and shows it in a window.
    /// </summary>
    /// <returns>The window and its view model.</returns>
    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowScannedWindowAsync()
    {
        var fs = new FakeFileSystem();
        var random = new Random(3);
        string[] folders = ["Projects", "Photos", "Music", "Movies", "Downloads", "Documents", "Library", "Archive"];
        string[] extensions = [".mov", ".jpg", ".mp3", ".zip", ".pdf", ".dmg", ".json", ".heic", ".m4a", ".docx"];
        for (var index = 0; index < folders.Length; index++)
        {
            for (var file = 0; file < 6; file++)
            {
                var extension = extensions[(index + file) % extensions.Length];
                fs.File($"{folders[index]}/item{file}{extension}", random.Next(1, 40) * (index + 1) * 50_000_000L, allocatedSize: FileSystemEntryInfo.UnknownSize);
            }
        }

        fs.File("Projects/whatdisk/src/app.cs", 120_000).File("notes.txt", 4_000);
        fs.Fail("Library/Private", new UnauthorizedAccessException("Permission denied"));
        var volumes = new FakeVolumeService();
        volumes.Mounts.Add(new MountEntry(fs.Root, new VolumeInfo
        {
            Id = "disk",
            MountPoint = fs.Root,
            Name = "Macintosh HD",
            TotalBytes = 1_000_000_000_000,
            AvailableBytes = 380_000_000_000,
            Kind = VolumeKind.Fixed,
            FileSystem = "apfs",
        }));

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = new MainWindowViewModel(new DiskScanner(fs, volumes, time), volumes, new FakeFolderPicker(), PlatformCapabilities.Generic, time)
        {
            PathText = fs.Root,
        };
        await viewModel.StartScanCommand.ExecuteAsync(null);
        await viewModel.WaitForChartAsync();

        var window = new MainWindow { Width = 1280, Height = 800, DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (window, viewModel);
    }

    /// <summary>
    /// Renders the window and, when requested, saves the frame.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="fileName">The file name for the frame.</param>
    private static void Capture(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("WHATDISK_SCREENSHOTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Join(directory, fileName), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
