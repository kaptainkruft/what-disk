using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using WhatDisk.Duplicates;
using WhatDisk.FileOperations;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Tests the main window's tabs and in-window dialogs: everything the app shows lives in one window, so the macOS
/// menu bar never switches between windows' menus. Set the <c>WHATDISK_SCREENSHOTS</c> environment variable to a
/// directory to keep the rendered frames for review.
/// </summary>
public sealed class MainWindowTabsTests
{
    /// <summary>Records shell actions.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>
    /// The window starts with the Scan tab alone, which cannot be closed; results open in a new, selected tab with a
    /// close button, whose page replaces the scan's; closing it shows the tab before it again.
    /// </summary>
    [AvaloniaFact]
    public void ResultsTab_OpensSelectedAndCloses()
    {
        var (window, viewModel) = Show(TestPlatforms.MacOS);
        var strip = window.GetVisualDescendants().OfType<TabStrip>().Single();
        Assert.Equal(["Scan"], Headers(viewModel));
        Assert.False(viewModel.CloseTabCommand.CanExecute(null));
        Assert.False(CloseButtons(window).Single().IsVisible);

        var results = Results("/Users/me/Photos", "Photos");
        viewModel.OpenTab(results);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Scan", "Duplicates in Photos"], Headers(viewModel));
        Assert.Same(results, strip.SelectedItem);
        Assert.False(window.FindControl<DockPanel>("ScanPage")!.IsVisible);
        Assert.True(window.GetVisualDescendants().OfType<DuplicatesView>().Single().IsVisible);
        Assert.Equal([false, true], CloseButtons(window).Select(static button => button.IsVisible));
        Capture(window, "main-window-tabs.png");

        CloseButtons(window)[1].Command!.Execute(results);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Scan"], Headers(viewModel));
        Assert.Same(viewModel.ScanTab, viewModel.SelectedTab);
        Assert.True(window.FindControl<DockPanel>("ScanPage")!.IsVisible);
        Assert.Empty(window.GetVisualDescendants().OfType<DuplicatesView>());
        window.Close();
    }

    /// <summary>
    /// Closing the selected tab shows the one before it; leaving the Scan tab makes the tree inactive, so its item
    /// commands do not act on a tree that cannot be seen; Close Tab (Ctrl+W on Windows) closes the tab shown.
    /// </summary>
    [AvaloniaFact]
    public void Tabs_CloseInOrder_AndLeaveTheTreeInactive()
    {
        var (window, viewModel) = Show(TestPlatforms.Windows);
        viewModel.IsTreeActive = true;
        var first = Results("/a", "a");
        var second = Results("/b", "b");
        viewModel.OpenTab(first);
        viewModel.OpenTab(second);
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsTreeActive);

        window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.W, RawInputModifiers.Control, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Scan", "Duplicates in a"], Headers(viewModel));
        Assert.Same(first, viewModel.SelectedTab);
        Assert.True(first.IsSelected);
        Assert.False(viewModel.ScanTab.IsSelected);

