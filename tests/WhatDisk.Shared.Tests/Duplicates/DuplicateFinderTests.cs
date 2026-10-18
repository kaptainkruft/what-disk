using System.Collections.Concurrent;
using System.IO.Hashing;
using WhatDisk.Duplicates;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Duplicates;

/// <summary>
/// Tests the duplicate search against real files in a temporary directory: grouping by length, comparing samples,
/// hashing, and what is left out or reported as a failure.
/// </summary>
public sealed class DuplicateFinderTests : IDisposable
{
    /// <summary>The directory holding the files, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-duplicates-");

    /// <summary>The directory's path with symbolic links resolved.</summary>
    private readonly string _base;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateFinderTests"/> class.
    /// </summary>
    public DuplicateFinderTests() => _base = PathUtilities.ResolveLinks(_temp.FullName);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var file in _temp.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Sixty-four samples are spread evenly from the start of the file to the last whole sample at its end; a file no
    /// longer than a sample is sampled whole.
    /// </summary>
    /// <param name="length">The file's length.</param>
    [Theory]
    [InlineData(1L)]
    [InlineData(4096L)]
    [InlineData(1_048_577L)]
    [InlineData(10_000_000L)]
    [InlineData(5_000_000_000_000L)]
    public void SampleOffsets_SpreadFromStartToEnd(long length)
    {
        var offsets = DuplicateFinder.SampleOffsets(length, 64, 4096);

        var last = Math.Max(0, length - 4096);
        Assert.Equal(64, offsets.Length);
        Assert.Equal(0, offsets[0]);
        Assert.Equal(last, offsets[^1]);
        Assert.Equal(last / 3, offsets[21]);
        Assert.All(offsets.Zip(offsets.Skip(1)), static pair => Assert.InRange(pair.Second - pair.First, 0, long.MaxValue));
    }

    /// <summary>
    /// Identical files up to 1 MiB in different folders form a set confirmed by the hash of their contents; a file of
    /// the same length with other contents is not in it.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task SmallIdenticalFiles_AreConfirmedByHash()
    {
        var content = Content(1, 10_000);
        Write("a/report.bin", content);
        Write("b/copy of report.bin", content);
        Write("c/other.bin", Content(2, 10_000));

        var result = await FindAsync();

        var set = Assert.Single(result.Sets);
        Assert.Equal(1, set.Id);
        Assert.Equal(10_000, set.Length);
        Assert.Equal(DuplicateComparison.Hash, set.Comparison);
        Assert.Equal([PathOf("a/report.bin"), PathOf("b/copy of report.bin")], set.Paths);
        Assert.Equal(Convert.ToHexStringLower(XxHash128.Hash(content)), set.Hash);
        Assert.Equal(10_000, result.WastedBytes);
        Assert.Equal((3, 3, 3, 0), (result.FilesConsidered, result.SizeMatches, result.HashedFiles, result.SampledFiles));
        Assert.Empty(result.Failures);
    }

    /// <summary>
    /// A file up to 1 MiB that differs anywhere, even between where samples would be taken, is ruled out by its hash.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task SmallFiles_DifferingAnywhere_AreRuledOut()
    {
        var content = Content(3, 1_048_576);
        Write("original.bin", content);
        Write("copy.bin", content);
        Write("differs.bin", Changed(content, 10_000));

        var result = await FindAsync();

        var set = Assert.Single(result.Sets);
        Assert.Equal(DuplicateComparison.Hash, set.Comparison);
        Assert.Equal([PathOf("copy.bin"), PathOf("original.bin")], set.Paths);
    }

    /// <summary>
    /// Files over 1 MiB are only sampled: identical files form a set matched by samples, without a hash; a file that
    /// differs inside a sample is ruled out, but one that differs only between samples is not (until the set is
    /// verified by hash).
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task LargeFiles_AreComparedBySamplesOnly()
    {
        var content = Content(4, 2_000_000);
        Write("original.bin", content);
        Write("copy.bin", content);
        Write("differs-in-a-sample.bin", Changed(content, 100));
        Write("differs-between-samples.bin", Changed(content, 10_000));

        var result = await FindAsync();

        var set = Assert.Single(result.Sets);
        Assert.Equal(DuplicateComparison.Samples, set.Comparison);
        Assert.Null(set.Hash);
        Assert.Equal([PathOf("copy.bin"), PathOf("differs-between-samples.bin"), PathOf("original.bin")], set.Paths);
        Assert.Equal((4, 0, 4), (result.SizeMatches, result.HashedFiles, result.SampledFiles));
        Assert.Equal(1, result.SampledSets);
    }

