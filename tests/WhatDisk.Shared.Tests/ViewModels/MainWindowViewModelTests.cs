using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Time.Testing;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the main window view model: choosing targets, running scans and keeping the panes in step.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    /// <summary>The in-memory file system.</summary>
    private readonly FakeFileSystem _fs = new();

    /// <summary>The volume service.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>The folder picker.</summary>
    private readonly FakeFolderPicker _picker = new();

    /// <summary>Controls the refresh timer.</summary>
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));

    /// <summary>The view model under test, created lazily so tests can choose the platform.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModelTests"/> class with a small tree on a 10 kB volume.
    /// </summary>
    public MainWindowViewModelTests()
    {
        _fs.File("photos/a.jpg", 3000).File("photos/b.jpg", 1000).File("music/song.mp3", 500).File("notes.txt", 100);
        _volumes.Mounts.Add(new MountEntry(_fs.Root, new VolumeInfo
        {
            Id = "disk",
            MountPoint = _fs.Root,
            Name = "Test Disk",
            TotalBytes = 10_000,
            AvailableBytes = 2_000,
            Kind = VolumeKind.Fixed,
        }));
    }

    /// <summary>
    /// Gets the view model, creating it with single-volume capabilities on first use.
    /// </summary>
    private MainWindowViewModel ViewModel => _viewModel ??= Create(PlatformCapabilities.Generic);

    /// <summary>
    /// Before any scan the view model is idle and cannot scan without a path.
    /// </summary>
    [AvaloniaFact]
    public void Initially_IsIdleAndNeedsAPath()
    {
        Assert.False(ViewModel.IsScanning);
        Assert.False(ViewModel.HasResults);
        Assert.False(ViewModel.StartScanCommand.CanExecute(null));
        Assert.Contains("Scan", ViewModel.StatusText, StringComparison.Ordinal);

        ViewModel.PathText = _fs.Root;
        Assert.True(ViewModel.StartScanCommand.CanExecute(null));
    }

    /// <summary>
    /// A completed scan fills the tree (with the root expanded), the chart and the status bar.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task StartScan_PopulatesEveryPane()
    {
        ViewModel.PathText = _fs.Root;

        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.False(ViewModel.IsScanning);
        Assert.True(ViewModel.HasResults);
        var rows = ViewModel.Tree.Rows;
        Assert.Equal(["Test Disk", "photos", "music", "notes.txt"], rows.Select(static row => row.Name));
        Assert.True(rows[0].IsExpanded);
        Assert.Equal(0.46, rows[0].PercentOfDisk, precision: 9);
        await ViewModel.WaitForChartAsync();
        Assert.Equal("File types in Test Disk", ViewModel.Chart.Title);
        Assert.Equal([".jpg", ".mp3", ".txt"], ViewModel.Chart.Legend.Select(static entry => entry.Label));
        Assert.Equal("4.60 KB", ViewModel.Chart.TotalText);
        Assert.StartsWith("Scanned 3 folders, 4 files", ViewModel.StatusText, StringComparison.Ordinal);
        Assert.Null(ViewModel.ErrorSummary);
        Assert.Same(rows[0], ViewModel.SelectedRow);
        Assert.Equal("Test Disk", ViewModel.Details.Title);
    }

    /// <summary>
    /// When a scan finishes, the first row is selected and the view is asked to focus it.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task StartScan_FocusesTheFirstRowWhenDone()
    {
        var focused = new List<TreeRowViewModel>();
        ViewModel.FocusRequested += (_, row) => focused.Add(row);
        ViewModel.PathText = _fs.Root;

        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Same(ViewModel.Tree.Rows[0], Assert.Single(focused));
        Assert.Same(ViewModel.Tree.Rows[0], ViewModel.SelectedRow);
    }

    /// <summary>
    /// A row the user selected while the scan was running keeps the selection and receives the focus.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task StartScan_KeepsASelectionMadeDuringTheScan()
    {
        using var release = new ManualResetEventSlim();
        _fs.OnRead = (path, token) =>
        {
            if (path.EndsWith("music", StringComparison.Ordinal))
            {
                release.Wait(token);
            }
        };
        TreeRowViewModel? focused = null;
        ViewModel.FocusRequested += (_, row) => focused = row;
        ViewModel.PathText = _fs.Root;
        var scan = ViewModel.StartScanCommand.ExecuteAsync(null);
        await Eventually.True(() => ViewModel.Tree.Rows.Any(static row => row.Name == "photos"), "the photos row is visible");

        var photos = ViewModel.Tree.Rows.Single(static row => row.Name == "photos");
        ViewModel.SelectedRow = photos;
        release.Set();
        await scan;

        Assert.Same(photos, ViewModel.SelectedRow);
        Assert.Same(photos, focused);
    }

    /// <summary>
    /// The panes refresh on the timer while a scan is still running.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task Refresh_UpdatesThePanesWhileScanning()
    {
        using var release = new ManualResetEventSlim();
        _fs.OnRead = (path, token) =>
        {
            if (path.EndsWith("music", StringComparison.Ordinal))
            {
                release.Wait(token);
            }
        };
        ViewModel.PathText = _fs.Root;

        var scan = ViewModel.StartScanCommand.ExecuteAsync(null);
        await Eventually.True(() => ViewModel.IsScanning && ViewModel.Session?.Statistics.FileCount >= 3, "the scan is under way");
        _time.Advance(MainWindowViewModel.RefreshInterval);
        await Eventually.True(() => ViewModel.StatusText.StartsWith("Scanning", StringComparison.Ordinal) && ViewModel.Tree.Rows.Count == 4, "the timer refreshed the panes");

        Assert.True(ViewModel.Tree.Rows[0].IsPending);
        Assert.StartsWith(_fs.Root, ViewModel.ProgressPath, StringComparison.Ordinal);

        release.Set();
        await scan;
        Assert.False(ViewModel.Tree.Rows[0].IsPending);
        Assert.Null(ViewModel.ProgressPath);
    }

    /// <summary>
    /// Stopping a scan keeps the partial results and says the scan was stopped.
    /// </summary>
    /// <returns>A task that completes when the scan has stopped.</returns>
    [AvaloniaFact]
    public async Task StopScan_KeepsPartialResults()
    {
        _fs.OnRead = (path, token) =>
        {
            if (path.EndsWith("music", StringComparison.Ordinal))
            {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            }
        };
        ViewModel.PathText = _fs.Root;
        var scan = ViewModel.StartScanCommand.ExecuteAsync(null);
        await Eventually.True(() => ViewModel.StopScanCommand.CanExecute(null), "the scan has started");

        ViewModel.StopScanCommand.Execute(null);
        await scan;

        Assert.StartsWith("Scan stopped", ViewModel.StatusText, StringComparison.Ordinal);
        Assert.True(ViewModel.HasResults);
        Assert.False(ViewModel.StopScanCommand.CanExecute(null));
    }

    /// <summary>
    /// Selecting a folder charts its file types and shows its details; selecting a file charts its folder and
    /// highlights the file's type.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task SelectedRow_DrivesTheChartAndDetails()
    {
        ViewModel.PathText = _fs.Root;
        await ViewModel.StartScanCommand.ExecuteAsync(null);
        var photos = ViewModel.Tree.Rows[1];

        ViewModel.SelectedRow = photos;
        await ViewModel.WaitForChartAsync();
        Assert.Equal("File types in photos", ViewModel.Chart.Title);
        Assert.Equal([".jpg"], ViewModel.Chart.Legend.Select(static entry => entry.Label));
        Assert.Equal("photos", ViewModel.Details.Title);
        Assert.True(ViewModel.Details.HasSelection);

        photos.IsExpanded = true;
        var file = ViewModel.Tree.Rows.Single(static row => row.Name == "a.jpg");
        ViewModel.SelectedRow = file;
        await ViewModel.WaitForChartAsync();
        Assert.Equal("File types in photos", ViewModel.Chart.Title);
        Assert.Equal(".jpg", ViewModel.Chart.HighlightedKey);
        Assert.True(ViewModel.Chart.Legend[0].IsHighlighted);
        Assert.Equal("File", ViewModel.Details.Subtitle);
    }

    /// <summary>
    /// Collapsing the folder of the selected row moves the selection to the folder.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task SelectedRow_MovesToTheNearestVisibleAncestorWhenHidden()
    {
        ViewModel.PathText = _fs.Root;
        await ViewModel.StartScanCommand.ExecuteAsync(null);
        var photos = ViewModel.Tree.Rows[1];
        photos.IsExpanded = true;
        ViewModel.SelectedRow = ViewModel.Tree.Rows[2];

        photos.IsExpanded = false;

        Assert.Same(photos, ViewModel.SelectedRow);
    }

    /// <summary>
    /// Selecting a node from the chart reveals it in the tree and asks the view to scroll to it.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task SelectNode_RevealsAndScrollsToTheNode()
    {
        ViewModel.PathText = _fs.Root;
        await ViewModel.StartScanCommand.ExecuteAsync(null);
        TreeRowViewModel? scrolledTo = null;
        ViewModel.ScrollIntoViewRequested += (_, row) => scrolledTo = row;
        var song = ((DirectoryNode)ViewModel.Session!.Roots[0].Children.Single(static child => child.Name == "music")).Children[0];

        ViewModel.SelectNodeCommand.Execute(song);

        Assert.Same(song, ViewModel.SelectedRow?.Node);
        Assert.Same(ViewModel.SelectedRow, scrolledTo);
    }

    /// <summary>
    /// Sorting by a column re-sorts the tree and toggles direction on a second click.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task Sort_ReordersTheTree()
    {
        ViewModel.PathText = _fs.Root;
        await ViewModel.StartScanCommand.ExecuteAsync(null);

        ViewModel.SortCommand.Execute(TreeSortColumn.Name);
        Assert.Equal(["music", "notes.txt", "photos"], ViewModel.Tree.Rows.Skip(1).Select(static row => row.Name));
        Assert.Equal(new TreeSortOrder(TreeSortColumn.Name, false), ViewModel.SortOrder);

        ViewModel.SortCommand.Execute(TreeSortColumn.Name);
        Assert.Equal(["photos", "notes.txt", "music"], ViewModel.Tree.Rows.Skip(1).Select(static row => row.Name));
    }

    /// <summary>
    /// Where several volumes can be scanned together, the checked volumes are scanned as separate roots.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task CheckedVolumes_AreScannedTogetherWhenSupported()
    {
        _viewModel = Create(PlatformCapabilities.Generic with { SupportsMultipleVolumeSelection = true });
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("p", _fs.PathOf("photos")));
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("m", _fs.PathOf("music")));
        await ViewModel.LoadVolumesCommand.ExecuteAsync(null);

        ViewModel.Volumes[0].IsChecked = true;
        ViewModel.Volumes[1].IsChecked = true;
        Assert.Equal($"{_fs.PathOf("photos")}; {_fs.PathOf("music")}", ViewModel.PathText);
        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Equal([_fs.PathOf("photos"), _fs.PathOf("music")], ViewModel.Session!.Roots.Select(static root => root.RootPath));
        await ViewModel.WaitForChartAsync();
        Assert.Equal($"File types in {_fs.PathOf("photos")}", ViewModel.Chart.Title);

        ViewModel.SelectedRow = null;
        await ViewModel.WaitForChartAsync();
        Assert.Equal("File types in all scanned volumes", ViewModel.Chart.Title);
    }

    /// <summary>
    /// Typing a path after checking volumes scans the typed path instead.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task TypedPath_TakesPrecedenceOverCheckedVolumes()
    {
        _viewModel = Create(PlatformCapabilities.Generic with { SupportsMultipleVolumeSelection = true });
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("p", _fs.PathOf("photos")));
        await ViewModel.LoadVolumesCommand.ExecuteAsync(null);
        ViewModel.Volumes[0].IsChecked = true;

        ViewModel.PathText = _fs.PathOf("music");
        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Equal(_fs.PathOf("music"), Assert.Single(ViewModel.Session!.Roots).RootPath);
    }

    /// <summary>
    /// Choosing a single volume from the picker scans it straight away.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task ScanVolume_ScansTheChosenVolume()
    {
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("m", _fs.PathOf("music")));
        await ViewModel.LoadVolumesCommand.ExecuteAsync(null);

        await ViewModel.ScanVolumeCommand.ExecuteAsync(ViewModel.Volumes[0]);

        Assert.Equal(_fs.PathOf("music"), ViewModel.PathText);
        Assert.Equal(500, ViewModel.Session!.Roots[0].Size);
    }

    /// <summary>
    /// Loading volumes keeps the ones that were checked.
    /// </summary>
    /// <returns>A task that completes when the volumes have loaded twice.</returns>
    [AvaloniaFact]
    public async Task LoadVolumes_KeepsCheckedVolumes()
    {
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("a", "/a"));
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("b", "/b"));
        await ViewModel.LoadVolumesCommand.ExecuteAsync(null);
        ViewModel.Volumes[1].IsChecked = true;

        await ViewModel.LoadVolumesCommand.ExecuteAsync(null);

        Assert.Equal(2, _volumes.SelectableCalls);
        Assert.Equal([false, true], ViewModel.Volumes.Select(static volume => volume.IsChecked));
    }

    /// <summary>
    /// Asking for the volumes again while they are still loading (opening the volume menu just after start-up)
    /// replaces the first request quietly instead of failing.
    /// </summary>
    /// <returns>A task that completes when both requests have finished.</returns>
    [AvaloniaFact]
    public async Task LoadVolumes_ToleratesOverlappingRequests()
    {
        _volumes.Selectable.Add(FakeVolumeService.CreateVolume("a", "/a"));
        _volumes.SelectableDelay = TimeSpan.FromMilliseconds(300);

        var first = ViewModel.LoadVolumesCommand.ExecuteAsync(null);
        var second = ViewModel.LoadVolumesCommand.ExecuteAsync(null);
        await Task.WhenAll(first, second);

        Assert.Single(ViewModel.Volumes);
        Assert.False(ViewModel.IsLoadingVolumes);
    }

    /// <summary>
    /// Browsing scans the chosen folder; cancelling the dialog does nothing.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task Browse_ScansTheChosenFolder()
    {
        await ViewModel.BrowseCommand.ExecuteAsync(null);
        Assert.Null(ViewModel.Session);

        _picker.Answer = _fs.PathOf("photos");
        await ViewModel.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(_fs.PathOf("photos"), ViewModel.PathText);
        Assert.Equal(4000, ViewModel.Session!.Roots[0].Size);
        Assert.True(ViewModel.CanBrowse);
    }

    /// <summary>
    /// Folders that cannot be read are summarised in the status bar.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [AvaloniaFact]
    public async Task ErrorSummary_CountsUnreadableFolders()
    {
        _fs.Fail("locked", new UnauthorizedAccessException("denied"));
        ViewModel.PathText = _fs.Root;

        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Equal("1 folder could not be read", ViewModel.ErrorSummary);
        Assert.True(ViewModel.Tree.Rows.Single(static row => row.Name == "locked").HasError);
    }

    /// <summary>
    /// Starting a new scan replaces the previous results.
    /// </summary>
    /// <returns>A task that completes when both scans have finished.</returns>
    [AvaloniaFact]
    public async Task StartScan_ReplacesPreviousResults()
    {
        ViewModel.PathText = _fs.PathOf("photos");
        await ViewModel.StartScanCommand.ExecuteAsync(null);
        ViewModel.SelectedRow = ViewModel.Tree.Rows[0];

        ViewModel.PathText = _fs.PathOf("music");
        await ViewModel.StartScanCommand.ExecuteAsync(null);

        Assert.Equal(_fs.PathOf("music"), ViewModel.Tree.Rows[0].Name);
        Assert.Same(ViewModel.Tree.Rows[0], ViewModel.SelectedRow);
    }

    /// <summary>
    /// Disposes the view model, stopping any scan.
    /// </summary>
    public void Dispose() => _viewModel?.Dispose();

    /// <summary>
    /// Creates the view model.
    /// </summary>
    /// <param name="platform">The platform capabilities.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel Create(PlatformCapabilities platform) =>
        new(new DiskScanner(_fs, _volumes, _time), _volumes, _picker, platform, _time);
}
