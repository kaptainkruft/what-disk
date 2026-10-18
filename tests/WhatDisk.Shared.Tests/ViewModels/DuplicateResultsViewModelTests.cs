using System.Globalization;
using System.IO.Hashing;
using System.Text;
using Avalonia.Headless.XUnit;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the Duplicates window's view model: its rows and summary, that copying and exporting give the same CSV, and
/// that verifying sampled sets by hash confirms, trims or splits them.
/// </summary>
public sealed class DuplicateResultsViewModelTests : IDisposable
{
    /// <summary>Formats sizes the same way on every machine.</summary>
    private static readonly ByteSizeFormatter Sizes = new(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture);

    /// <summary>Records the copies.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>Holds the files of the verification tests, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-verify-");

    /// <inheritdoc/>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// There is one row per file, set by set, with every other set shaded and sampled files marked; the summary counts
    /// the sets, files and space that could be freed, says how many sets are only sampled, and the details count each
    /// stage and what was left out.
    /// </summary>
    [Fact]
    public void Rows_AndSummary_DescribeTheSets()
    {
        var results = Create(Result());

        Assert.Equal("Duplicates in /data", results.Title);
        Assert.Equal(
            [
                (1, "/data/a", "2.00 MB", "—", "Sampled", false),
                (1, "/data/b", "2.00 MB", "—", "Sampled", false),
                (1, "/data/c", "2.00 MB", "—", "Sampled", false),
                (2, "/data/d", "10 bytes", "00000000000000000000000000000002", "Hash", true),
                (2, "/data/e", "10 bytes", "00000000000000000000000000000002", "Hash", true),
            ],
            results.Rows.Select(static row => (row.SetId, row.Path, row.SizeText, row.HashText, row.ComparisonText, row.IsAlternate)));
        Assert.Equal("2,000,000 bytes", results.Rows[0].ExactSizeText);
        Assert.Equal("2 sets of duplicates: 5 files. Keeping one file of each set would free 4.00 MB.", results.Summary);
        Assert.Equal("1 set confirmed by hash; 1 set of files over 1 MiB only matched on 64 samples of 4 KiB. Verify by hash before deleting anything.", results.VerificationSummary);
        Assert.Equal("Of 100 files, 7 shared a size with another: 4 up to 1 MiB were hashed, and 3 larger ones were compared by samples. Left out: 3 empty files, 1 cloud-only file, 2 extra hard links.", results.Details);
        Assert.Equal("WhatDisk Duplicates 2026-10-17 0930.csv", results.SuggestedFileName);
        Assert.True(results.HasSampledSets);
        Assert.Null(results.FailureSummary);
    }

    /// <summary>
    /// Failures are summarised, and the first few are listed with their reasons.
    /// </summary>
    [Fact]
    public void Failures_AreSummarisedAndListed()
    {
        var failures = Enumerable.Range(1, 22).Select(static index => new DuplicateFailure($"/data/f{index}", "Access denied")).ToArray();

        var results = Create(Result() with { Failures = failures });

        Assert.Equal("22 files could not be read and were left out.", results.FailureSummary);
        var lines = results.FailureDetails!.Split(Environment.NewLine);
        Assert.Equal(21, lines.Length);
        Assert.Equal("/data/f1: Access denied", lines[0]);
        Assert.Equal("… and 2 more", lines[^1]);
    }

    /// <summary>
    /// Copy puts the CSV on the clipboard, and exporting writes the same CSV after a UTF-8 byte order mark, replacing
    /// a longer file's contents completely.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task CopyAndExport_GiveTheSameCsv()
    {
        var results = Create(Result());
        var expected = DuplicateCsv.Format(Result().Sets);

        Assert.True(results.CopyCsvCommand.CanExecute(null));
        results.CopyCsvCommand.Execute(null);
        Assert.Equal([$"copy text: {expected}"], _shell.Calls);
        Assert.Equal("Copied 5 rows to the clipboard.", results.Message);

        using var file = new MemoryStream();
        file.Write(new byte[expected.Length * 2]);
        await results.ExportAsync("dupes.csv", () => Task.FromResult<Stream>(new NonClosingStream(file)));

        Assert.Equal([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(expected)], file.ToArray());
        Assert.Equal("Exported 5 rows to “dupes.csv”.", results.Message);
    }

    /// <summary>
    /// A failed export is reported, not thrown.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Export_Failure_IsReported()
    {
        var results = Create(Result());

        await results.ExportAsync("dupes.csv", static () => throw new UnauthorizedAccessException("Read-only folder."));

        Assert.Equal("Could not export to “dupes.csv”: Read-only folder.", results.Message);
    }