    /// <summary>
    /// Files of exactly 1 MiB are hashed; one byte more and they are sampled.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task OneMebibyte_IsTheLimitForHashing()
    {
        var limit = Content(5, 1024 * 1024);
        var over = Content(6, (1024 * 1024) + 1);
        Write("limit-a", limit);
        Write("limit-b", limit);
        Write("over-a", over);
        Write("over-b", over);

        var result = await FindAsync();

        Assert.Equal(
            [((1024L * 1024) + 1, DuplicateComparison.Samples), (1024L * 1024, DuplicateComparison.Hash)],
            result.Sets.Select(static set => (set.Length, set.Comparison)));
    }

    /// <summary>
    /// Verifying a sampled set hashes its files: identical files are confirmed with their hash, and a file that
    /// differed only between samples drops out.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Verify_ConfirmsIdenticalFilesAndDropsOthers()
    {
        var content = Content(7, 2_000_000);
        Write("a.bin", content);
        Write("b.bin", content);
        Write("c.bin", Changed(content, 10_000));
        var set = new DuplicateSet(4, 2_000_000, null, [PathOf("a.bin"), PathOf("b.bin"), PathOf("c.bin")], DuplicateComparison.Samples);
        var progress = new RecordingBytes();

        var verification = await new DuplicateFinder().VerifyAsync(set, progress, TestContext.Current.CancellationToken);

        var group = Assert.Single(verification.Groups);
        Assert.Equal([PathOf("a.bin"), PathOf("b.bin")], group.Paths);
        Assert.Equal(Convert.ToHexStringLower(XxHash128.Hash(content)), group.Hash);
        Assert.Empty(verification.Failures);
        Assert.Equal(6_000_000, progress.Reports[^1]);
    }

    /// <summary>
    /// Verifying a set that holds two different contents gives two groups, largest first; a missing file is reported.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Verify_SplitsMixedSetsAndReportsMissingFiles()
    {
        var first = Content(8, 1_500_000);
        var second = Changed(first, 10_000);
        Write("x1", first);
        Write("x2", first);
        Write("y1", second);
        Write("y2", second);
        Write("y3", second);
        var set = new DuplicateSet(1, 1_500_000, null, [PathOf("gone"), PathOf("x1"), PathOf("x2"), PathOf("y1"), PathOf("y2"), PathOf("y3")], DuplicateComparison.Samples);

        var verification = await new DuplicateFinder().VerifyAsync(set, null, TestContext.Current.CancellationToken);

        Assert.Equal([[PathOf("y1"), PathOf("y2"), PathOf("y3")], [PathOf("x1"), PathOf("x2")]], verification.Groups.Select(static group => group.Paths));
        Assert.Equal([new DuplicateFailure(PathOf("gone"), "No longer exists")], verification.Failures);
    }

    /// <summary>
    /// Files of the same length with two different contents form two sets, and sets are numbered by the space they
    /// waste, largest first.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Sets_AreSplitByContentAndNumberedByWaste()
    {
        var small = Content(4, 1000);
        var other = Content(5, 1000);
        var large = Content(6, 4000);
        Write("small1", small);
        Write("small2", small);
        Write("small3", small);
        Write("other1", other);
        Write("other2", other);
        Write("large1", large);
        Write("large2", large);

        var result = await FindAsync();

        Assert.Equal([1, 2, 3], result.Sets.Select(static set => set.Id));
        Assert.Equal([4000L, 2000L, 1000L], result.Sets.Select(static set => set.WastedBytes));
        Assert.Equal([PathOf("other1"), PathOf("other2")], result.Sets[2].Paths);
        Assert.Equal(7, result.DuplicateFiles);
    }

    /// <summary>
    /// Files shorter than a sample are compared whole; empty files are left out.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task TinyFiles_AreCompared_EmptyFilesAreLeftOut()
    {
        Write("one-a", [7]);
        Write("one-b", [7]);
        Write("one-c", [8]);
        Write("short-a", Content(7, 63));
        Write("short-b", Changed(Content(7, 63), 30));
        Write("empty-a", []);
        Write("empty-b", []);

        var result = await FindAsync();

        var set = Assert.Single(result.Sets);
        Assert.Equal([PathOf("one-a"), PathOf("one-b")], set.Paths);
        Assert.Equal(2, result.EmptyFiles);
    }

