using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Platform;

namespace WhatDisk.ViewModels;

/// <summary>
/// The results of a duplicate search, for the Duplicates window: one row per file, grouped by set, with a summary,
/// the same CSV for copying to the clipboard and exporting to a file, a way to show a file in the file manager, and
/// commands that confirm sets matched by samples by hashing their files.
/// </summary>
public sealed partial class DuplicateResultsViewModel : ObservableObject
{
    /// <summary>The most failures listed in the tooltip.</summary>
    private const int ListedFailures = 20;

    /// <summary>Copies the CSV to the clipboard and shows files in the file manager.</summary>
    private readonly IShellService _shell;

    /// <summary>Hashes the files of sampled sets.</summary>
    private readonly DuplicateFinder _finder;

    /// <summary>Formats sizes in the platform's units.</summary>
    private readonly ByteSizeFormatter _sizes;

    /// <summary>The file manager's name, such as "Finder" or "File Explorer".</summary>
    private readonly string _fileManagerName;

    /// <summary>The sets shown, in display order.</summary>
    private readonly List<DuplicateSet> _sets;

    /// <summary>The files that could not be read, during the search or since.</summary>
    private readonly List<DuplicateFailure> _failures;

    /// <summary>The number the next set split off by hashing gets.</summary>
    private int _nextSetId;

    /// <summary>Stops the running verification.</summary>
    private CancellationTokenSource? _verification;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateResultsViewModel"/> class.
    /// </summary>
    /// <param name="result">The search's result.</param>
    /// <param name="scope">What was searched, for the window title, such as the scanned folder.</param>
    /// <param name="sizes">Formats sizes in the platform's units.</param>
    /// <param name="shell">Copies text to the clipboard and shows files in the file manager.</param>
    /// <param name="finder">Hashes the files of sampled sets.</param>
    /// <param name="fileManagerName">The file manager's name, such as "Finder" or "File Explorer".</param>
    /// <param name="foundAt">When the search finished, in local time, for the suggested file name.</param>
    public DuplicateResultsViewModel(
        DuplicateSearchResult result,
        string scope,
        ByteSizeFormatter sizes,
        IShellService shell,
        DuplicateFinder finder,
        string fileManagerName,
        DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(finder);
        ArgumentException.ThrowIfNullOrEmpty(fileManagerName);
        Result = result;
        _sizes = sizes;
        _shell = shell;
        _finder = finder;
        _fileManagerName = fileManagerName;
        _sets = [.. result.Sets];
        _failures = [.. result.Failures];
        _nextSetId = _sets.Count == 0 ? 1 : _sets.Max(static set => set.Id) + 1;
        Title = $"Duplicates in {scope}";
        Rows = [.. _sets.SelectMany(CreateRows)];
        SuggestedFileName = $"WhatDisk Duplicates {foundAt.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.csv";
        Details = DescribeSearch(result);
        UpdateShading();
        UpdateSummary();
        if (HasRows && CanReveal)
        {
            Message = $"Double-click a file to show it in {fileManagerName}.";
        }
    }

    /// <summary>
    /// Gets the search's result, as found.
    /// </summary>
    public DuplicateSearchResult Result { get; }

    /// <summary>
    /// Gets the sets shown, as confirmed so far.
    /// </summary>
    public IReadOnlyList<DuplicateSet> Sets => _sets;

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets one row per duplicate file, in set order.
    /// </summary>
    public ObservableCollection<DuplicateRowViewModel> Rows { get; }

    /// <summary>
    /// Gets a value indicating whether any duplicates are shown.
    /// </summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// Gets a value indicating whether any set was only matched by samples.
    /// </summary>
    public bool HasSampledSets => _sets.Exists(static set => set.Comparison == DuplicateComparison.Samples);

    /// <summary>
    /// Gets how many files each stage of the search compared, and which were left out.
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Gets the results as CSV, as copied and exported.
    /// </summary>
    public string Csv => DuplicateCsv.Format(_sets);

    /// <summary>
    /// Gets the file name suggested when exporting.
    /// </summary>
    public string SuggestedFileName { get; }

    /// <summary>
    /// Gets a value indicating whether the platform can show files in its file manager.
    /// </summary>
    public bool CanReveal => (_shell.Capabilities & ShellCapabilities.Reveal) != 0;

    /// <summary>
    /// Gets a value indicating whether there is anything to copy, the platform can copy text to the clipboard, and no
    /// verification is changing the sets.
    /// </summary>
    public bool CanCopy => HasRows && !IsVerifying && (_shell.Capabilities & ShellCapabilities.CopyText) != 0;

