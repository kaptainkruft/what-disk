using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Scanning;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// Finding duplicate files. The Duplicates tab, which is always there, covers every scanned file: while a scan runs
/// and afterwards it lists the files over the small file limit that share a size, and its Sampled Scan (also Tools ›
/// Find Duplicates in Scanned Files) and Full Hash Scan compare their contents. Tools › Find Duplicates in Selection
/// (also in the context menu) compares the selected folders and files as one set, so selecting two folders finds
/// copies between them as well as within each, and shows the result in a tab of its own.
/// </summary>
public sealed partial class MainWindowViewModel : IDuplicateSearchHost
{
    /// <summary>The listing of files that share a size in progress, or the last one.</summary>
    private Task _duplicatesUpdate = Task.CompletedTask;

    /// <summary>Guards <see cref="_duplicatesBusy"/> and <see cref="_duplicatesUpdatePending"/>.</summary>
    private readonly Lock _duplicatesGate = new();

    /// <summary>Whether a listing of files that share a size is running.</summary>
    private bool _duplicatesBusy;

    /// <summary>Whether the files must be listed again when the listing in progress finishes.</summary>
    private bool _duplicatesUpdatePending;

    /// <summary>Whether the Duplicates tab's list is out of date, so it is listed again when the tab is next shown.</summary>
    private bool _duplicatesStale;

    /// <summary>When the files that share a size were last listed, as a <see cref="TimeProvider"/> timestamp; zero for never.</summary>
    private long _duplicatesListedAt;

    /// <summary>Counts changes to what the Duplicates tab shows; a listing started before a change is not shown.</summary>
    private int _duplicatesGeneration;

    /// <summary>Whether the Duplicates tab lists and compares files no longer than the small file limit too.</summary>
    private bool _includeSmallFiles;

    /// <summary>
    /// Gets the least time between listings of the files that share a size while a scan runs, so a large list does
    /// not keep the window busy.
    /// </summary>
    public static TimeSpan DuplicatesRefreshInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the Duplicates tab, which covers the whole scan and cannot be closed.
    /// </summary>
    public DuplicateResultsViewModel DuplicatesTab { get; }

    /// <summary>
    /// Gets a value indicating whether a duplicate search is running. Choosing something else to scan (the volume
    /// picker, the path box, Browse and Scan) is disabled meanwhile, since a new scan would replace the tree being
    /// searched.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanVolumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    public partial bool IsFindingDuplicates { get; private set; }

    /// <inheritdoc/>
    bool IDuplicateSearchHost.CanSearch => CanFindDuplicates();

    /// <inheritdoc/>
    long IDuplicateSearchHost.SmallFileLimit => SmallFileLimit();

    /// <inheritdoc/>
    bool IDuplicateSearchHost.IncludesSmallFiles => _includeSmallFiles;

    /// <inheritdoc/>
    Task IDuplicateSearchHost.SampledScanAsync() => SampledScanAsync();

    /// <inheritdoc/>
    void IDuplicateSearchHost.StopSearch() => _operationCancellation?.Cancel();

    /// <inheritdoc/>
    async Task IDuplicateSearchHost.SetIncludeSmallFilesAsync(bool include)
    {
        if (include == _includeSmallFiles)
        {
            return;
        }

        if (include)
        {
            var limit = DuplicateFinderOptions.DescribeBinarySize(SmallFileLimit());
            var request = new ConfirmationRequest(
                $"Include files of {limit} or less?",
                "Small files share a size far more often than large ones. Across a whole disk this can list hundreds of thousands of matches, "
                + "which makes the Duplicates tab slow to update and its scans slow to run.",
                "Include Them",
                IsDestructive: false);
            if (!await _dialogs.ConfirmAsync(request))
            {
                return;
            }
        }

        _includeSmallFiles = include;
        ResetDuplicates();
        UpdateDuplicates();
    }