    /// <summary>
    /// Hard links to the same file (the same identity) are compared once, so a file and its link are not reported as
    /// duplicates of each other; a real copy still is.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task HardLinks_AreComparedOnce()
    {
        var content = Content(8, 2000);
        Write("file", content);
        Write("link", content);
        var identities = new Dictionary<string, FileIdentity>
        {
            [PathOf("file")] = new(1, 42),
            [PathOf("link")] = new(1, 42),
        };
        var finder = new DuplicateFinder(path => identities.GetValueOrDefault(path));

        var linksOnly = await finder.FindAsync(Candidates("file", "link"), null, TestContext.Current.CancellationToken);
        Assert.Empty(linksOnly.Sets);
        Assert.Equal(1, linksOnly.HardLinks);

        Write("copy", content);
        var withCopy = await finder.FindAsync(Candidates("file", "link", "copy"), null, TestContext.Current.CancellationToken);
        var set = Assert.Single(withCopy.Sets);
        Assert.Equal([PathOf("copy"), PathOf("file")], set.Paths);
    }

    /// <summary>
    /// A file listed twice is compared once.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task TheSamePathTwice_IsComparedOnce()
    {
        Write("file", Content(9, 500));

        var result = await new DuplicateFinder().FindAsync(Candidates("file", "file"), null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Sets);
    }

    /// <summary>
    /// A file whose length changed since the scan, or that no longer exists, is reported and left out; the others
    /// are still compared.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ChangedAndMissingFiles_AreReported()
    {
        var content = Content(10, 3000);
        Write("a", content);
        Write("b", content);
        Write("grown", content);
        File.AppendAllText(PathOf("grown"), "more");
        DuplicateCandidate[] candidates = [.. Candidates("a", "b"), new(PathOf("grown"), 3000), new(PathOf("gone"), 3000)];

        var result = await new DuplicateFinder().FindAsync(candidates, null, TestContext.Current.CancellationToken);

        Assert.Equal([PathOf("a"), PathOf("b")], Assert.Single(result.Sets).Paths);
        Assert.Equal(
            [new DuplicateFailure(PathOf("gone"), "No longer exists"), new DuplicateFailure(PathOf("grown"), "Changed since the scan")],
            result.Failures);
    }

    /// <summary>
    /// A file that cannot be read is reported and left out.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task UnreadableFiles_AreReported()
    {
        Assert.SkipWhen(Environment.UserName == "root", "The superuser can read any file.");
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permissions only; Windows is covered by the in-use test.");
            return;
        }

        var content = Content(11, 3000);
        Write("a", content);
        Write("b", content);
        Write("locked", content);
        File.SetUnixFileMode(PathOf("locked"), UnixFileMode.None);

        var result = await FindAsync();

