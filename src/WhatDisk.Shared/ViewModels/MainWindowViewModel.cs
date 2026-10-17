using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Charting;
using WhatDisk.FileOperations;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.ViewModels;

/// <summary>
/// The main window: choosing what to scan, running the scan and presenting its results in the tree, chart and
/// details panes. While a scan runs, every pane is refreshed from the live scan tree every
/// <see cref="RefreshInterval"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Starts scans.</summary>
    private readonly IDiskScanner _scanner;

    /// <summary>Lists volumes.</summary>
    private readonly Volumes.IVolumeService _volumeService;

    /// <summary>Shows the native folder dialog.</summary>
    private readonly IFolderPicker _folderPicker;

    /// <summary>Drives the refresh timer.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Formats byte counts.</summary>
    private readonly ByteSizeFormatter _sizes;

    /// <summary>Describes file types and identifies packages.</summary>
    private readonly IFileTypeProvider _fileTypes;

    /// <summary>Totals file sizes by type for the chart.</summary>
    private readonly FileTypeAggregator _fileTypeTotals;

    /// <summary>The chart computation in progress, or the last one.</summary>
    private Task _chartUpdate = Task.CompletedTask;

    /// <summary>Guards <see cref="_chartBusy"/> and <see cref="_chartUpdatePending"/>.</summary>
    private readonly Lock _chartGate = new();

    /// <summary>Whether a chart computation is running.</summary>
    private bool _chartBusy;

    /// <summary>Whether the chart must be recomputed when the computation in progress finishes.</summary>
    private bool _chartUpdatePending;

    /// <summary>What the chart last showed, and whether the scan had finished by then.</summary>
    private (object? Scope, bool IsFinal) _chartedScope;

    /// <summary>The current (or most recent) scan.</summary>
    private ScanSession? _session;

    /// <summary>The roots list last given to the tree, to detect newly discovered volumes.</summary>
    private IReadOnlyList<RootNode>? _knownRoots;

    /// <summary>The selected rows when the tree started changing its rows.</summary>
    private List<TreeRowViewModel> _selectionBeforeReconcile = [];

    /// <summary>Whether the tree is changing its rows (the view may transiently clear the selection).</summary>
    private bool _isReconciling;

    /// <summary>Whether the path text was written by the volume picker rather than typed.</summary>
    private bool _pathFromVolumes;

    /// <summary>Whether path changes are being made by the view model itself.</summary>
    private bool _isSettingPath;

    /// <summary>Incremented whenever a scan is requested, so an older request can tell it was superseded.</summary>
    private int _scanGeneration;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="scanner">Starts scans.</param>
    /// <param name="volumeService">Lists volumes.</param>
    /// <param name="folderPicker">Shows the native folder dialog.</param>
    /// <param name="platform">Describes platform conventions.</param>
    /// <param name="timeProvider">Drives the refresh timer.</param>
    /// <param name="fileTypes">Describes file types; <see langword="null"/> for extensions without platform knowledge.</param>
    /// <param name="shell">The file manager's actions; <see langword="null"/> for opening only.</param>
    /// <param name="dialogs">Asks the user to confirm and resolve conflicts; <see langword="null"/> declines everything.</param>
    /// <param name="enumerator">Describes items added to the tree; <see langword="null"/> for the portable enumerator.</param>
    public MainWindowViewModel(
        IDiskScanner scanner,
        Volumes.IVolumeService volumeService,
        IFolderPicker folderPicker,
        PlatformCapabilities platform,
        TimeProvider timeProvider,
        IFileTypeProvider? fileTypes = null,
        IShellService? shell = null,
        IDialogService? dialogs = null,
        IFileSystemEnumerator? enumerator = null)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(volumeService);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _scanner = scanner;
        _volumeService = volumeService;
        _folderPicker = folderPicker;
        _timeProvider = timeProvider;
        Platform = platform;
        _sizes = new ByteSizeFormatter(platform.ByteUnits);
        _fileTypes = fileTypes ?? new DefaultFileTypeProvider();
        _fileTypeTotals = new FileTypeAggregator(_fileTypes);
        _shell = shell ?? new DefaultShellService();
        _dialogs = dialogs ?? new DecliningDialogService();
        _enumerator = enumerator ?? new ManagedFileSystemEnumerator();
        _transfer = new FileTransfer(platform.PathComparison);
        Tree = new TreeListModel(_sizes);
        Tree.Reconciling += OnTreeReconciling;
        Tree.Reconciled += OnTreeReconciled;
        Selection = new SelectionModel<TreeRowViewModel> { SingleSelect = false, Source = Tree.Rows };
        Selection.SelectionChanged += OnSelectionChanged;
        Chart = new PieChartViewModel(_sizes);
        Details = new SelectionDetailsViewModel(_sizes, platform);
        StatusText = IdleStatus;
    }

    /// <summary>
    /// Raised when the view should scroll a row into view, for example after a chart slice was clicked.
    /// </summary>
    public event EventHandler<TreeRowViewModel>? ScrollIntoViewRequested;

    /// <summary>
    /// Raised when the view should give a row keyboard focus, so the arrow keys work straight away (after a scan).
    /// </summary>
    public event EventHandler<TreeRowViewModel>? FocusRequested;

    /// <summary>
    /// Gets the interval between refreshes of the panes while a scan runs.
    /// </summary>
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets the platform conventions.
    /// </summary>
    public PlatformCapabilities Platform { get; }

    /// <summary>
    /// Gets the tree of results.
    /// </summary>
    public TreeListModel Tree { get; }

    /// <summary>
    /// Gets the pie chart pane.
    /// </summary>
    public PieChartViewModel Chart { get; }

    /// <summary>
    /// Gets the selection details pane.
    /// </summary>
    public SelectionDetailsViewModel Details { get; }

    /// <summary>
    /// Gets the volumes offered in the volume picker.
    /// </summary>
    public ObservableCollection<VolumeItemViewModel> Volumes { get; } = [];

    /// <summary>
    /// Gets a value indicating whether several volumes can be scanned together on this platform.
    /// </summary>
    public bool AllowsMultipleVolumes => Platform.SupportsMultipleVolumeSelection;

    /// <summary>
    /// Gets the hint shown in the empty path box.
    /// </summary>
    public string PathWatermark => Platform.PathPlaceholder;

    /// <summary>
    /// Gets a value indicating whether the native folder dialog is available.
    /// </summary>
    public bool CanBrowse => _folderPicker.IsSupported;

    /// <summary>
    /// Gets the current sort order of the tree.
    /// </summary>
    public TreeSortOrder SortOrder => Tree.SortOrder;

    /// <summary>
    /// Gets the session of the current or most recent scan.
    /// </summary>
    public ScanSession? Session => _session;

    /// <summary>
    /// Gets or sets the path (or network location) to scan.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    public partial string PathText { get; set; } = string.Empty;

    /// <summary>
    /// Gets the selected rows; the list binds to it, and several rows can be selected.
    /// </summary>
    public SelectionModel<TreeRowViewModel> Selection { get; }

    /// <summary>
    /// Gets or sets the primary selected row (the first of the selection). Setting it selects only that row.
    /// </summary>
    public TreeRowViewModel? SelectedRow
    {
        get => Selection.SelectedItem;
        set
        {
            if (ReferenceEquals(value, Selection.SelectedItem) && Selection.Count <= 1)
            {
                return;
            }

            using (Selection.BatchUpdate())
            {
                Selection.Clear();
                if (value is not null && Tree.IndexOf(value) is var index and >= 0)
                {
                    Selection.Select(index);
                }
            }
        }
    }

    /// <summary>
    /// Gets every selected row, in display order.
    /// </summary>
    public IReadOnlyList<TreeRowViewModel> SelectedRows =>
        [.. Selection.SelectedIndexes.Order().Where(index => index < Tree.Rows.Count).Select(index => Tree.Rows[index])];

    /// <summary>
    /// Gets a value indicating whether a scan is running.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanVolumeCommand))]
    public partial bool IsScanning { get; private set; }

    /// <summary>
    /// Re-evaluates the item commands when scanning starts or stops.
    /// </summary>
    /// <param name="value">Whether a scan is running.</param>
    partial void OnIsScanningChanged(bool value) => NotifyItemCommands();

    /// <summary>
    /// Gets a value indicating whether the tree has anything to show.
    /// </summary>
    [ObservableProperty]
    public partial bool HasResults { get; private set; }

    /// <summary>
    /// Gets the status bar summary.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; }

    /// <summary>
    /// Gets the directory currently being read, while a scan runs.
    /// </summary>
    [ObservableProperty]
    public partial string? ProgressPath { get; private set; }

    /// <summary>
    /// Gets a summary of directories that could not be read, if any.
    /// </summary>
    [ObservableProperty]
    public partial string? ErrorSummary { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the volume list is being loaded.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoadingVolumes { get; private set; }

    /// <summary>
    /// Gets the status shown before any scan has run.
    /// </summary>
    private static string IdleStatus => "Choose a volume or a folder, then press Scan.";

    /// <summary>
    /// Re-reads the live scan tree into every pane. Called by the refresh timer; safe to call at any time.
    /// </summary>
    public void Refresh()
    {
        if (_session is not { } session)
        {
            return;
        }

        var roots = session.Roots;
        if (!ReferenceEquals(roots, _knownRoots))
        {
            var isFirst = _knownRoots is null;
            _knownRoots = roots;
            Tree.SetRoots(roots);
            if (isFirst)
            {
                foreach (var row in Tree.RootRows.Where(static row => row.Node is RootNode { IsDiscovered: false }).ToArray())
                {
                    row.IsExpanded = true;
                }
            }
        }
        else
        {
            Tree.Refresh();
        }

        HasResults = Tree.Rows.Count > 0;
        if (!IsOperationRunning)
        {
            UpdateStatus(session);
        }

        UpdateChart();
        UpdateDetails();
    }

    /// <summary>
    /// Loads (or reloads) the volumes for the volume picker, keeping any that were checked. A newer request (for
    /// example opening the volume menu while the start-up load is still running) cancels an older one, which then
    /// ends quietly.
    /// </summary>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>A task that completes when the list has been loaded.</returns>
    [RelayCommand]
    public async Task LoadVolumesAsync(CancellationToken cancellationToken)
    {
        IsLoadingVolumes = true;
        try
        {
            var volumes = await Task.Run(_volumeService.GetSelectableVolumes, cancellationToken);
            var checkedIds = Volumes.Where(static item => item.IsChecked).Select(static item => item.Volume.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in Volumes)
            {
                item.PropertyChanged -= OnVolumeItemPropertyChanged;
            }

            Volumes.Clear();
            foreach (var volume in volumes)
            {
                var item = new VolumeItemViewModel(volume, _sizes) { IsChecked = checkedIds.Contains(volume.Id) };
                item.PropertyChanged += OnVolumeItemPropertyChanged;
                Volumes.Add(item);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded by a newer request, which owns the loading indicator now.
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = $"The list of volumes could not be read: {exception.Message}";
        }

        IsLoadingVolumes = false;
    }

    /// <summary>
    /// Stops the running scan, keeping the results gathered so far.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
        Tree.Reconciling -= OnTreeReconciling;
        Tree.Reconciled -= OnTreeReconciled;
    }

    /// <summary>
    /// Starts scanning the checked volumes (where several can be selected) or the path in the path box.
    /// </summary>
    /// <returns>A task that completes when the scan finishes.</returns>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private Task StartScanAsync() => RunScanAsync(GetTargets());

    /// <summary>
    /// Scans a single volume chosen from the volume picker.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <returns>A task that completes when the scan finishes.</returns>
    [RelayCommand(CanExecute = nameof(CanScanVolume))]
    private Task ScanVolumeAsync(VolumeItemViewModel? volume)
    {
        if (volume is null)
        {
            return Task.CompletedTask;
        }

        SetPathText(volume.MountPoint, fromVolumes: false);
        return RunScanAsync([volume.MountPoint]);
    }

    /// <summary>
    /// Stops the running scan or file operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopScan()
    {
        _session?.Cancel();
        _operationCancellation?.Cancel();
    }

    /// <summary>
    /// Determines whether the Stop button is enabled.
    /// </summary>
    /// <returns><see langword="true"/> while a scan or file operation runs.</returns>
    private bool CanStop() => IsScanning || IsOperationRunning;

    /// <summary>
    /// Lets the user choose a folder with the native dialog, then scans it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the dialog.</param>
    /// <returns>A task that completes when the scan finishes, or immediately if the dialog was cancelled.</returns>
    [RelayCommand]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        if (!_folderPicker.IsSupported)
        {
            return;
        }

        string? folder;
        try
        {
            folder = await _folderPicker.PickFolderAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        SetPathText(folder, fromVolumes: false);
        await RunScanAsync([folder]);
    }

    /// <summary>
    /// Sorts the tree by a column, reversing the direction when it is already sorted by that column.
    /// </summary>
    /// <param name="column">The column.</param>
    [RelayCommand]
    private void Sort(TreeSortColumn column)
    {
        Tree.SortOrder = Tree.SortOrder.Toggle(column);
        Tree.Refresh();
        OnPropertyChanged(nameof(SortOrder));
    }

    /// <summary>
    /// Selects a node in the tree, expanding its ancestors and scrolling it into view.
    /// </summary>
    /// <param name="node">The node to select.</param>
    [RelayCommand]
    private void SelectNode(FileSystemNode? node)
    {
        if (node is null)
        {
            return;
        }

        var target = node is PlaceholderNode { LinkedRoot: { } linked } && SelectedRow?.Node == node ? linked : node;
        if (Tree.Reveal(target) is { } row)
        {
            SelectedRow = row;
            ScrollIntoViewRequested?.Invoke(this, row);
        }
    }

    /// <summary>
    /// Expands or collapses a row.
    /// </summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    private static void ToggleExpansion(TreeRowViewModel? row)
    {
        if (row is { IsExpandable: true })
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    /// <summary>
    /// Runs a scan of the given paths until it finishes, refreshing the panes every <see cref="RefreshInterval"/>.
    /// </summary>
    /// <param name="targets">The paths to scan.</param>
    /// <returns>A task that completes when the scan finishes.</returns>
    private async Task RunScanAsync(string[] targets)
    {
        if (targets.Length == 0)
        {
            return;
        }

        var generation = ++_scanGeneration;
        if (_session is { IsRunning: true } previous)
        {
            previous.Cancel();
            await previous.Completion;
        }

        ScanSession session;
        try
        {
            // Planning reads the mount table and identifies each target, which can block on an unreachable network
            // share, so it runs off the UI thread.
            var request = new ScanRequest(targets, CreateOptions(targets));
            StatusText = "Preparing to scan\u2026";
            session = await Task.Run(() => _scanner.Start(request));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusText = $"Cannot scan “{string.Join(", ", targets)}”: {exception.Message}";
            return;
        }

        if (generation != _scanGeneration)
        {
            // Another scan was requested while this one was being prepared; it takes over.
            session.Dispose();
            return;
        }

        _session?.Dispose();
        _session = session;
        _knownRoots = null;
        SelectedRow = null;
        Tree.Clear();
        Chart.Clear();
        Details.Show(null, 0, isScanRunning: true);
        IsScanning = true;
        Refresh();

        using var refreshCancellation = new CancellationTokenSource();
        var refreshLoop = RunRefreshLoopAsync(refreshCancellation.Token);
        try
        {
            await session.Completion;
        }
        finally
        {
            await refreshCancellation.CancelAsync();
            await refreshLoop;
            if (ReferenceEquals(_session, session))
            {
                IsScanning = false;
                Refresh();
                FocusTree();
            }
        }
    }

    /// <summary>
    /// Selects the first row (unless the user already selected one during the scan) and asks the view to focus it.
    /// </summary>
    private void FocusTree()
    {
        if ((SelectedRow ?? (Tree.Rows.Count > 0 ? Tree.Rows[0] : null)) is not { } row)
        {
            return;
        }

        SelectedRow = row;
        FocusRequested?.Invoke(this, row);
    }

    /// <summary>
    /// Refreshes the panes every <see cref="RefreshInterval"/> until cancelled.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // The scan finished; the caller performs the final refresh.
        }
    }

    /// <summary>
    /// Chooses scan options for the targets: more parallelism for network locations, whose latency dominates.
    /// </summary>
    /// <param name="targets">The paths to scan.</param>
    /// <returns>The options.</returns>
    private ScanOptions CreateOptions(string[] targets)
    {
        var isNetwork = targets.Any(target => target.StartsWith(@"\\", StringComparison.Ordinal)
            || Volumes.Any(volume => volume.IsNetwork && PathUtilities.IsSameOrDescendant(target, volume.MountPoint, Platform.PathComparison)));
        return new ScanOptions { MaxDegreeOfParallelism = isNetwork ? 32 : ScanOptions.DefaultParallelism };
    }

    /// <summary>
    /// Gets the paths the Scan button would scan.
    /// </summary>
    /// <returns>The checked volumes when they were chosen in the picker, otherwise the path box contents.</returns>
    private string[] GetTargets()
    {
        if (AllowsMultipleVolumes && _pathFromVolumes)
        {
            string[] mountPoints = [.. Volumes.Where(static item => item.IsChecked).Select(static item => item.MountPoint)];
            if (mountPoints.Length > 0)
            {
                return mountPoints;
            }
        }

        var path = PathText.Trim();
        return path.Length == 0 ? [] : [path];
    }

    /// <summary>
    /// Determines whether the Scan button is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when nothing is being scanned and there is something to scan.</returns>
    private bool CanStartScan() => !IsScanning && GetTargets().Length > 0;

    /// <summary>
    /// Determines whether a volume can be scanned from the volume picker.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <returns><see langword="true"/> when nothing is being scanned.</returns>
    private bool CanScanVolume(VolumeItemViewModel? volume) => !IsScanning && volume is not null;

    /// <summary>
    /// Sets the path box without disturbing the record of how it was set.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="fromVolumes">Whether the text summarises checked volumes.</param>
    private void SetPathText(string text, bool fromVolumes)
    {
        _isSettingPath = true;
        try
        {
            PathText = text;
            _pathFromVolumes = fromVolumes;
        }
        finally
        {
            _isSettingPath = false;
        }
    }

    /// <summary>
    /// Records that the user typed a path, so the Scan button uses it rather than the checked volumes.
    /// </summary>
    /// <param name="value">The new path text.</param>
    partial void OnPathTextChanged(string value)
    {
        if (!_isSettingPath)
        {
            _pathFromVolumes = false;
        }
    }

    /// <summary>
    /// Updates the chart, details and commands when the user changes the selection.
    /// </summary>
    /// <param name="sender">The selection.</param>
    /// <param name="e">The change.</param>
    private void OnSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs<TreeRowViewModel> e)
    {
        if (!_isReconciling)
        {
            OnSelectionUpdated();
        }
    }

    /// <summary>
    /// Brings everything that depends on the selection up to date.
    /// </summary>
    private void OnSelectionUpdated()
    {
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedRows));
        UpdateChart();
        UpdateDetails();
        NotifyItemCommands();
    }

    /// <summary>
    /// Shows the checked volumes in the path box when a volume is checked or unchecked.
    /// </summary>
    /// <param name="sender">The volume item.</param>
    /// <param name="e">The property change.</param>
    private void OnVolumeItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VolumeItemViewModel.IsChecked))
        {
            return;
        }

        var mountPoints = Volumes.Where(static item => item.IsChecked).Select(static item => item.MountPoint).ToArray();
        SetPathText(string.Join("; ", mountPoints), fromVolumes: mountPoints.Length > 0);
        StartScanCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Remembers the selection before the tree changes its rows.
    /// </summary>
    /// <param name="sender">The tree.</param>
    /// <param name="e">Unused.</param>
    private void OnTreeReconciling(object? sender, EventArgs e)
    {
        _isReconciling = true;
        _selectionBeforeReconcile = [.. Selection.SelectedItems.OfType<TreeRowViewModel>()];
    }

    /// <summary>
    /// Restores the selection after the tree changed its rows (moved rows lose their selection in the list). A
    /// selected row that is no longer visible, because an ancestor was collapsed or it was deleted, is replaced by its
    /// nearest visible ancestor.
    /// </summary>
    /// <param name="sender">The tree.</param>
    /// <param name="e">Unused.</param>
    private void OnTreeReconciled(object? sender, EventArgs e)
    {
        var previous = _selectionBeforeReconcile;
        _selectionBeforeReconcile = [];
        try
        {
            if (previous.Count == 0)
            {
                return;
            }

            var indexes = new Dictionary<TreeRowViewModel, int>(Tree.Rows.Count, ReferenceEqualityComparer.Instance);
            for (var index = 0; index < Tree.Rows.Count; index++)
            {
                indexes[Tree.Rows[index]] = index;
            }

            var wanted = new List<int>(previous.Count);
            foreach (var row in previous)
            {
                var visible = row;
                while (visible is not null && !indexes.ContainsKey(visible))
                {
                    visible = visible.Parent;
                }

                if (visible is not null && !wanted.Contains(indexes[visible]))
                {
                    wanted.Add(indexes[visible]);
                }
            }

            if (!wanted.Order().SequenceEqual(Selection.SelectedIndexes.Order()))
            {
                using (Selection.BatchUpdate())
                {
                    Selection.Clear();
                    foreach (var index in wanted)
                    {
                        Selection.Select(index);
                    }
                }
            }

            if (wanted.Count != previous.Count || !wanted.Select(index => Tree.Rows[index]).All(row => previous.Contains(row)))
            {
                _isReconciling = false;
                OnSelectionUpdated();
            }
        }
        finally
        {
            _isReconciling = false;
        }
    }

    /// <summary>
    /// Updates the status bar from the scan's progress counters.
    /// </summary>
    /// <param name="session">The scan.</param>
    private void UpdateStatus(ScanSession session)
    {
        var statistics = session.Statistics;
        var counts = $"{DisplayFormat.Quantity(statistics.DirectoryCount, "folder", "folders")}, " +
            $"{DisplayFormat.Quantity(statistics.FileCount, "file", "files")}, {_sizes.Format(statistics.ByteCount)}";
        var elapsed = DisplayFormat.Duration(session.Elapsed);
        StatusText = session.Status switch
        {
            ScanStatus.Running => $"Scanning… {counts} · {elapsed}",
            ScanStatus.Completed => $"Scanned {counts} in {elapsed}",
            ScanStatus.Cancelled => $"Scan stopped after {elapsed}; results are incomplete · {counts}",
            _ => $"The scan failed: {session.Failure?.Message}",
        };
        ErrorSummary = statistics.ErrorCount > 0
            ? DisplayFormat.Quantity(statistics.ErrorCount, "folder could not be read", "folders could not be read")
            : null;
        ProgressPath = session.IsRunning ? statistics.CurrentPath : null;
    }

    /// <summary>
    /// Waits until the chart reflects the latest request. Used by tests; the UI never waits.
    /// </summary>
    /// <returns>A task that completes when no chart computation is running or pending.</returns>
    internal async Task WaitForChartAsync()
    {
        while (true)
        {
            var current = Volatile.Read(ref _chartUpdate);
            await current;
            lock (_chartGate)
            {
                if (!_chartBusy && !_chartUpdatePending && ReferenceEquals(current, _chartUpdate))
                {
                    return;
                }
            }

            await Task.Yield();
        }
    }

    /// <summary>
    /// Charts the file types in the selected folder (or the folder of the selected item, or every scan root),
    /// highlighting the type of the selected file. The totals are computed off the UI thread; when a computation is
    /// already running, another one follows as soon as it finishes.
    /// </summary>
    private void UpdateChart()
    {
        if (_session is not { } session)
        {
            _chartedScope = default;
            Chart.Clear();
            return;
        }

        var (title, scope, nodes, highlight) = DescribeChartScope(session);
        if (_chartedScope is { IsFinal: true } charted && ReferenceEquals(charted.Scope, scope) && !session.IsRunning)
        {
            // Nothing in a finished scan changes any more; only the highlighted type can differ.
            Chart.Highlight(highlight);
            return;
        }

        lock (_chartGate)
        {
            if (_chartBusy)
            {
                _chartUpdatePending = true;
                return;
            }

            _chartBusy = true;
        }

        _chartUpdate = UpdateChartAsync(title, scope, nodes, highlight, isFinal: !session.IsRunning);
    }

    /// <summary>
    /// Computes the file-type totals on a background thread and shows them.
    /// </summary>
    /// <param name="title">The chart title.</param>
    /// <param name="scope">Identifies what is charted.</param>
    /// <param name="nodes">The nodes whose files are totalled.</param>
    /// <param name="highlight">The type to highlight, if any.</param>
    /// <param name="isFinal">Whether the scan had finished, so the result cannot change.</param>
    /// <returns>A task that completes when the chart has been updated.</returns>
    private async Task UpdateChartAsync(string title, object scope, IReadOnlyList<FileSystemNode> nodes, string? highlight, bool isFinal)
    {
        try
        {
            var items = await Task.Run(() => _fileTypeTotals.Aggregate(nodes)
                .Select(total => new ChartItem(total.Key, FileTypes.Label(total.Key), _fileTypes.Describe(total.Key), total.Value))
                .ToArray());
            Chart.Update(title, scope, items, highlight);
            _chartedScope = (scope, isFinal);
        }
#pragma warning disable CA1031 // The chart is best effort; a failure must not break the scan or the window.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Trace.TraceWarning("Could not chart file types: {0}", exception.Message);
        }

        bool again;
        lock (_chartGate)
        {
            again = _chartUpdatePending;
            _chartUpdatePending = false;
            _chartBusy = false;
        }

        if (again)
        {
            UpdateChart();
        }
    }

    /// <summary>
    /// Works out what the chart shows for the current selection.
    /// </summary>
    /// <param name="session">The scan.</param>
    /// <returns>The title, an object identifying the scope, the nodes to total and the type to highlight.</returns>
    private (string Title, object Scope, IReadOnlyList<FileSystemNode> Nodes, string? Highlight) DescribeChartScope(ScanSession session)
    {
        var selected = SelectedRow?.Node;
        switch (selected)
        {
            case DirectoryNode directory when directory is RootNode || _fileTypes.GetPackageType(directory) is null:
                return ($"File types in {directory.Name}", directory, [directory], null);
            case { Parent: { } parent }:
                var highlight = selected switch
                {
                    FileNode { Kind: NodeKind.File } file => FileTypes.FromName(file.Name),
                    DirectoryNode package => _fileTypes.GetPackageType(package),
                    _ => null,
                };
                return ($"File types in {parent.Name}", parent, [parent], highlight);
            default:
                var roots = session.Roots;
                return roots.Count == 1
                    ? ($"File types in {roots[0].Name}", roots[0], [roots[0]], null)
                    : ("File types in all scanned volumes", session, roots, null);
        }
    }

    /// <summary>
    /// Shows the selected row in the details pane.
    /// </summary>
    private void UpdateDetails()
    {
        var rows = Selection.SelectedItems;
        if (rows.Count > 1)
        {
            Details.ShowSummary([.. rows.OfType<TreeRowViewModel>().Select(static row => row.Node)]);
            return;
        }

        Details.Show(SelectedRow?.Node, SelectedRow?.PercentOfParent ?? 0, _session?.IsRunning ?? false);
    }
}
