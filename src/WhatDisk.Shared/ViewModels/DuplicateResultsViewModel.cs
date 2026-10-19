using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Platform;

namespace WhatDisk.ViewModels;

/// <summary>
/// Duplicate files, shown in a tab: one row per file, grouped by set, with a summary, the same CSV for copying to the
/// clipboard and exporting to a file, a way to show a file in the file manager, and commands that confirm sets by
/// hashing their files.
/// </summary>
/// <remarks>
/// The Duplicates tab, which is always there, covers the whole scan: before a scan it is empty; while the scan runs
/// and afterwards it lists the files that share a size, following the tree; a sampled scan or a full hash scan then
/// compares their contents, and the tab keeps that result until the next scan. A search of the selected items shows
/// its result in a tab of its own, which can be closed.
/// </remarks>
public sealed partial class DuplicateResultsViewModel : TabViewModel
{
    /// <summary>The most failures listed in the tooltip.</summary>
    private const int ListedFailures = 20;

    /// <summary>The result before anything has been found.</summary>
    private static readonly DuplicateSearchResult NoResult = new([], 0, 0, 0, 0, 0, 0, 0, []);

    /// <summary>The tab's title, or <see langword="null"/> for the Duplicates tab.</summary>
    private readonly string? _header;

    /// <summary>Runs sampled scans of the whole scan, or <see langword="null"/> for a search of the selection.</summary>
    private readonly IDuplicateSearchHost? _host;

    /// <summary>Copies the CSV to the clipboard and shows files in the file manager.</summary>
    private readonly IShellService _shell;

    /// <summary>Formats sizes in the platform's units.</summary>
    private readonly ByteSizeFormatter _sizes;

    /// <summary>The file manager's name, such as "Finder" or "File Explorer".</summary>
    private readonly string _fileManagerName;

    /// <summary>The sets shown, in display order.</summary>
    private readonly List<DuplicateSet> _sets = [];

    /// <summary>The files that could not be read, during the search or since.</summary>
    private readonly List<DuplicateFailure> _failures = [];

    /// <summary>Hashes the files of sets that are not confirmed yet.</summary>
    private DuplicateFinder _finder = new();

    /// <summary>The number the next set split off by hashing gets.</summary>
    private int _nextSetId = 1;

    /// <summary>Stops the running verification.</summary>
    private CancellationTokenSource? _verification;

    /// <summary>The files of size-only sets hashed whole by sampled scans since the files were listed.</summary>
    private int _hashedFiles;

    /// <summary>The files of size-only sets compared by samples by sampled scans since the files were listed.</summary>
    private int _sampledFiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateResultsViewModel"/> class for the result of a search of
    /// the selection, in a tab of its own.
    /// </summary>
    /// <param name="result">The search's result.</param>
    /// <param name="scope">What was searched, for the window title, such as the scanned folder.</param>
    /// <param name="sizes">Formats sizes in the platform's units.</param>
    /// <param name="shell">Copies text to the clipboard and shows files in the file manager.</param>
    /// <param name="finder">Hashes the files of sampled sets.</param>
    /// <param name="fileManagerName">The file manager's name, such as "Finder" or "File Explorer".</param>
    /// <param name="foundAt">When the search finished, in local time, for the suggested file name.</param>
    /// <param name="shortScope">What was searched, briefly, for the tab's title, such as a folder's name; <see langword="null"/> uses <paramref name="scope"/>.</param>
    public DuplicateResultsViewModel(
        DuplicateSearchResult result,
        string scope,
        ByteSizeFormatter sizes,
        IShellService shell,
        DuplicateFinder finder,
        string fileManagerName,
        DateTimeOffset foundAt,
        string? shortScope = null)
        : this(sizes, shell, fileManagerName, host: null, header: $"Duplicates in {shortScope ?? scope}")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(finder);
        ShowResult(result, finder, foundAt, scope);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateResultsViewModel"/> class for the Duplicates tab, which
    /// covers the whole scan and cannot be closed. It is empty until <see cref="ShowSizeMatches"/> or
    /// <see cref="ShowResult"/> fills it.
    /// </summary>
    /// <param name="sizes">Formats sizes in the platform's units.</param>
    /// <param name="shell">Copies text to the clipboard and shows files in the file manager.</param>
    /// <param name="fileManagerName">The file manager's name, such as "Finder" or "File Explorer".</param>
    /// <param name="host">Runs sampled scans of the whole scan.</param>
    public DuplicateResultsViewModel(ByteSizeFormatter sizes, IShellService shell, string fileManagerName, IDuplicateSearchHost host)
        : this(sizes, shell, fileManagerName, host ?? throw new ArgumentNullException(nameof(host)), header: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateResultsViewModel"/> class, empty.
    /// </summary>
    /// <param name="sizes">Formats sizes in the platform's units.</param>
    /// <param name="shell">Copies text to the clipboard and shows files in the file manager.</param>
    /// <param name="fileManagerName">The file manager's name, such as "Finder" or "File Explorer".</param>
    /// <param name="host">Runs sampled scans of the whole scan, or <see langword="null"/>.</param>
    /// <param name="header">The tab's title, or <see langword="null"/> for the Duplicates tab.</param>
    private DuplicateResultsViewModel(ByteSizeFormatter sizes, IShellService shell, string fileManagerName, IDuplicateSearchHost? host, string? header)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentException.ThrowIfNullOrEmpty(fileManagerName);
        _sizes = sizes;
        _shell = shell;
        _fileManagerName = fileManagerName;
        _host = host;
        _header = header;
        IsLive = host is not null;
        UpdateSummary();
    }

    /// <summary>
    /// Gets the result shown, as found.
    /// </summary>
    public DuplicateSearchResult Result { get; private set; } = NoResult;