        viewModel.SelectedTab = viewModel.ScanTab;
        viewModel.CloseTabCommand.Execute(first);
        Assert.Same(viewModel.ScanTab, viewModel.SelectedTab);
        Assert.Equal(["Scan"], Headers(viewModel));
        window.Close();
    }

    /// <summary>
    /// Settings opens in one tab: asking again shows that tab rather than opening another, and closing it releases it.
    /// </summary>
    [AvaloniaFact]
    public void Settings_OpensInOneTab()
    {
        var (window, viewModel) = Show(TestPlatforms.MacOS);

        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.SelectedTab = viewModel.ScanTab;
        viewModel.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var settings = Assert.Single(viewModel.Tabs.OfType<SettingsViewModel>());
        Assert.Same(settings, viewModel.SelectedTab);
        Assert.Equal(["Scan", "Settings"], Headers(viewModel));
        Assert.True(window.GetVisualDescendants().OfType<SettingsView>().Single().IsVisible);

        viewModel.CloseTabCommand.Execute(null);
        Assert.Equal(["Scan"], Headers(viewModel));
        window.Close();
    }

    /// <summary>
    /// Closing a results tab while it is hashing stops the hashing.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ClosingAResultsTab_StopsHashing()
    {
        var folder = Directory.CreateTempSubdirectory("whatdisk-tab-close-");
        try
        {
            var content = new byte[8_000_000];
            new Random(1).NextBytes(content);
            string[] paths = [Path.Join(folder.FullName, "a"), Path.Join(folder.FullName, "b")];
            foreach (var path in paths)
            {
                await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
            }

            var (window, viewModel) = Show(TestPlatforms.MacOS);
            var results = Results(folder.FullName, "folder", new DuplicateSet(1, content.Length, null, paths, DuplicateComparison.Samples));
            viewModel.OpenTab(results);
            var verifying = results.VerifyAllCommand.ExecuteAsync(null);
            Assert.True(results.IsVerifying);

            viewModel.CloseTabCommand.Execute(results);
            await verifying;

            Assert.False(results.IsVerifying);
            Assert.DoesNotContain(results, viewModel.Tabs);
            window.Close();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A question appears over the window, which is dimmed and disabled, and the commands wait meanwhile; Escape gives
    /// the cancel answer and the default button answers otherwise; focus returns afterwards.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Questions_AppearInsideTheWindow()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        var dialogs = new WindowDialogService(() => window);
        var viewModel = CreateViewModel(TestPlatforms.MacOS, dialogs);
        window.DataContext = viewModel;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var layer = window.FindControl<Panel>("DialogLayer")!;
        var frame = window.FindControl<DockPanel>("Frame")!;

        var cancelled = dialogs.ConfirmAsync(new ConfirmationRequest("Delete “big”?", "It can’t be undone.", "Delete", IsDestructive: true));
        Dispatcher.UIThread.RunJobs();

        Assert.True(layer.IsVisible);
        Assert.False(frame.IsEnabled);
        Assert.True(viewModel.IsDialogOpen);
        Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
        var question = window.GetVisualDescendants().OfType<QuestionView>().Single();
        Assert.Equal("Delete “big”?", question.Heading);
        Capture(window, "main-window-question.png");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(await cancelled);
        Dispatcher.UIThread.RunJobs();
        Assert.False(layer.IsVisible);
        Assert.True(frame.IsEnabled);
        Assert.False(viewModel.IsDialogOpen);
        Assert.True(viewModel.OpenSettingsCommand.CanExecute(null));

        var confirmed = dialogs.ConfirmAsync(new ConfirmationRequest("Move “big” to the Trash?", "It can be restored.", "Move to Trash", IsDestructive: false));
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(static button => button.Content as string == "Move to Trash")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(await confirmed);
        window.Close();
    }

    /// <summary>
    /// The About box appears inside the window and closes with OK; a name conflict asked with no window to ask in is
    /// skipped.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task About_AppearsInsideTheWindow()
    {
        var (window, _) = Show(TestPlatforms.MacOS);

        var about = window.ShowAboutAsync();
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<AboutView>().Single();
        Assert.True(window.IsShowingDialog);
        view.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await about;

        Assert.False(window.IsShowingDialog);
        var hidden = new MainWindow();
        var decision = await new WindowDialogService(() => hidden).ResolveConflictAsync(new TransferConflict("/a", "/b/a", false));
        Assert.Equal(ConflictResolution.Skip, decision.Resolution);
        window.Close();
    }

    /// <summary>
    /// Gets the tabs' titles.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>The titles, in order.</returns>
    private static List<string> Headers(MainWindowViewModel viewModel) => [.. viewModel.Tabs.Select(static tab => tab.Header)];

    /// <summary>
    /// Gets the tab strip's close buttons, in order.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The buttons.</returns>
    private static List<Button> CloseButtons(Window window) =>
        [.. window.GetVisualDescendants().OfType<TabStrip>().Single().GetVisualDescendants().OfType<Button>().Where(static button => button.Classes.Contains("tab-close"))];

    /// <summary>
    /// Renders the window, keeping the frame when screenshots are requested.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="fileName">The screenshot's file name.</param>
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

    /// <summary>
    /// Creates a main window view model over an empty fake file system.
    /// </summary>
    /// <param name="platform">The platform's conventions.</param>
    /// <param name="dialogs">Asks questions, or <see langword="null"/>.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(PlatformCapabilities platform, IDialogService? dialogs = null)
    {
        var fs = new FakeFileSystem();
        var volumes = new FakeVolumeService();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return new MainWindowViewModel(new DiskScanner(fs, volumes, time), volumes, new FakeFolderPicker(), platform, time, shell: _shell, dialogs: dialogs, settings: new AppSettings(platform, null));
    }

    /// <summary>
    /// Shows a main window.
    /// </summary>
    /// <param name="platform">The platform's conventions.</param>
    /// <returns>The window and its view model.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel) Show(PlatformCapabilities platform)
    {
        var viewModel = CreateViewModel(platform);
        var window = new MainWindow { Width = 1000, Height = 700, DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    /// Creates duplicate search results.
    /// </summary>
    /// <param name="scope">What was searched, in full.</param>
    /// <param name="shortScope">What was searched, briefly.</param>
    /// <param name="sets">The sets.</param>
    /// <returns>The results.</returns>
    private DuplicateResultsViewModel Results(string scope, string shortScope, params DuplicateSet[] sets) => new(
        new DuplicateSearchResult(sets, 10, 4, 2, 2, 0, 0, 0, []),
        scope,
        new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture),
        _shell,
        new DuplicateFinder(),
        "Finder",
        DateTimeOffset.Now,
        shortScope);
}
