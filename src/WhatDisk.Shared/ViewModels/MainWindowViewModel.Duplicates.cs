using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Scanning;
using WhatDisk.Settings;

namespace WhatDisk.ViewModels;

/// <summary>
/// Finding duplicate files within a scope: every scanned file (Tools › Find Duplicates in Scanned Files), or the
/// selected folders and files (Tools › Find Duplicates in Selection, also in the context menu). The files of a scope
/// are compared as one set, so selecting two folders finds copies between them as well as within each.
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
    /// Finds the duplicates among every scanned file: the scope is the scan's top-level items.
    /// </summary>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    [RelayCommand(CanExecute = nameof(CanFindDuplicates))]
    private Task FindDuplicatesInScanAsync() =>
        _session is { } session
            ? FindDuplicatesAsync([.. session.Roots], string.Join(", ", session.Request.Targets), string.Join(", ", session.Roots.Select(static root => root.Name)))
            : Task.CompletedTask;

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
    /// Finds the files with identical contents within a scope (smaller files by hash, larger ones by samples, as the
    /// settings choose), showing progress in the status bar (Stop cancels), then shows the results in a new tab, where
    /// sampled sets can be confirmed by hash, at once when the settings say so.
    /// </summary>
    /// <param name="scope">The folders and files whose files are compared, as one set.</param>
    /// <param name="scopeName">What the scope is, in full, such as a folder's path.</param>
    /// <param name="shortName">What the scope is, briefly, for the tab's title, such as a folder's name.</param>
    /// <returns>A task that completes when the search has finished and the results are shown.</returns>
    private async Task FindDuplicatesAsync(FileSystemNode[] scope, string scopeName, string shortName)
    {
        if (_session is null || scope.Length == 0)
        {
            return;
        }

        var finder = new DuplicateFinder(_enumerator.GetIdentity, DuplicateOptions());
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
            var results = new DuplicateResultsViewModel(result, scopeName, _sizes, _shell, finder, Platform.FileManagerName, _timeProvider.GetLocalNow(), shortName);
            OpenTab(results);
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
    /// Determines whether Find Duplicates in Scanned Files is enabled: after a scan, while nothing else runs.
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