    /// <summary>
    /// Waits until the Duplicates tab reflects the latest listing of files that share a size. Used by tests; the UI
    /// never waits.
    /// </summary>
    /// <returns>A task that completes when no listing is running or pending.</returns>
    internal async Task WaitForDuplicatesAsync()
    {
        while (true)
        {
            var current = Volatile.Read(ref _duplicatesUpdate);
            await current;
            lock (_duplicatesGate)
            {
                if (!_duplicatesBusy && !_duplicatesUpdatePending && ReferenceEquals(current, _duplicatesUpdate))
                {
                    return;
                }
            }

            await Task.Yield();
        }
    }

    /// <summary>
    /// Shows the Duplicates tab and runs its sampled scan of every scanned file.
    /// </summary>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    [RelayCommand(CanExecute = nameof(CanFindDuplicates))]
    private Task FindDuplicatesInScanAsync()
    {
        SelectedTab = DuplicatesTab;
        return SampledScanAsync();
    }

    /// <summary>
    /// Finds the duplicates among the selected folders and files, compared as one set, so that two suspect folders
    /// can be compared. Items beneath another selected item are counted once.
    /// </summary>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    [RelayCommand(CanExecute = nameof(CanFindDuplicatesInSelection))]
    private Task FindDuplicatesInSelectionAsync()
    {
        var scope = Topmost(SelectedRows.Select(static row => row.Node));
        var (name, shortName) = scope switch
        {
            [var only] => (only.FullPath, only.Name),
            [var first, var second] => ($"{first.FullPath} and {second.FullPath}", $"{first.Name} and {second.Name}"),
            _ => (DisplayFormat.Quantity(scope.Length, "selected item", "selected items"), DisplayFormat.Quantity(scope.Length, "selected item", "selected items")),
        };
        return FindDuplicatesAsync(scope, name, shortName);
    }

    /// <summary>
    /// Compares the files of the whole scan that share a size and shows the result in the Duplicates tab.
    /// </summary>
    /// <returns>A task that completes when the search has finished or was stopped.</returns>
    private Task SampledScanAsync() =>
        _session is { } session ? FindDuplicatesAsync([.. session.Roots], DescribeScanScope(session), null, DuplicatesTab) : Task.CompletedTask;

    /// <summary>
    /// Finds the files with identical contents within a scope (smaller files by hash, larger ones by samples, as the
    /// settings choose), showing progress in the status bar (Stop cancels), then shows the results in the Duplicates
    /// tab or a new tab, where sets matched by samples can be confirmed by hash, at once when the settings say so.
    /// </summary>
    /// <param name="scope">The folders and files whose files are compared, as one set.</param>
    /// <param name="scopeName">What the scope is, in full, such as a folder's path.</param>
    /// <param name="shortName">What the scope is, briefly, for a new tab's title, such as a folder's name.</param>
    /// <param name="target">
    /// The Duplicates tab, which compares the files over the small file limit unless smaller files are included; or
    /// <see langword="null"/> to compare every non-empty file and show the result in a new tab.
    /// </param>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    private async Task FindDuplicatesAsync(FileSystemNode[] scope, string scopeName, string? shortName, DuplicateResultsViewModel? target = null)
    {
        if (_session is null || scope.Length == 0)
        {
            return;
        }

        if (target is { IsVerifying: true })
        {
            target.StopCommand.Execute(null);
        }

        var finder = new DuplicateFinder(_enumerator.GetIdentity, DuplicateOptions() with { MinimumLength = target is null ? 0 : DuplicateMinimumLength() });
        DuplicateSearchResult? result = null;
        IsFindingDuplicates = true;
        if (target is not null)
        {
            _duplicatesGeneration++;
            target.IsSearching = true;
        }

        try
        {
            await RunOperationAsync(SearchAsync);
        }
        finally
        {
            IsFindingDuplicates = false;
            target?.IsSearching = false;
        }

        if (result is null)
        {
            // Stopped: the Duplicates tab still lists the files that share a size.
            UpdateDuplicates();
            return;
        }

        var results = target;
        if (results is null)
        {
            results = new DuplicateResultsViewModel(result, scopeName, _sizes, _shell, finder, Platform.FileManagerName, _timeProvider.GetLocalNow(), shortName);
            OpenTab(results);
        }
        else
        {
            _duplicatesGeneration++;
            results.ShowResult(result, finder, _timeProvider.GetLocalNow(), scopeName);
        }

        if (Settings.Get(SettingsCatalog.Duplicates.VerifyAutomatically) && results.FullHashScanCommand.CanExecute(null))
        {
            await results.FullHashScanCommand.ExecuteAsync(null);
        }

        async Task<string> SearchAsync(CancellationToken cancellationToken)
        {
            // Progress reports are posted to the UI thread and can arrive after the search ends; they must not
            // replace the summary.
            var searching = true;
            var progress = new Progress<DuplicateProgress>(report =>
            {
                if (searching)
                {
                    StatusText = DescribeProgress(report, finder.Options);
                }
            });
            try
            {
                result = await finder.FindAsync(scope, progress, cancellationToken);
            }
            finally
            {
                searching = false;
            }

            return DescribeResult(result);
        }
    }