    /// <summary>
    /// Gets the sets shown, as confirmed so far.
    /// </summary>
    public IReadOnlyList<DuplicateSet> Sets => _sets;

    /// <summary>
    /// Gets what is covered, in full, as the tab's tooltip, such as "Duplicates in /Users/me"; <see langword="null"/>
    /// before the first scan.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string? Title { get; private set; }

    /// <inheritdoc/>
    public override string Header => _header ?? "Duplicates";

    /// <inheritdoc/>
    public override string? ToolTip => Title;

    /// <summary>
    /// Gets a value indicating whether the tab can be closed: a search of the selection can, the Duplicates tab
    /// cannot.
    /// </summary>
    public override bool CanClose => _host is null;

    /// <summary>
    /// Gets a value indicating whether this is the Duplicates tab, which covers the whole scan and offers a sampled
    /// scan and the choice of including smaller files.
    /// </summary>
    public bool CoversWholeScan => _host is not null;

    /// <summary>
    /// Gets a value indicating whether the tab lists the files that share a size and follows the scan, rather than
    /// showing the result of comparing them.
    /// </summary>
    public bool IsLive { get; private set; }

    /// <summary>
    /// Gets one row per file, in set order.
    /// </summary>
    public ObservableRangeList<DuplicateRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets a value indicating whether any files are listed.
    /// </summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// Gets a value indicating whether any set is not confirmed by hash: it only matched on samples, or only on size.
    /// </summary>
    public bool HasUnconfirmedSets => _sets.Exists(static set => set.Comparison != DuplicateComparison.Hash);

    /// <summary>
    /// Gets a value indicating whether any set only shares a size, so a sampled scan has something to compare.
    /// </summary>
    public bool HasSizeOnlySets => _sets.Exists(static set => set.Comparison == DuplicateComparison.Size);