    /// <summary>
    /// Gets what was found: the number of sets and files, and the space that could be freed.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Gets how the sets were compared: how many were confirmed by hash and how many only matched on samples.
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
    /// Gets a value indicating whether files are being hashed to confirm sampled sets.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    [NotifyCanExecuteChangedFor(nameof(CopyCsvCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifySetCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopVerifyingCommand))]
    public partial bool IsVerifying { get; private set; }

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
    /// Describes how many files each stage compared, and what was left out.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The description.</returns>
    private static string DescribeSearch(DuplicateSearchResult result)
    {
        var text = $"Of {DisplayFormat.Quantity(result.FilesConsidered, "file", "files")}, {DisplayFormat.Count(result.SizeMatches)} shared a size with another: "
            + $"{DisplayFormat.Count(result.HashedFiles)} up to 1 MiB were hashed, and {DisplayFormat.Count(result.SampledFiles)} larger ones were compared by samples.";
        var leftOut = new List<string>();
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
        return outcomes.Count == 0 ? $"{text}." : $"{text}; {string.Join("; ", outcomes.Select(static outcome => $"set {outcome.SetId} {outcome.Text}"))}.";
    }

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
    /// Hashes the files of every set matched only by samples, largest waste first, confirming each set, splitting it,
    /// or removing the files that differ. Sets already confirmed by hash are not read again.
    /// </summary>
    /// <returns>A task that completes when every set has been hashed or the verification was stopped.</returns>
    [RelayCommand(CanExecute = nameof(CanVerifyAll))]
    private Task VerifyAllAsync() => VerifyAsync([.. _sets.Where(static set => set.Comparison == DuplicateComparison.Samples)]);

    /// <summary>
    /// Determines whether Verify All is enabled.
    /// </summary>
    /// <returns><see langword="true"/> when a set was only sampled and nothing is being verified.</returns>
    private bool CanVerifyAll() => !IsVerifying && HasSampledSets;

    /// <summary>
    /// Hashes the files of the set a row belongs to.
    /// </summary>
    /// <param name="row">A row of the set.</param>
    /// <returns>A task that completes when the set has been hashed or the verification was stopped.</returns>
    [RelayCommand(CanExecute = nameof(CanVerifySet))]
    private Task VerifySetAsync(DuplicateRowViewModel? row) =>
        row is not null && FindSet(row.SetId) is { Comparison: DuplicateComparison.Samples } set ? VerifyAsync([set]) : Task.CompletedTask;

    /// <summary>
    /// Determines whether a row's set can be verified.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when the row's set was only sampled and nothing is being verified.</returns>
    private bool CanVerifySet(DuplicateRowViewModel? row) => !IsVerifying && row is { IsSampled: true };

    /// <summary>
    /// Stops the running verification; sets already hashed keep their results.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsVerifying))]
    private void StopVerifying() => _verification?.Cancel();