    /// <summary>
    /// Empties the Duplicates tab and makes it list the files that share a size again, for a new scan or a new small
    /// file limit; a listing in progress is not shown.
    /// </summary>
    private void ResetDuplicates()
    {
        _duplicatesGeneration++;
        _duplicatesListedAt = 0;
        _duplicatesStale = true;
        DuplicatesTab.Clear(_session is { } session ? DescribeScanScope(session) : null);
        DuplicatesTab.NotifySearchStateChanged();
    }

    /// <summary>
    /// Lists the files of the scan that share a size in the Duplicates tab, while it follows the scan. The listing runs
    /// off the UI thread, only while the tab is shown (otherwise when it is next shown), and at most every
    /// <see cref="DuplicatesRefreshInterval"/> while a scan runs; when a listing is already running, another follows
    /// as soon as it finishes.
    /// </summary>
    private void UpdateDuplicates()
    {
        if (_session is not { } session || !DuplicatesTab.IsLive || DuplicatesTab.IsBusy)
        {
            return;
        }

        if (!DuplicatesTab.IsSelected
            || (session.IsRunning && _duplicatesListedAt != 0 && _timeProvider.GetElapsedTime(_duplicatesListedAt) < DuplicatesRefreshInterval))
        {
            _duplicatesStale = true;
            return;
        }

        lock (_duplicatesGate)
        {
            if (_duplicatesBusy)
            {
                _duplicatesUpdatePending = true;
                return;
            }

            _duplicatesBusy = true;
        }

        _duplicatesStale = false;
        _duplicatesListedAt = _timeProvider.GetTimestamp();
        _duplicatesUpdate = ListDuplicatesAsync(session, _duplicatesGeneration);
    }

    /// <summary>
    /// Groups the scan's files by size on a background thread and shows the sets in the Duplicates tab, unless what the
    /// tab shows changed meanwhile.
    /// </summary>
    /// <param name="session">The scan.</param>
    /// <param name="generation">The value of <see cref="_duplicatesGeneration"/> when the listing started.</param>
    /// <returns>A task that completes when the tab has been updated.</returns>
    private async Task ListDuplicatesAsync(ScanSession session, int generation)
    {
        try
        {
            var finder = new DuplicateFinder(_enumerator.GetIdentity, DuplicateOptions() with { MinimumLength = DuplicateMinimumLength() });
            var roots = session.Roots;
            var result = await Task.Run(() => finder.GroupBySize(roots, CancellationToken.None));
            if (generation == _duplicatesGeneration && ReferenceEquals(session, _session))
            {
                DuplicatesTab.ShowSizeMatches(result, DescribeScanScope(session), finder, _timeProvider.GetLocalNow());
            }
        }
#pragma warning disable CA1031 // The list is best effort; a failure must not break the scan or the window.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Trace.TraceWarning("Could not list files that share a size: {0}", exception.Message);
        }