    /// <summary>
    /// Gets how many files each stage of the search compared, and which were left out.
    /// </summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the list shows when it is empty.
    /// </summary>
    [ObservableProperty]
    public partial string EmptyHint { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the results as CSV, as copied and exported.
    /// </summary>
    public string Csv => DuplicateCsv.Format(_sets);

    /// <summary>
    /// Gets the file name suggested when exporting.
    /// </summary>
    public string SuggestedFileName { get; private set; } = "WhatDisk Duplicates.csv";

    /// <summary>
    /// Gets a value indicating whether the platform can show files in its file manager.
    /// </summary>
    public bool CanReveal => (_shell.Capabilities & ShellCapabilities.Reveal) != 0;

    /// <summary>
    /// Gets a value indicating whether there is anything to copy, the platform can copy text to the clipboard, and no
    /// scan or verification is changing the sets.
    /// </summary>
    public bool CanCopy => HasRows && !IsBusy && (_shell.Capabilities & ShellCapabilities.CopyText) != 0;

    /// <summary>
    /// Gets a value indicating whether files no longer than the small file limit are listed too.
    /// </summary>
    public bool IncludesSmallFiles => _host?.IncludesSmallFiles ?? false;

    /// <summary>
    /// Gets the text of the button that includes or leaves out smaller files, or <see langword="null"/> when there is
    /// no such choice (a search of the selection, or no limit).
    /// </summary>
    public string? SmallFilesText => _host is { SmallFileLimit: > 0 and var limit }
        ? IncludesSmallFiles
            ? $"Leave Out Files of {DuplicateFinderOptions.DescribeBinarySize(limit)} or Less"
            : $"Include Files of {DuplicateFinderOptions.DescribeBinarySize(limit)} or Less…"
        : null;

    /// <summary>
    /// Gets what was found: the number of sets and files, and the space that could be freed.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Gets how the sets were compared: how many were confirmed by hash, and how many only matched on samples or on
    /// size.
    /// </summary>
    [ObservableProperty]
    public partial string? VerificationSummary { get; private set; }

    /// <summary>
    /// Gets how many files could not be read, or <see langword="null"/> when every file was read.
    /// </summary>
    [ObservableProperty]
    public partial string? FailureSummary { get; private set; }

    /// <summary>
    /// Gets the files that could not be read and why (the first few), or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? FailureDetails { get; private set; }

    /// <summary>
    /// Gets a hint, or the outcome of the last copy, export, reveal or verification, or its progress.
    /// </summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>
    /// Gets a value indicating whether files are being hashed to confirm sets.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    public partial bool IsVerifying { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether a sampled scan of the whole scan is running, which the main window
    /// reports in the status bar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    public partial bool IsSearching { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether a sampled scan or a verification is running; Stop stops it.
    /// </summary>
    public bool IsBusy => IsVerifying || IsSearching;

    /// <summary>
    /// Writes the CSV to a stream as UTF-8 with a byte order mark, so that spreadsheet programs read non-ASCII paths
    /// correctly.
    /// </summary>
    /// <param name="stream">The stream; it is not closed.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task that completes when the CSV has been written.</returns>
    public async Task WriteCsvAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await stream.WriteAsync(Encoding.UTF8.Preamble.ToArray(), cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(Csv), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Exports the CSV to a file the user chose, reporting the outcome in <see cref="Message"/>.
    /// </summary>
    /// <param name="name">The file's name, for the message.</param>
    /// <param name="open">Opens the file for writing.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    public async Task ExportAsync(string name, Func<Task<Stream>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        try
        {
            var stream = await open();
            await using (stream.ConfigureAwait(true))
            {
                if (stream.CanSeek)
                {
                    // Replacing a longer file must not leave its tail behind.
                    stream.SetLength(0);
                }

                await WriteCsvAsync(stream);
            }

            Message = $"Exported {DisplayFormat.Quantity(Rows.Count, "row", "rows")} to “{name}”.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Message = $"Could not export to “{name}”: {exception.Message}";
        }
    }

    /// <summary>
    /// Empties the tab and makes it follow the next scan, stopping any verification. Sets already hashed are
    /// forgotten.
    /// </summary>
    /// <param name="scope">What the next lists will cover, such as the scanned folder, or <see langword="null"/> before any scan.</param>
    public void Clear(string? scope)
    {
        _verification?.Cancel();
        _sets.Clear();
        _failures.Clear();
        Rows.Clear();
        Result = NoResult;
        Title = scope is null ? null : $"Duplicates in {scope}";
        Details = string.Empty;
        FailureSummary = null;
        FailureDetails = null;
        Message = null;
        _hashedFiles = 0;
        _sampledFiles = 0;
        IsLive = _host is not null;
        UpdateSummary();
    }

    /// <summary>
    /// Lists the files that share a size, as grouping the scan found them, keeping the rows of files already listed so
    /// that the selection and scroll position stay while the scan runs. Ignored once the tab shows the result of
    /// comparing the files.
    /// </summary>
    /// <param name="result">The sets of files that share a size, from <see cref="DuplicateFinder.GroupBySize"/>.</param>
    /// <param name="scope">What the sets cover, such as the scanned folder.</param>
    /// <param name="finder">Hashes the files of a set when asked, with the options that grouped them.</param>
    /// <param name="listedAt">When the files were grouped, in local time, for the suggested file name.</param>
    public void ShowSizeMatches(DuplicateSearchResult result, string scope, DuplicateFinder finder, DateTimeOffset listedAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(finder);
        if (!IsLive)
        {
            return;
        }

        _finder = finder;
        Result = result;
        _sets.Clear();
        _sets.AddRange(result.Sets);
        _failures.Clear();
        _hashedFiles = 0;
        _sampledFiles = 0;
        _nextSetId = NextSetId();
        ReconcileRows();
        Title = $"Duplicates in {scope}";
        Details = DescribeSizeMatches(result, finder.Options);
        SuggestedFileName = FileNameFor(listedAt);
        UpdateShading();
        UpdateSummary();
        ShowRevealHint();
    }

    /// <summary>
    /// Shows the files that share a size as a sampled scan of them starts; the tab stops following the scan, and
    /// <see cref="ShowOutcome"/> then replaces each length's set as soon as its files have been compared.
    /// </summary>
    /// <param name="sizeMatches">The sets of files that share a size, from <see cref="DuplicateFinder.GroupBySize"/>.</param>
    /// <param name="scope">What the sets cover, such as the scanned folder.</param>
    /// <param name="finder">The finder that compares them.</param>
    /// <param name="listedAt">When the files were grouped, in local time, for the suggested file name.</param>
    public void ShowComparing(DuplicateSearchResult sizeMatches, string scope, DuplicateFinder finder, DateTimeOffset listedAt)
    {
        IsLive = true;
        ShowSizeMatches(sizeMatches, scope, finder, listedAt);
        IsLive = false;
    }

    /// <summary>
    /// Gets the files of the sets that only share a size, which a sampled scan compares; sets already compared by
    /// samples or hash are left out, so a sampled scan that was stopped carries on where it stopped.
    /// </summary>
    /// <returns>The files, with their lengths.</returns>
    public IReadOnlyList<DuplicateCandidate> GetSizeOnlyFiles() =>
        [.. _sets.Where(static set => set.Comparison == DuplicateComparison.Size).SelectMany(static set => set.Paths.Select(path => new DuplicateCandidate(path, set.Length)))];

    /// <summary>
    /// Replaces the set of files of one length with what comparing them found, as a sampled scan goes: the rows of
    /// matching files stay, and the others leave.
    /// </summary>
    /// <param name="outcome">What comparing the files of one length found.</param>
    public void ShowOutcome(DuplicateGroupOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (_sets.Find(set => set.Length == outcome.Length && set.Comparison == DuplicateComparison.Size) is { } set)
        {
            Compare(set, outcome.Matches, outcome.Comparison);
        }
    }

    /// <summary>
    /// Finishes a sampled scan of the sets that only shared a size: any whose outcome was not shown yet are replaced
    /// from the result, failures are added, and the sets are numbered again by the space they waste, keeping the rows
    /// already shown. The details count every file compared since the files were listed, including by a scan that was
    /// stopped.
    /// </summary>
    /// <param name="result">The result of comparing the files of the size-only sets.</param>
    /// <param name="finder">The finder that compared them, which hashes sampled sets with the same options.</param>
    /// <param name="foundAt">When the scan finished, in local time, for the suggested file name.</param>
    public void FinishComparison(DuplicateSearchResult result, DuplicateFinder finder, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(finder);
        _finder = finder;
        foreach (var set in _sets.Where(static set => set.Comparison == DuplicateComparison.Size).ToList())
        {
            var found = result.Sets.Where(found => found.Length == set.Length).ToList();
            Compare(set, [.. found.Select(static found => new DuplicateMatch(found.Hash, found.Paths))], found.FirstOrDefault()?.Comparison ?? DuplicateComparison.Hash);
        }

        DuplicateSet[] numbered =
        [
            .. _sets
                .OrderByDescending(static set => set.WastedBytes)
                .ThenByDescending(static set => set.Length)
                .ThenBy(static set => set.Paths[0], StringComparer.Ordinal)
                .Select(static (set, index) => set with { Id = index + 1 }),
        ];
        _sets.Clear();
        _sets.AddRange(numbered);
        _failures.AddRange(result.Failures);
        _nextSetId = NextSetId();
        ReconcileRows();
        Result = Result with { Sets = numbered, HashedFiles = _hashedFiles, SampledFiles = _sampledFiles, Failures = [.. _failures] };
        Details = DescribeSearch(Result, finder.Options);
        SuggestedFileName = FileNameFor(foundAt);
        UpdateShading();
        UpdateSummary();
    }

    /// <summary>
    /// Shows the result of comparing the files' contents, which stays until the tab is cleared. Rows of files already
    /// listed the same way are kept, so the selection and scroll position stay.
    /// </summary>
    /// <param name="result">The search's result.</param>
    /// <param name="finder">The finder that compared the files, which hashes sampled sets with the same options.</param>
    /// <param name="foundAt">When the search finished, in local time, for the suggested file name.</param>
    /// <param name="scope">What was searched, such as the scanned folder; <see langword="null"/> keeps the title.</param>
    public void ShowResult(DuplicateSearchResult result, DuplicateFinder finder, DateTimeOffset foundAt, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(finder);
        _verification?.Cancel();
        IsLive = false;
        _finder = finder;
        Result = result;
        _sets.Clear();
        _sets.AddRange(result.Sets);
        _failures.Clear();
        _failures.AddRange(result.Failures);
        _nextSetId = NextSetId();
        ReconcileRows();
        if (scope is not null)
        {
            Title = $"Duplicates in {scope}";
        }

        Details = DescribeSearch(result, finder.Options);
        SuggestedFileName = FileNameFor(foundAt);
        FailureSummary = null;
        FailureDetails = null;
        Message = null;
        UpdateShading();
        UpdateSummary();
        ShowRevealHint();
    }

    /// <summary>
    /// Re-evaluates the commands that depend on the main window: whether a scan has finished and nothing else runs,
    /// and whether smaller files are included.
    /// </summary>
    public void NotifySearchStateChanged()
    {
        OnPropertyChanged(nameof(IncludesSmallFiles));
        OnPropertyChanged(nameof(SmallFilesText));
        NotifyCommands();
    }

    /// <summary>
    /// Stops any hashing when the tab is closed; sets already hashed keep their results.
    /// </summary>
    public override void OnClosed() => _verification?.Cancel();

    /// <summary>
    /// Describes how many files each stage compared, and what was left out.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="options">How the files were compared.</param>
    /// <returns>The description.</returns>
    private static string DescribeSearch(DuplicateSearchResult result, DuplicateFinderOptions options) =>
        WithLeftOut(
            $"Of {DisplayFormat.Quantity(result.FilesConsidered, "file", "files")}{Over(options)}, {DisplayFormat.Count(result.SizeMatches)} shared a size with another: "
            + $"{DisplayFormat.Count(result.HashedFiles)} up to {options.HashLimitText} were hashed, and {DisplayFormat.Count(result.SampledFiles)} larger ones were compared by {options.SamplesText}.",
            result,
            options);

    /// <summary>
    /// Describes how many files share a size, and what was left out.
    /// </summary>
    /// <param name="result">The sets of files that share a size.</param>
    /// <param name="options">The options that grouped them.</param>
    /// <returns>The description.</returns>
    private static string DescribeSizeMatches(DuplicateSearchResult result, DuplicateFinderOptions options) =>
        WithLeftOut(
            $"Of {DisplayFormat.Quantity(result.FilesConsidered, "file", "files")}{Over(options)}, {DisplayFormat.Count(result.SizeMatches)} share a size with another; their contents have not been compared.",
            result,
            options);

    /// <summary>
    /// Names the minimum length files had to exceed, for a description.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>Such as " over 1 MiB", or nothing when every non-empty file was considered.</returns>
    private static string Over(DuplicateFinderOptions options) => options.MinimumLength > 0 ? $" over {options.MinimumLengthText}" : string.Empty;

    /// <summary>
    /// Appends what was left out to a description.
    /// </summary>
    /// <param name="text">The description.</param>
    /// <param name="result">The result.</param>
    /// <param name="options">The options.</param>
    /// <returns>The description, with what was left out.</returns>
    private static string WithLeftOut(string text, DuplicateSearchResult result, DuplicateFinderOptions options)
    {
        var leftOut = new List<string>();
        if (result.SmallFiles > 0)
        {
            leftOut.Add(DisplayFormat.Quantity(result.SmallFiles, $"file of {options.MinimumLengthText} or less", $"files of {options.MinimumLengthText} or less"));
        }

        if (result.EmptyFiles > 0)
        {
            leftOut.Add(DisplayFormat.Quantity(result.EmptyFiles, "empty file", "empty files"));
        }

        if (result.OfflineFiles > 0)
        {
            leftOut.Add(DisplayFormat.Quantity(result.OfflineFiles, "cloud-only file", "cloud-only files"));
        }

        if (result.HardLinks > 0)
        {
            leftOut.Add(DisplayFormat.Quantity(result.HardLinks, "extra hard link", "extra hard links"));
        }

        return leftOut.Count == 0 ? text : $"{text} Left out: {string.Join(", ", leftOut)}.";
    }

    /// <summary>
    /// Describes the outcome of a verification.
    /// </summary>
    /// <param name="sets">The sets that were hashed.</param>
    /// <param name="confirmed">The number of sets confirmed whole.</param>
    /// <param name="outcomes">What changed in the other sets, by set number.</param>
    /// <returns>The description.</returns>
    private static string DescribeVerification(IReadOnlyList<DuplicateSet> sets, int confirmed, List<(int SetId, string Text)> outcomes)
    {
        if (sets.Count == 1)
        {
            return outcomes is [var only] ? $"Hashed set {only.SetId}: it {only.Text}." : $"Hashed set {sets[0].Id}: every file is identical.";
        }

        var text = $"Hashed {DisplayFormat.Quantity(sets.Count, "set", "sets")}: {DisplayFormat.Count(confirmed)} confirmed";
        if (outcomes.Count == 0)
        {
            return $"{text}.";
        }

        // A full hash scan of files that only shared a size can remove thousands of sets; name only the first few.
        const int Named = 10;
        var named = string.Join("; ", outcomes.Take(Named).Select(static outcome => $"set {outcome.SetId} {outcome.Text}"));
        return outcomes.Count > Named ? $"{text}; {named}; and {DisplayFormat.Count(outcomes.Count - Named)} more changed." : $"{text}; {named}.";
    }

    /// <summary>
    /// Suggests a file name for exporting.
    /// </summary>
    /// <param name="at">When the files were listed, in local time.</param>
    /// <returns>Such as "WhatDisk Duplicates 2026-10-19 1430.csv".</returns>
    private static string FileNameFor(DateTimeOffset at) => $"WhatDisk Duplicates {at.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.csv";

    /// <summary>
    /// Copies the CSV to the clipboard.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyCsv()
    {
        try
        {
            _shell.CopyText(Csv);
            Message = $"Copied {DisplayFormat.Quantity(Rows.Count, "row", "rows")} to the clipboard.";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Message = $"Could not copy: {exception.Message}";
        }
    }

    /// <summary>
    /// Shows a file in the file manager (Finder or File Explorer), selected in its folder.
    /// </summary>
    /// <param name="row">The file's row.</param>
    [RelayCommand(CanExecute = nameof(CanRevealRow))]
    private void Reveal(DuplicateRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var name = Path.GetFileName(row.Path);
        if (!File.Exists(row.Path))
        {
            Message = $"“{name}” no longer exists.";
            return;
        }

        try
        {
            _shell.Reveal([row.Path]);
            Message = $"Showed “{name}” in {_fileManagerName}.";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Message = $"Could not show “{name}”: {exception.Message}";
        }
    }

    /// <summary>
    /// Determines whether a row's file can be shown in the file manager.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when there is a row and the platform can show files.</returns>
    private bool CanRevealRow(DuplicateRowViewModel? row) => row is not null && CanReveal;

    /// <summary>
    /// Compares the files of the whole scan that share a size: smaller files by hash, larger ones by samples, as the
    /// settings choose. The main window runs the search and reports its progress in the status bar.
    /// </summary>
    /// <returns>A task that completes when the search has finished or was stopped.</returns>
    [RelayCommand(CanExecute = nameof(CanSampledScan))]
    private Task SampledScanAsync() => _host?.SampledScanAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Determines whether Sampled Scan is enabled.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> for the Duplicates tab once a scan has finished, while nothing else runs, when some set
    /// still only shares a size (or the tab follows the scan).
    /// </returns>
    private bool CanSampledScan() => _host is { CanSearch: true } && !IsBusy && (IsLive || HasSizeOnlySets);

    /// <summary>
    /// Hashes the whole of every file of the sets not confirmed by hash yet (those that only matched on size or on
    /// samples), smallest files first, confirming each set, splitting it, or removing the files that differ, as soon
    /// as it is hashed. Sets already confirmed by hash are not read again.
    /// </summary>
    /// <returns>A task that completes when every set has been hashed or the verification was stopped.</returns>
    [RelayCommand(CanExecute = nameof(CanFullHashScan))]
    private Task FullHashScanAsync() =>
        VerifyAsync([.. _sets.Where(static set => set.Comparison != DuplicateComparison.Hash).OrderBy(static set => set.Length).ThenBy(static set => set.Id)]);

    /// <summary>
    /// Determines whether Full Hash Scan is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when a set is not confirmed by hash, nothing is running, and any scan has finished.</returns>
    private bool CanFullHashScan() => !IsBusy && HasUnconfirmedSets && (_host?.CanSearch ?? true);

    /// <summary>
    /// Hashes the files of the set a row belongs to.
    /// </summary>
    /// <param name="row">A row of the set.</param>
    /// <returns>A task that completes when the set has been hashed or the verification was stopped.</returns>
    [RelayCommand(CanExecute = nameof(CanVerifySet))]
    private Task VerifySetAsync(DuplicateRowViewModel? row) =>
        row is not null && FindSet(row.SetId) is { Comparison: not DuplicateComparison.Hash } set ? VerifyAsync([set]) : Task.CompletedTask;

    /// <summary>
    /// Determines whether a row's set can be verified.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when the row's set is not confirmed by hash, nothing is running, and any scan has finished.</returns>
    private bool CanVerifySet(DuplicateRowViewModel? row) => !IsBusy && row is { IsUnconfirmed: true } && (_host?.CanSearch ?? true);

    /// <summary>
    /// Stops the running verification or sampled scan; sets already hashed keep their results.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop()
    {
        if (IsVerifying)
        {
            _verification?.Cancel();
        }
        else if (IsSearching)
        {
            _host?.StopSearch();
        }
    }

    /// <summary>
    /// Includes the files no longer than the small file limit, after warning that there can be very many, or leaves
    /// them out again.
    /// </summary>
    /// <returns>A task that completes when the choice has been made and applied.</returns>
    [RelayCommand(CanExecute = nameof(CanToggleSmallFiles))]
    private Task ToggleSmallFilesAsync() => _host?.SetIncludeSmallFilesAsync(!_host.IncludesSmallFiles) ?? Task.CompletedTask;

    /// <summary>
    /// Determines whether smaller files can be included or left out.
    /// </summary>
    /// <returns><see langword="true"/> for the Duplicates tab once a scan has started, while nothing is running.</returns>
    private bool CanToggleSmallFiles() => _host is { SmallFileLimit: > 0 } && Title is not null && !IsBusy;

    /// <summary>
    /// Re-evaluates the commands when a scan or verification starts or stops.
    /// </summary>
    /// <param name="value">Whether files are being hashed.</param>
    partial void OnIsVerifyingChanged(bool value) => NotifyCommands();

    /// <summary>
    /// Re-evaluates the commands when a sampled scan starts or stops.
    /// </summary>
    /// <param name="value">Whether a sampled scan is running.</param>
    partial void OnIsSearchingChanged(bool value) => NotifyCommands();

    /// <summary>
    /// Re-evaluates whether each command is enabled.
    /// </summary>
    private void NotifyCommands()
    {
        CopyCsvCommand.NotifyCanExecuteChanged();
        SampledScanCommand.NotifyCanExecuteChanged();
        FullHashScanCommand.NotifyCanExecuteChanged();
        VerifySetCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ToggleSmallFilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Hashes the files of sets one after another, applying each set's outcome as soon as it is known and reporting
    /// progress in <see cref="Message"/>. The tab stops following the scan, as the sets now reflect their contents.
    /// </summary>
    /// <param name="sets">The sets.</param>
    /// <returns>A task that completes when every set has been hashed or the verification was stopped.</returns>
    private async Task VerifyAsync(IReadOnlyList<DuplicateSet> sets)
    {
        using var cancellation = new CancellationTokenSource();
        _verification = cancellation;
        IsLive = false;
        IsVerifying = true;
        var totalBytes = sets.Sum(static set => set.Length * set.Paths.Count);
        var doneBytes = 0L;
        var outcomes = new List<(int SetId, string Text)>();
        var confirmed = 0;
        try
        {
            for (var index = 0; index < sets.Count; index++)
            {
                var set = sets[index];
                var position = sets.Count == 1 ? $"set {set.Id}" : $"set {set.Id} ({index + 1} of {sets.Count})";
                var before = doneBytes;
                var reporting = true;
                var progress = new Progress<long>(bytes =>
                {
                    if (reporting)
                    {
                        Message = $"Hashing {position}: {_sizes.Format(before + bytes)} of {_sizes.Format(totalBytes)}…";
                    }
                });
                Message = $"Hashing {position}: {_sizes.Format(before)} of {_sizes.Format(totalBytes)}…";
                DuplicateVerification verification;
                try
                {
                    verification = await _finder.VerifyAsync(set, progress, cancellation.Token);
                }
                finally
                {
                    reporting = false;
                }

                if (!ReferenceEquals(_verification, cancellation))
                {
                    // The tab was cleared meanwhile; the set is no longer shown.
                    throw new OperationCanceledException(cancellation.Token);
                }

                if (Apply(set, [.. verification.Groups.Select(static group => new DuplicateMatch(group.Hash, group.Paths))], DuplicateComparison.Hash, verification.Failures) is { } outcome)
                {
                    outcomes.Add((set.Id, outcome));
                }
                else
                {
                    confirmed++;
                }

                doneBytes += set.Length * set.Paths.Count;
            }

            Message = DescribeVerification(sets, confirmed, outcomes);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_verification, cancellation))
            {
                Message = $"Stopped after hashing {DisplayFormat.Quantity(confirmed + outcomes.Count, "set", "sets")}.";
            }
        }
        finally
        {
            if (ReferenceEquals(_verification, cancellation))
            {
                _verification = null;
            }

            IsVerifying = false;
        }
    }