    /// <summary>
    /// Hashes the files of sets one after another, applying each set's outcome as soon as it is known and reporting
    /// progress in <see cref="Message"/>.
    /// </summary>
    /// <param name="sets">The sets.</param>
    /// <returns>A task that completes when every set has been hashed or the verification was stopped.</returns>
    private async Task VerifyAsync(IReadOnlyList<DuplicateSet> sets)
    {
        using var cancellation = new CancellationTokenSource();
        _verification = cancellation;
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

                if (Apply(set, verification) is { } outcome)
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
            Message = $"Stopped after hashing {DisplayFormat.Quantity(confirmed + outcomes.Count, "set", "sets")}.";
        }
        finally
        {
            _verification = null;
            IsVerifying = false;
        }
    }

    /// <summary>
    /// Replaces a sampled set with what hashing found: the same set confirmed by hash, the set without the files that
    /// differ, several sets when it held more than one kind of contents (the largest keeps the set's number; the
    /// others get new numbers and follow it), or nothing when no two files are identical.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="verification">What hashing found.</param>
    /// <returns><see langword="null"/> when every file was confirmed; otherwise what happened to the set, such as "lost 1 file
    /// that differed".</returns>
    private string? Apply(DuplicateSet set, DuplicateVerification verification)
    {
        var index = _sets.FindIndex(candidate => candidate.Id == set.Id);
        var firstRow = Rows.IndexOf(Rows.First(row => row.SetId == set.Id));
        _failures.AddRange(verification.Failures);
        DuplicateSet[] replacements =
        [
            .. verification.Groups.Select((group, position) =>
                new DuplicateSet(position == 0 ? set.Id : _nextSetId++, set.Length, group.Hash, group.Paths, DuplicateComparison.Hash)),
        ];
        _sets.RemoveAt(index);
        _sets.InsertRange(index, replacements);

        string? outcome;
        if (replacements is [var whole] && whole.Paths.SequenceEqual(set.Paths, StringComparer.Ordinal))
        {
            // Confirmed: the rows stay, so the selection and scroll position do too.
            for (var row = firstRow; row < firstRow + set.Paths.Count; row++)
            {
                Rows[row].Confirm(whole.Hash!);
            }

            outcome = null;
        }
        else
        {
            for (var count = 0; count < set.Paths.Count; count++)
            {
                Rows.RemoveAt(firstRow);
            }

            var position = firstRow;
            foreach (var row in replacements.SelectMany(CreateRows))
            {
                Rows.Insert(position++, row);
            }

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
    /// Creates the rows for a set's files.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <returns>One row per file.</returns>
    private IEnumerable<DuplicateRowViewModel> CreateRows(DuplicateSet set) =>
        set.Paths.Select(path => new DuplicateRowViewModel(set.Id, path, set.Length, _sizes.Format(set.Length), _sizes.FormatExact(set.Length), set.Hash, set.Comparison));

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
        Summary = _sets.Count == 0
            ? "No duplicate files found."
            : $"{DisplayFormat.Quantity(_sets.Count, "set", "sets")} of duplicates: {DisplayFormat.Quantity(files, "file", "files")}. "
              + $"Keeping one file of each set would free {_sizes.Format(_sets.Sum(static set => set.WastedBytes))}.";
        var sampled = _sets.Count(static set => set.Comparison == DuplicateComparison.Samples);
        VerificationSummary = _sets.Count == 0
            ? null
            : sampled == 0
                ? "Every set is confirmed by hash."
                : $"{DisplayFormat.Quantity(_sets.Count - sampled, "set", "sets")} confirmed by hash; {DisplayFormat.Quantity(sampled, "set", "sets")} of files over 1 MiB "
                  + $"only matched on {DuplicateFinder.SampleCount} samples of {DuplicateFinder.SampleLength / 1024} KiB. Verify by hash before deleting anything.";
        if (_failures.Count > 0)
        {
            FailureSummary = $"{DisplayFormat.Quantity(_failures.Count, "file", "files")} could not be read and {(_failures.Count == 1 ? "was" : "were")} left out.";
            FailureDetails = string.Join(Environment.NewLine, _failures.Take(ListedFailures).Select(static failure => $"{failure.Path}: {failure.Reason}"))
                + (_failures.Count > ListedFailures ? $"{Environment.NewLine}… and {DisplayFormat.Count(_failures.Count - ListedFailures)} more" : string.Empty);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasSampledSets));
        OnPropertyChanged(nameof(CanCopy));
        CopyCsvCommand.NotifyCanExecuteChanged();
        VerifyAllCommand.NotifyCanExecuteChanged();
        VerifySetCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// One file in the Duplicates window.
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
    /// <param name="hash">The hash of the file's contents, or <see langword="null"/> when it was only sampled.</param>
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
    /// Gets the number of the file's set.
    /// </summary>
    public int SetId { get; }

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
    /// Gets the hash of the file's contents, or <see langword="null"/> when it was only sampled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashText))]
    public partial string? Hash { get; private set; }

    /// <summary>
    /// Gets how the file was compared.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampled))]
    [NotifyPropertyChangedFor(nameof(ComparisonText))]
    [NotifyPropertyChangedFor(nameof(ComparisonTip))]
    public partial DuplicateComparison Comparison { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the row belongs to a shaded set (every other set is shaded).
    /// </summary>
    [ObservableProperty]
    public partial bool IsAlternate { get; set; }

    /// <summary>
    /// Gets the hash for display: the hash, or a dash when the file was only sampled.
    /// </summary>
    public string HashText => Hash ?? "—";

    /// <summary>
    /// Gets a value indicating whether the file was only compared by samples.
    /// </summary>
    public bool IsSampled => Comparison == DuplicateComparison.Samples;

    /// <summary>
    /// Gets how the file was compared, for display: "Hash" or "Sampled".
    /// </summary>
    public string ComparisonText => DuplicateCsv.ComparisonName(Comparison);

    /// <summary>
    /// Gets what the comparison means.
    /// </summary>
    public string ComparisonTip => IsSampled
        ? $"Matched on {DuplicateFinder.SampleCount} samples of {DuplicateFinder.SampleLength / 1024} KiB spread through the file; the rest was not compared. Verify the set by hash to be sure."
        : "The whole file was hashed (XXH128) and matches the others in its set.";

    /// <summary>
    /// Records that hashing confirmed the file.
    /// </summary>
    /// <param name="hash">The hash of its contents.</param>
    internal void Confirm(string hash)
    {
        Hash = hash;
        Comparison = DuplicateComparison.Hash;
    }
}