        Assert.Equal([PathOf("a"), PathOf("b")], Assert.Single(result.Sets).Paths);
        Assert.Equal([new DuplicateFailure(PathOf("locked"), "Access denied")], result.Failures);
    }

    /// <summary>
    /// A file another program has open without sharing it is reported and left out (Windows only).
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task FilesInUse_AreReported()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows enforces exclusive opens.");
        var content = Content(12, 3000);
        Write("a", content);
        Write("b", content);
        Write("in-use", content);
        using var exclusive = new FileStream(PathOf("in-use"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await FindAsync();

        Assert.Equal([PathOf("a"), PathOf("b")], Assert.Single(result.Sets).Paths);
        Assert.Equal(PathOf("in-use"), Assert.Single(result.Failures).Path);
    }

    /// <summary>
    /// In a scanned tree, only regular files are compared: cloud-only files, extra hard links the scan already found
    /// and empty files are counted and left out, and folders are searched all the way down.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ScannedTrees_LeaveOutCloudFilesHardLinksAndEmptyFiles()
    {
        var content = Content(13, 1500);
        foreach (var name in new[] { "a", "sub/b", "cloud", "hardlink", "link" })
        {
            Write(name, content);
        }

        Write("empty", []);
        var root = TestTree.Root(_base);
        var sub = TestTree.Dir(root, "sub");
        TestTree.Publish(sub, TestTree.File(sub, "b", 1500));
        TestTree.Publish(
            root,
            TestTree.File(root, "a", 1500),
            sub,
            new FileNode("cloud", root, NodeKind.File, 0, 1500, DateTime.MinValue, EntryAttributes.Offline, false),
            new FileNode("hardlink", root, NodeKind.File, 0, 1500, DateTime.MinValue, EntryAttributes.None, isHardLinkDuplicate: true),
            new FileNode("link", root, NodeKind.SymbolicLink, 1500, 1500, DateTime.MinValue, EntryAttributes.None, false),
            TestTree.File(root, "empty", 0));

        var result = await new DuplicateFinder().FindAsync([root], null, TestContext.Current.CancellationToken);

        Assert.Equal([PathOf("a"), PathOf("sub/b")], Assert.Single(result.Sets).Paths);
        Assert.Equal((2, 1, 1, 1), (result.FilesConsidered, result.OfflineFiles, result.HardLinks, result.EmptyFiles));
    }

    /// <summary>
    /// Progress is reported for sampling the larger files and then hashing the smaller ones, ending with every file
    /// and byte hashed.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Progress_IsReportedForEachStage()
    {
        var large = Content(14, 1_500_000);
        var small = Content(15, 300_000);
        Write("large-a", large);
        Write("large-b", large);
        Write("small-a", small);
        Write("small-b", small);
        Write("small-c", Content(16, 300_000));
        var progress = new RecordingProgress();

        await new DuplicateFinder().FindAsync(Candidates("large-a", "large-b", "small-a", "small-b", "small-c"), progress, TestContext.Current.CancellationToken);

        Assert.Equal([DuplicateSearchStage.Sampling, DuplicateSearchStage.Hashing], progress.Reports.Select(static report => report.Stage).Distinct());
        var last = progress.Reports[^1];
        Assert.Equal(new DuplicateProgress(DuplicateSearchStage.Hashing, 3, 3, 900_000, 900_000), last);
    }

    /// <summary>
    /// A cancelled search stops with <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task Cancelling_StopsTheSearch()
    {
        Write("a", Content(16, 100));
        Write("b", Content(16, 100));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DuplicateFinder().FindAsync(Candidates("a", "b"), null, cancellation.Token));
    }

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
    /// Searches every file in the temporary directory.
    /// </summary>
    /// <returns>The result.</returns>
    private Task<DuplicateSearchResult> FindAsync() =>
        new DuplicateFinder().FindAsync(
            Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories).Select(static path => new DuplicateCandidate(path, new FileInfo(path).Length)).ToArray(),
            null,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Describes files in the temporary directory with their current lengths.
    /// </summary>
    /// <param name="relatives">The files' relative paths.</param>
    /// <returns>The candidates.</returns>
    private DuplicateCandidate[] Candidates(params string[] relatives) =>
        [.. relatives.Select(relative => new DuplicateCandidate(PathOf(relative), new FileInfo(PathOf(relative)).Length))];

    /// <summary>
    /// Gets the absolute path of a file in the temporary directory.
    /// </summary>
    /// <param name="relative">The relative path, with forward slashes.</param>
    /// <returns>The absolute path.</returns>
    private string PathOf(string relative) => Path.Join(_base, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Writes a file, creating its folders.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="content">The contents.</param>
    private void Write(string relative, byte[] content)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// Records the bytes hashed, as reported, on the reporting thread.
    /// </summary>
    private sealed class RecordingBytes : IProgress<long>
    {
        /// <summary>The reports.</summary>
        private readonly ConcurrentQueue<long> _reports = new();

        /// <summary>
        /// Gets the reports, in order.
        /// </summary>
        public IReadOnlyList<long> Reports => [.. _reports];

        /// <inheritdoc/>
        public void Report(long value) => _reports.Enqueue(value);
    }

    /// <summary>
    /// Records progress reports as they are made, on the reporting thread.
    /// </summary>
    private sealed class RecordingProgress : IProgress<DuplicateProgress>
    {
        /// <summary>The reports.</summary>
        private readonly ConcurrentQueue<DuplicateProgress> _reports = new();

        /// <summary>
        /// Gets the reports, in order.
        /// </summary>
        public IReadOnlyList<DuplicateProgress> Reports => [.. _reports];

        /// <inheritdoc/>
        public void Report(DuplicateProgress value) => _reports.Enqueue(value);
    }
}