    /// <summary>
    /// Replaces a size-only set with what a sampled scan found in it, counting its files as hashed or sampled.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="matches">The groups of matching files, largest first.</param>
    /// <param name="comparison">How they were compared.</param>
    private void Compare(DuplicateSet set, IReadOnlyList<DuplicateMatch> matches, DuplicateComparison comparison)
    {
        if (comparison == DuplicateComparison.Hash)
        {
            _hashedFiles += set.Paths.Count;
        }
        else
        {
            _sampledFiles += set.Paths.Count;
        }

        Apply(set, matches, comparison, []);
    }

    /// <summary>
    /// Replaces a set with what comparing its files found: the same set confirmed, the set without the files that
    /// differ, several sets when it held more than one kind of contents (the largest keeps the set's number; the others
    /// get new numbers and follow it), or nothing when no two files match.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="matches">The groups of matching files, largest first.</param>
    /// <param name="comparison">How they were compared.</param>
    /// <param name="failures">The files that could not be read.</param>
    /// <returns><see langword="null"/> when every file was confirmed; otherwise what happened to the set, such as "lost 1 file
    /// that differed".</returns>
    private string? Apply(DuplicateSet set, IReadOnlyList<DuplicateMatch> matches, DuplicateComparison comparison, IReadOnlyList<DuplicateFailure> failures)
    {
        var index = _sets.FindIndex(candidate => candidate.Id == set.Id);
        var firstRow = Rows.IndexOf(Rows.First(row => row.SetId == set.Id));
        _failures.AddRange(failures);
        DuplicateSet[] replacements =
        [
            .. matches.Select((match, position) =>
                new DuplicateSet(position == 0 ? set.Id : _nextSetId++, set.Length, match.Hash, match.Paths, comparison)),
        ];
        _sets.RemoveAt(index);
        _sets.InsertRange(index, replacements);

        string? outcome;
        if (replacements is [var whole] && whole.Paths.SequenceEqual(set.Paths, StringComparer.Ordinal))
        {
            // Confirmed: the rows stay, so the selection and scroll position do too.
            for (var row = firstRow; row < firstRow + set.Paths.Count; row++)
            {
                Rows[row].Record(whole.Hash, comparison);
            }

            outcome = null;
        }
        else
        {
            Rows.RemoveRange(firstRow, set.Paths.Count);
            Rows.InsertRange(firstRow, [.. replacements.SelectMany(CreateRows)]);
            var removed = set.Paths.Count - replacements.Sum(static replacement => replacement.Paths.Count);
            outcome = replacements.Length switch
            {
                0 => "had no identical files and was removed",
                1 => $"lost {DisplayFormat.Quantity(removed, "file", "files")} that differed",
                _ => $"split into sets {string.Join(" and ", replacements.Select(static replacement => replacement.Id))}",
            };
        }

        UpdateShading();
        UpdateSummary();
        return outcome;
    }

