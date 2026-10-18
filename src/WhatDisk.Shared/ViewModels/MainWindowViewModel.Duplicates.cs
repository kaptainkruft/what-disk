using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// Finding duplicate files among the scanned files (Tools › Find Duplicates).
/// </summary>
public sealed partial class MainWindowViewModel
{
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

    /// <summary>
    /// Finds the files with identical contents among everything scanned (smaller files by hash, larger ones by
    /// samples, as the settings choose), showing progress in the status bar (Stop cancels), then shows the results in
    /// the Duplicates window, where sampled sets can be confirmed by hash, at once when the settings say so.
    /// </summary>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    [RelayCommand(CanExecute = nameof(CanFindDuplicates))]
    private async Task FindDuplicatesAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        var finder = new DuplicateFinder(_enumerator.GetIdentity, DuplicateOptions());
        var roots = session.Roots;
        DuplicateSearchResult? result = null;
        IsFindingDuplicates = true;
        try
        {
            await RunOperationAsync(SearchAsync);
        }
        finally
        {
            IsFindingDuplicates = false;
        }

        if (result is not null)
        {
            var results = new DuplicateResultsViewModel(result, string.Join(", ", session.Request.Targets), _sizes, _shell, finder, Platform.FileManagerName, _timeProvider.GetLocalNow());
            _dialogs.ShowDuplicates(results);
            if (Settings.Get(SettingsCatalog.Duplicates.VerifyAutomatically) && results.VerifyAllCommand.CanExecute(null))
            {
                await results.VerifyAllCommand.ExecuteAsync(null);
            }
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
                result = await finder.FindAsync(roots, progress, cancellationToken);
            }
            finally
            {
                searching = false;
            }

            return DescribeResult(result);
        }
    }

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
    /// Determines whether Find Duplicates is enabled: after a scan, while nothing else runs.
    /// </summary>
    /// <returns><see langword="true"/> when enabled.</returns>
    private bool CanFindDuplicates() => !IsScanning && !IsOperationRunning && _session is not null && HasResults;
}