    /// <summary>
    /// With no duplicates, or where the platform cannot copy text, Copy is disabled.
    /// </summary>
    [Fact]
    public void Copy_IsDisabledWithNothingToCopyOrNoClipboard()
    {
        var none = Create(Result() with { Sets = [] });
        Assert.False(none.HasRows);
        Assert.False(none.CopyCsvCommand.CanExecute(null));
        Assert.Equal("No duplicate files found.", none.Summary);

        _shell.Capabilities = ShellCapabilities.Open;
        Assert.False(Create(Result()).CopyCsvCommand.CanExecute(null));
    }

    /// <summary>
    /// Revealing a row shows its file in the file manager, named in the hint and the outcome; a file that has gone
    /// since the search is reported instead.
    /// </summary>
    [Fact]
    public void Reveal_ShowsTheFileInTheFileManager()
    {
        var file = Path.GetTempFileName();
        try
        {
            var results = Create(Result() with { Sets = [new DuplicateSet(1, 10, "00000000000000000000000000000001", [file, "/no/such/copy.bin"], DuplicateComparison.Hash)] });
            Assert.Equal("Double-click a file to show it in Finder.", results.Message);

            Assert.True(results.RevealCommand.CanExecute(results.Rows[0]));
            results.RevealCommand.Execute(results.Rows[0]);
            Assert.Equal([$"reveal: {file}"], _shell.Calls);
            Assert.Equal($"Showed “{Path.GetFileName(file)}” in Finder.", results.Message);

            results.RevealCommand.Execute(results.Rows[1]);
            Assert.Single(_shell.Calls);
            Assert.Equal("“copy.bin” no longer exists.", results.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Where the platform cannot show files in a file manager, revealing is disabled and not offered.
    /// </summary>
    [Fact]
    public void Reveal_IsDisabledWithoutAFileManager()
    {
        _shell.Capabilities = ShellCapabilities.Open;

        var results = Create(Result());

        Assert.False(results.RevealCommand.CanExecute(results.Rows[0]));
        Assert.Null(results.Message);
    }

    /// <summary>
    /// Verifying a set hashes its files: a file that only matched on samples drops out of its set, the files left are
    /// confirmed with their hash, and the summary and CSV follow. Verify All then confirms the remaining sampled set in
    /// place, keeping its rows, and leaves nothing to verify.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Verify_ConfirmsTrimsAndKeepsRows()
    {
        var content = Content(1, 1_500_000);
        var other = Content(2, 1_200_000);
        DuplicateSet[] sets =
        [
            new(1, 1_500_000, null, [Write("a", content), Write("b", content), Write("c", Changed(content, 10_000))], DuplicateComparison.Samples),
            new(2, 1_200_000, null, [Write("d", other), Write("e", other)], DuplicateComparison.Samples),
        ];
        var results = Create(Result() with { Sets = sets });
        var hash = Convert.ToHexStringLower(XxHash128.Hash(content));

        await results.VerifySetCommand.ExecuteAsync(results.Rows[2]);

        Assert.Equal([(1, "Hash", hash), (1, "Hash", hash), (2, "Sampled", "—"), (2, "Sampled", "—")], results.Rows.Select(static row => (row.SetId, row.ComparisonText, row.HashText)));
        Assert.Equal("Hashed set 1: it lost 1 file that differed.", results.Message);
        Assert.Equal(DuplicateComparison.Hash, results.Sets[0].Comparison);
        Assert.Contains($",1500000,{hash},Hash\r\n", results.Csv, StringComparison.Ordinal);
        Assert.Equal("2 sets of duplicates: 4 files. Keeping one file of each set would free 2.70 MB.", results.Summary);
        Assert.False(results.VerifySetCommand.CanExecute(results.Rows[0]));
        Assert.True(results.VerifySetCommand.CanExecute(results.Rows[2]));

        var kept = results.Rows[2];
        await results.VerifyAllCommand.ExecuteAsync(null);

        Assert.Same(kept, results.Rows[2]);
        Assert.Equal("Hash", kept.ComparisonText);
        Assert.Equal("Hashed set 2: every file is identical.", results.Message);
        Assert.Equal("Every set is confirmed by hash.", results.VerificationSummary);
        Assert.False(results.HasSampledSets);
        Assert.False(results.VerifyAllCommand.CanExecute(null));
    }

    /// <summary>
    /// A set holding two different contents splits: the larger group keeps the set's number, the other gets the next
    /// free number and follows it; a set with no identical files disappears.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task VerifyAll_SplitsAndRemovesSets()
    {
        var first = Content(3, 1_100_000);
        var second = Changed(first, 10_000);
        var third = Content(4, 1_300_000);
        DuplicateSet[] sets =
        [
            new(1, 1_300_000, null, [Write("p", third), Write("q", Changed(third, 10_000))], DuplicateComparison.Samples),
            new(2, 1_100_000, null, [Write("x1", first), Write("x2", first), Write("y1", second), Write("y2", second), Write("y3", second)], DuplicateComparison.Samples),
            new(3, 10, "00000000000000000000000000000003", ["/data/s", "/data/t"], DuplicateComparison.Hash),
        ];
        var results = Create(Result() with { Sets = sets });

        await results.VerifyAllCommand.ExecuteAsync(null);

        Assert.Equal([2, 2, 2, 4, 4, 3, 3], results.Rows.Select(static row => row.SetId));
        Assert.Equal([PathOf("y1"), PathOf("y2"), PathOf("y3"), PathOf("x1"), PathOf("x2")], results.Rows.Take(5).Select(static row => row.Path));
        Assert.Equal([false, false, false, true, true, false, false], results.Rows.Select(static row => row.IsAlternate));
        Assert.Equal("Hashed 2 sets: 0 confirmed; set 1 had no identical files and was removed; set 2 split into sets 2 and 4.", results.Message);
    }

    /// <summary>
    /// While hashing, Copy, Verify All and Verify Set are disabled and Stop is enabled; afterwards it is the other way
    /// round.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task WhileVerifying_OtherCommandsWait()
    {
        var content = Content(5, 4_000_000);
        var results = Create(Result() with { Sets = [new DuplicateSet(1, 4_000_000, null, [Write("a", content), Write("b", content)], DuplicateComparison.Samples)] });
        Assert.False(results.StopVerifyingCommand.CanExecute(null));

        var verifying = results.VerifyAllCommand.ExecuteAsync(null);

        Assert.True(results.IsVerifying);
        Assert.StartsWith("Hashing set 1: ", results.Message, StringComparison.Ordinal);
        Assert.False(results.CopyCsvCommand.CanExecute(null));
        Assert.False(results.VerifyAllCommand.CanExecute(null));
        Assert.False(results.VerifySetCommand.CanExecute(results.Rows[0]));
        Assert.True(results.StopVerifyingCommand.CanExecute(null));

        results.StopVerifyingCommand.Execute(null);
        await verifying;

        Assert.False(results.IsVerifying);
        Assert.True(results.CopyCsvCommand.CanExecute(null));
        Assert.False(results.StopVerifyingCommand.CanExecute(null));
    }

    /// <summary>
    /// Creates a result with two sets: three 2 MB files matched by samples and two 10-byte files confirmed by hash.
    /// </summary>
    /// <returns>The result.</returns>
    private static DuplicateSearchResult Result() => new(
        [
            new DuplicateSet(1, 2_000_000, null, ["/data/a", "/data/b", "/data/c"], DuplicateComparison.Samples),
            new DuplicateSet(2, 10, "00000000000000000000000000000002", ["/data/d", "/data/e"], DuplicateComparison.Hash),
        ],
        FilesConsidered: 100,
        SizeMatches: 7,
        HashedFiles: 4,
        SampledFiles: 3,
        EmptyFiles: 3,
        OfflineFiles: 1,
        HardLinks: 2,
        Failures: []);

    /// <summary>
    /// Creates repeatable pseudo-random contents.
    /// </summary>
    /// <param name="seed">Selects the contents.</param>
    /// <param name="length">The length.</param>
    /// <returns>The contents.</returns>
    private static byte[] Content(int seed, int length)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Copies contents with one byte changed.
    /// </summary>
    /// <param name="content">The contents.</param>
    /// <param name="offset">The byte to change.</param>
    /// <returns>The changed copy.</returns>
    private static byte[] Changed(byte[] content, int offset)
    {
        var copy = content.ToArray();
        copy[offset] ^= 0xff;
        return copy;
    }

    /// <summary>
    /// Gets the path of a file in the temporary directory.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The path.</returns>
    private string PathOf(string name) => Path.Join(_temp.FullName, name);

    /// <summary>
    /// Writes a file in the temporary directory.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <param name="content">The contents.</param>
    /// <returns>The file's path.</returns>
    private string Write(string name, byte[] content)
    {
        File.WriteAllBytes(PathOf(name), content);
        return PathOf(name);
    }

    /// <summary>
    /// Creates the view model for a result.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The view model.</returns>
    private DuplicateResultsViewModel Create(DuplicateSearchResult result) =>
        new(result, "/data", Sizes, _shell, new DuplicateFinder(), "Finder", new DateTimeOffset(2026, 10, 17, 9, 30, 0, TimeSpan.FromHours(11)));

    /// <summary>
    /// Wraps a stream so that disposing the wrapper leaves the stream open for inspection.
    /// </summary>
    /// <param name="inner">The stream.</param>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => inner.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc/>
        public override long Length => inner.Length;

        /// <inheritdoc/>
        public override long Position { get => inner.Position; set => inner.Position = value; }

        /// <inheritdoc/>
        public override void Flush() => inner.Flush();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            inner.SetLength(value);
            inner.Position = Math.Min(inner.Position, value);
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