    /// <summary>
    /// Finds a set by its number.
    /// </summary>
    /// <param name="id">The set's number.</param>
    /// <returns>The set, or <see langword="null"/> when there is none.</returns>
    private DuplicateSet? FindSet(int id) => _sets.Find(set => set.Id == id);

    /// <summary>
    /// Gets the number for the next set split off by hashing.
    /// </summary>
    /// <returns>One more than the largest set number shown.</returns>
    private int NextSetId() => _sets.Count == 0 ? 1 : _sets.Max(static set => set.Id) + 1;

    /// <summary>
    /// Makes the rows show the sets, keeping the row of each file already listed the same way (same size, size units,
    /// comparison and hash) and renumbering it, so the selection and scroll position stay.
    /// </summary>
    private void ReconcileRows()
    {
        var existing = new Dictionary<string, DuplicateRowViewModel>(Rows.Count, StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            existing.TryAdd(row.Path, row);
        }

        var desired = new List<DuplicateRowViewModel>(_sets.Sum(static set => set.Paths.Count));
        foreach (var set in _sets)
        {
            var sizeText = _sizes.Format(set.Length);
            foreach (var path in set.Paths)
            {
                if (existing.Remove(path, out var row) && row.Size == set.Length && row.Comparison == set.Comparison
                    && row.SizeText == sizeText && string.Equals(row.Hash, set.Hash, StringComparison.Ordinal))
                {
                    row.SetId = set.Id;
                    desired.Add(row);
                }
                else
                {
                    desired.Add(CreateRow(set, path, _finder.Options));
                }
            }
        }

        ListReconciler.Apply(Rows, desired);
    }