        bool again;
        lock (_duplicatesGate)
        {
            again = _duplicatesUpdatePending;
            _duplicatesUpdatePending = false;
            _duplicatesBusy = false;
        }

        if (again)
        {
            UpdateDuplicates();
        }
    }

    /// <summary>
    /// Describes what a scan covers, in full, such as the scanned folder.
    /// </summary>
    /// <param name="session">The scan.</param>
    /// <returns>The scanned paths.</returns>
    private static string DescribeScanScope(ScanSession session) => string.Join(", ", session.Request.Targets);

    /// <summary>
    /// Gets the length files must exceed to be listed in the Duplicates tab unless smaller files are included.
    /// </summary>
    /// <returns>The length in bytes, from the settings.</returns>
    private long SmallFileLimit() => Settings.Get(SettingsCatalog.Duplicates.EstimateOverMiB) * 1024L * 1024;

    /// <summary>
    /// Gets the length files must exceed to be listed and compared in the Duplicates tab.
    /// </summary>
    /// <returns>Zero when smaller files are included; otherwise <see cref="SmallFileLimit"/>.</returns>
    private long DuplicateMinimumLength() => _includeSmallFiles ? 0 : SmallFileLimit();

    /// <summary>
    /// Describes a duplicate search's progress for the status bar.
    /// </summary>
    /// <param name="report">The progress.</param>
    /// <param name="options">How files are compared.</param>
    /// <returns>The status text.</returns>
    private string DescribeProgress(DuplicateProgress report, DuplicateFinderOptions options) => report.Stage switch
    {
        DuplicateSearchStage.Grouping => $"Finding duplicates: grouping {DisplayFormat.Quantity(report.FilesTotal, "file", "files")} by size…",
        DuplicateSearchStage.Sampling => $"Finding duplicates: comparing samples of {DisplayFormat.Count(report.FilesDone)} of {DisplayFormat.Quantity(report.FilesTotal, "file", "files")} over {options.HashLimitText}…",
        _ => $"Finding duplicates: hashing {DisplayFormat.Count(report.FilesDone)} of {DisplayFormat.Quantity(report.FilesTotal, "file", "files")} up to {options.HashLimitText} ({_sizes.Format(report.BytesDone)} of {_sizes.Format(report.BytesTotal)})…",
    };

    /// <summary>
    /// Describes a duplicate search's result for the status bar.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The status text.</returns>
    private string DescribeResult(DuplicateSearchResult result)
    {
        var found = result.Sets.Count == 0
            ? $"No duplicates among {DisplayFormat.Quantity(result.FilesConsidered, "file", "files")}"
            : $"Found {DisplayFormat.Quantity(result.Sets.Count, "set", "sets")} of duplicates ({DisplayFormat.Quantity(result.DuplicateFiles, "file", "files")}, {_sizes.Format(result.WastedBytes)} could be freed)"
              + (result.SampledSets > 0 ? $"; {DisplayFormat.Quantity(result.SampledSets, "set", "sets")} only matched on samples" : string.Empty);
        return result.Failures.Count == 0 ? found : $"{found}; {DisplayFormat.Quantity(result.Failures.Count, "file", "files")} could not be read";
    }

    /// <summary>
    /// Determines whether Find Duplicates in Scanned Files (and the Duplicates tab's scans) is enabled: after a scan,
    /// while nothing else runs.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanFindDuplicates() => !IsScanning && !IsOperationRunning && !IsDialogOpen && _session is not null && HasResults;

    /// <summary>
    /// Determines whether Find Duplicates in Selection is enabled: as for the scanned files, while the tree is active
    /// and the selection could hold duplicates (a folder, or more than one item).
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanFindDuplicatesInSelection() =>
        CanFindDuplicates() && HasActionableSelection() && (Selection.Count > 1 || Selection.SelectedItems.Any(static row => row?.Node is DirectoryNode));
}