    /// <summary>
    /// Creates the rows for a set's files.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <returns>One row per file.</returns>
    private IEnumerable<DuplicateRowViewModel> CreateRows(DuplicateSet set) => set.Paths.Select(path => CreateRow(set, path, _finder.Options));

    /// <summary>
    /// Creates the row for one file of a set.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="path">The file.</param>
    /// <param name="options">How the files were compared, for the sampling described in the row's tooltip.</param>
    /// <returns>The row.</returns>
    private DuplicateRowViewModel CreateRow(DuplicateSet set, string path, DuplicateFinderOptions options) =>
        new(set.Id, path, set.Length, _sizes.Format(set.Length), _sizes.FormatExact(set.Length), set.Hash, set.Comparison)
        {
            SamplesText = options.SamplesText,
        };

    /// <summary>
    /// Suggests double-clicking a file, unless something more useful is shown.
    /// </summary>
    private void ShowRevealHint()
    {
        if (Message is null && HasRows && CanReveal)
        {
            Message = $"Double-click a file to show it in {_fileManagerName}.";
        }
    }

    /// <summary>
    /// Shades every other set, so neighbouring sets can be told apart.
    /// </summary>
    private void UpdateShading()
    {
        var shaded = _sets.Where(static (_, index) => index % 2 == 1).Select(static set => set.Id).ToHashSet();
        foreach (var row in Rows)
        {
            row.IsAlternate = shaded.Contains(row.SetId);
        }
    }

    /// <summary>
    /// Describes the sets shown and the files that could not be read, and updates which commands are enabled.
    /// </summary>
    private void UpdateSummary()
    {
        var files = _sets.Sum(static set => set.Paths.Count);
        var freed = _sizes.Format(_sets.Sum(static set => set.WastedBytes));
        var bySize = _sets.Count(static set => set.Comparison == DuplicateComparison.Size);
        var sampled = _sets.Count(static set => set.Comparison == DuplicateComparison.Samples);
        var hashed = _sets.Count - bySize - sampled;
        var over = _host is { SmallFileLimit: > 0 and var limit, IncludesSmallFiles: false } ? $" over {DuplicateFinderOptions.DescribeBinarySize(limit)}" : string.Empty;
        if (_sets.Count == 0)
        {
            (Summary, EmptyHint) = (Title, IsLive) switch
            {
                (null, _) => ("No scan yet.", $"Scan a volume or a folder: files{over} that share a size are listed here as the scan finds them."),
                (_, true) => ("No files share a size.", $"No two files{over} have the same size."),
                _ => ("No duplicate files found.", "No two files have the same contents."),
            };
        }
        else if (bySize == _sets.Count)
        {
            Summary = $"{DisplayFormat.Quantity(_sets.Count, "set", "sets")} of files{over} that share a size: {DisplayFormat.Quantity(files, "file", "files")}. "
                + $"If they are duplicates, keeping one file of each set would free {freed}.";
        }
        else
        {
            Summary = $"{DisplayFormat.Quantity(_sets.Count, "set", "sets")} of duplicates: {DisplayFormat.Quantity(files, "file", "files")}. "
                + $"Keeping one file of each set would free {freed}.";
        }

        VerificationSummary = DescribeComparisons(hashed, sampled, bySize);
        if (_failures.Count > 0)
        {
            FailureSummary = $"{DisplayFormat.Quantity(_failures.Count, "file", "files")} could not be read and {(_failures.Count == 1 ? "was" : "were")} left out.";
            FailureDetails = string.Join(Environment.NewLine, _failures.Take(ListedFailures).Select(static failure => $"{failure.Path}: {failure.Reason}"))
                + (_failures.Count > ListedFailures ? $"{Environment.NewLine}… and {DisplayFormat.Count(_failures.Count - ListedFailures)} more" : string.Empty);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasUnconfirmedSets));
        OnPropertyChanged(nameof(HasSizeOnlySets));
        OnPropertyChanged(nameof(CanCopy));
        NotifyCommands();
    }

    /// <summary>
    /// Describes how surely the sets are duplicates.
    /// </summary>
    /// <param name="hashed">The sets confirmed by hash.</param>
    /// <param name="sampled">The sets that only matched on samples.</param>
    /// <param name="bySize">The sets that only share a size.</param>
    /// <returns>The description, or <see langword="null"/> when there are no sets.</returns>
    private string? DescribeComparisons(int hashed, int sampled, int bySize)
    {
        if (hashed + sampled + bySize == 0)
        {
            return null;
        }

        if (sampled + bySize == 0)
        {
            return "Every set is confirmed by hash.";
        }

        if (hashed + sampled == 0)
        {
            return _host is null
                ? "Only sizes were compared. Run a Full Hash Scan before deleting anything."
                : "Only sizes were compared. Run a Sampled Scan or a Full Hash Scan before deleting anything.";
        }

        var parts = new List<string>();
        if (hashed > 0)
        {
            parts.Add($"{DisplayFormat.Quantity(hashed, "set", "sets")} confirmed by hash");
        }

        if (sampled > 0)
        {
            parts.Add($"{DisplayFormat.Quantity(sampled, "set", "sets")} of files over {_finder.Options.HashLimitText} only matched on {_finder.Options.SamplesText}");
        }

        if (bySize > 0)
        {
            parts.Add($"{DisplayFormat.Quantity(bySize, "set", "sets")} only share a size");
        }

        return $"{string.Join("; ", parts)}. Run a Full Hash Scan before deleting anything.";
    }
}

/// <summary>
/// One file in a duplicates tab.
/// </summary>
public sealed partial class DuplicateRowViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateRowViewModel"/> class.
    /// </summary>
    /// <param name="setId">The number of the file's set.</param>
    /// <param name="path">The file's full path.</param>
    /// <param name="size">The file's length in bytes.</param>
    /// <param name="sizeText">The length, formatted for reading.</param>
    /// <param name="exactSizeText">The length in bytes, formatted.</param>
    /// <param name="hash">The hash of the file's contents, or <see langword="null"/> when it was not hashed.</param>
    /// <param name="comparison">How the file was compared.</param>
    public DuplicateRowViewModel(int setId, string path, long size, string sizeText, string exactSizeText, string? hash, DuplicateComparison comparison)
    {
        SetId = setId;
        Path = path;
        Size = size;
        SizeText = sizeText;
        ExactSizeText = exactSizeText;
        Hash = hash;
        Comparison = comparison;
    }

    /// <summary>
    /// Gets the number of the file's set, which changes while a scan finds more files that share a size.
    /// </summary>
    [ObservableProperty]
    public partial int SetId { get; internal set; }

    /// <summary>
    /// Gets the file's full path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the file's length in bytes.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the length, formatted for reading.
    /// </summary>
    public string SizeText { get; }

    /// <summary>
    /// Gets the length in bytes, formatted.
    /// </summary>
    public string ExactSizeText { get; }

    /// <summary>
    /// Gets the hash of the file's contents, or <see langword="null"/> when it was not hashed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashText))]
    public partial string? Hash { get; private set; }

    /// <summary>
    /// Gets how the file was compared.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampled))]
    [NotifyPropertyChangedFor(nameof(IsUnconfirmed))]
    [NotifyPropertyChangedFor(nameof(ComparisonText))]
    [NotifyPropertyChangedFor(nameof(ComparisonTip))]
    public partial DuplicateComparison Comparison { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the row belongs to a shaded set (every other set is shaded).
    /// </summary>
    [ObservableProperty]
    public partial bool IsAlternate { get; set; }

    /// <summary>
    /// Gets the hash for display: the hash, or a dash when the file was not hashed.
    /// </summary>
    public string HashText => Hash ?? "—";

    /// <summary>
    /// Gets a value indicating whether the file was only compared by samples.
    /// </summary>
    public bool IsSampled => Comparison == DuplicateComparison.Samples;

    /// <summary>
    /// Gets a value indicating whether the file is not confirmed by hash: it only matched on samples, or only on size.
    /// </summary>
    public bool IsUnconfirmed => Comparison != DuplicateComparison.Hash;

    /// <summary>
    /// Gets how the file was compared, for display: "Hash", "Sampled" or "Size".
    /// </summary>
    public string ComparisonText => DuplicateCsv.ComparisonName(Comparison);

    /// <summary>
    /// Gets how the file was sampled, such as "64 samples of 4 KiB", for <see cref="ComparisonTip"/>.
    /// </summary>
    public string SamplesText { get; init; } = DuplicateFinderOptions.Default.SamplesText;

    /// <summary>
    /// Gets what the comparison means.
    /// </summary>
    public string ComparisonTip => Comparison switch
    {
        DuplicateComparison.Hash => "The whole file was hashed (XXH128) and matches the others in its set.",
        DuplicateComparison.Samples => $"Matched on {SamplesText} spread through the file; the rest was not compared. Verify the set by hash to be sure.",
        _ => "Only the size matches the others in its set; the contents have not been compared. Run a scan, or verify the set by hash.",
    };

    /// <summary>
    /// Records that comparing the file's contents matched it with the others in its set.
    /// </summary>
    /// <param name="hash">The hash of its contents, or <see langword="null"/> when it was only sampled.</param>
    /// <param name="comparison">How it was compared.</param>
    internal void Record(string? hash, DuplicateComparison comparison)
    {
        Hash = hash;
        Comparison = comparison;
    }
}
