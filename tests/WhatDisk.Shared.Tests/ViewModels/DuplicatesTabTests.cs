using Avalonia.Headless.XUnit;
using WhatDisk.Duplicates;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the Duplicates tab, which is always there, on real scans of a temporary directory: empty before a scan, then
/// the files over 1 MiB that share a size, compared by a sampled scan or a full hash scan.
/// </summary>
public sealed class DuplicatesTabTests : IDisposable
{
    /// <summary>The length of the large files: over the 1 MiB small file limit and hashing limit.</summary>
    private const int LargeLength = (2 * 1024 * 1024) + 1;

    /// <summary>The scanned directory, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-duplicates-tab-");

    /// <summary>The scanned directory's path with symbolic links resolved, as the scanner reports it.</summary>
    private readonly string _base;

    /// <summary>The volumes: the whole file system.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>Answers the questions.</summary>
    private readonly ScriptedDialogService _dialogs = new();

    /// <summary>The view model.</summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatesTabTests"/> class with three large files of one length
    /// (two identical), two identical small files and another small file.
    /// </summary>
    public DuplicatesTabTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", Path.GetPathRoot(_base)!);
        Write("videos/holiday.mov", Content(1, LargeLength));
        Write("backup/holiday copy.mov", Content(1, LargeLength));
        Write("videos/other.mov", Content(2, LargeLength));
        Write("notes/a.txt", Content(3, 5000));
        Write("notes/a copy.txt", Content(3, 5000));
        Write("readme.txt", Content(4, 10));
        _viewModel = new MainWindowViewModel(
            new DiskScanner(new ManagedFileSystemEnumerator(), _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            PlatformCapabilities.Generic,
            TimeProvider.System,
            shell: new RecordingShellService(),
            dialogs: _dialogs)
        {
            PathText = _temp.FullName,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _viewModel.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Before a scan, the Duplicates tab is there, cannot be closed, lists nothing and says how it will fill, and none
    /// of its commands can run.
    /// </summary>
    [AvaloniaFact]
    public void BeforeAScan_TheTabIsEmptyWithEveryCommandOff()
    {
        var tab = _viewModel.DuplicatesTab;

        Assert.Equal(["Scan", "Duplicates"], _viewModel.Tabs.Select(static item => item.Header));
        Assert.False(tab.CanClose);
        Assert.True(tab.CoversWholeScan);
        Assert.Empty(tab.Rows);
        Assert.Null(tab.ToolTip);
        Assert.Equal("No scan yet.", tab.Summary);
        Assert.Equal("Scan a volume or a folder: files over 1 MiB that share a size are listed here as the scan finds them.", tab.EmptyHint);
        Assert.Equal("Include Files of 1 MiB or Less…", tab.SmallFilesText);
        Assert.False(tab.SampledScanCommand.CanExecute(null));
        Assert.False(tab.FullHashScanCommand.CanExecute(null));
        Assert.False(tab.CopyCsvCommand.CanExecute(null));
        Assert.False(tab.StopCommand.CanExecute(null));
        Assert.False(tab.ToggleSmallFilesCommand.CanExecute(null));
    }

    /// <summary>
    /// After a scan, the tab lists the files over 1 MiB that share a size, as one set compared only by size, says
    /// that they have not been compared and what was left out, and its commands can run.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task AfterAScan_TheTabListsTheFilesThatShareASize()
    {
        var tab = await ScanAsync();

        var set = Assert.Single(tab.Sets);
        Assert.Equal(DuplicateComparison.Size, set.Comparison);
        Assert.Equal([PathOf("backup/holiday copy.mov"), PathOf("videos/holiday.mov"), PathOf("videos/other.mov")], set.Paths);
        Assert.All(tab.Rows, static row => Assert.Equal(("Size", "—", true), (row.ComparisonText, row.HashText, row.IsUnconfirmed)));
        Assert.True(tab.IsLive);
        Assert.Equal($"Duplicates in {_temp.FullName}", tab.Title);
        Assert.StartsWith("1 set of files over 1 MiB that share a size: 3 files. If they are duplicates, keeping one file of each set would free ", tab.Summary, StringComparison.Ordinal);
        Assert.Equal("Only sizes were compared. Run a Sampled Scan or a Full Hash Scan before deleting anything.", tab.VerificationSummary);
        Assert.Equal("Of 3 files over 1 MiB, 3 share a size with another; their contents have not been compared. Left out: 3 files of 1 MiB or less.", tab.Details);
        Assert.True(tab.SampledScanCommand.CanExecute(null));
        Assert.True(tab.FullHashScanCommand.CanExecute(null));
        Assert.True(tab.CopyCsvCommand.CanExecute(null));
        Assert.True(tab.ToggleSmallFilesCommand.CanExecute(null));
        Assert.EndsWith($"{PathOf("videos/other.mov")},{LargeLength},,Size\r\n", tab.Csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// While the tab is hidden, the files are listed only once it is shown.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task WhileHidden_TheTabListsTheFilesWhenShown()
    {
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        await _viewModel.WaitForDuplicatesAsync();
        Assert.Empty(_viewModel.DuplicatesTab.Rows);

        _viewModel.SelectedTab = _viewModel.DuplicatesTab;
        await _viewModel.WaitForDuplicatesAsync();

        Assert.Equal(3, _viewModel.DuplicatesTab.Rows.Count);
    }

    /// <summary>
    /// A sampled scan compares the files' contents (the larger ones by samples), reporting in the status bar and
    /// without opening another tab; the tab then keeps the result; a full hash scan confirms the sampled set.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task SampledScan_ThenFullHashScan_ConfirmTheCopy()
    {
        var tab = await ScanAsync();

        await tab.SampledScanCommand.ExecuteAsync(null);

        var sampled = Assert.Single(tab.Sets);
        Assert.Equal(DuplicateComparison.Samples, sampled.Comparison);
        Assert.Equal([PathOf("backup/holiday copy.mov"), PathOf("videos/holiday.mov")], sampled.Paths);
        Assert.False(tab.IsLive);
        Assert.False(tab.IsSearching);
        Assert.StartsWith("Found 1 set of duplicates (2 files", _viewModel.StatusText, StringComparison.Ordinal);
        Assert.Empty(_viewModel.DuplicateTabs());

        await tab.FullHashScanCommand.ExecuteAsync(null);

        Assert.Equal(DuplicateComparison.Hash, Assert.Single(tab.Sets).Comparison);
        Assert.Equal("Every set is confirmed by hash.", tab.VerificationSummary);
        Assert.False(tab.FullHashScanCommand.CanExecute(null));
    }

    /// <summary>
    /// A full hash scan straight from the files that share a size keeps the identical files and drops the one that
    /// differs; a new scan lists the files that share a size again.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FullHashScan_FromSizes_DropsTheFileThatDiffers()
    {
        var tab = await ScanAsync();

        await tab.FullHashScanCommand.ExecuteAsync(null);

        var set = Assert.Single(tab.Sets);
        Assert.Equal(DuplicateComparison.Hash, set.Comparison);
        Assert.Equal([PathOf("backup/holiday copy.mov"), PathOf("videos/holiday.mov")], set.Paths);
        Assert.Equal("Hashed set 1: it lost 1 file that differed.", tab.Message);
        Assert.False(tab.IsLive);

        await ScanAsync();

        Assert.True(tab.IsLive);
        Assert.Equal(DuplicateComparison.Size, Assert.Single(tab.Sets).Comparison);
        Assert.Equal(3, tab.Rows.Count);
    }

    /// <summary>
    /// Including the smaller files asks first; declining keeps the list, accepting adds the small files that share a
    /// size, and leaving them out again asks nothing.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task IncludingSmallerFiles_AsksFirst()
    {
        var tab = await ScanAsync();
        _dialogs.Confirm = false;

        await tab.ToggleSmallFilesCommand.ExecuteAsync(null);

        Assert.Equal("Include files of 1 MiB or less?", Assert.Single(_dialogs.Confirmations).Title);
        Assert.False(tab.IncludesSmallFiles);
        Assert.Single(tab.Sets);

        _dialogs.Confirm = true;
        await tab.ToggleSmallFilesCommand.ExecuteAsync(null);
        await _viewModel.WaitForDuplicatesAsync();

        Assert.True(tab.IncludesSmallFiles);
        Assert.Equal("Leave Out Files of 1 MiB or Less", tab.SmallFilesText);
        Assert.Equal([LargeLength, 5000], tab.Sets.Select(static set => set.Length));
        Assert.Equal([PathOf("notes/a copy.txt"), PathOf("notes/a.txt")], tab.Sets[1].Paths);

        await tab.ToggleSmallFilesCommand.ExecuteAsync(null);
        await _viewModel.WaitForDuplicatesAsync();

        Assert.Equal(2, _dialogs.Confirmations.Count);
        Assert.False(tab.IncludesSmallFiles);
        Assert.Single(tab.Sets);
    }

    /// <summary>
    /// A search of the selection still compares every non-empty file, whatever the small file limit, and shows its
    /// result in a tab of its own.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicatesInSelection_ComparesEveryFile()
    {
        await ScanAsync();
        _viewModel.SelectedTab = _viewModel.ScanTab;
        _viewModel.IsTreeActive = true;
        var root = (DirectoryNode)_viewModel.Tree.RootRows[0].Node;
        _viewModel.Selection.Clear();
        _viewModel.Selection.Select(_viewModel.Tree.IndexOf(_viewModel.Tree.FindVisible(root.Children.Single(static child => child.Name == "notes"))!));

        await _viewModel.FindDuplicatesInSelectionCommand.ExecuteAsync(null);

        var results = Assert.Single(_viewModel.DuplicateTabs());
        Assert.True(results.CanClose);
        Assert.Same(results, _viewModel.SelectedTab);
        Assert.Equal(DuplicateComparison.Hash, Assert.Single(results.Sets).Comparison);
        Assert.Equal(["Scan", "Duplicates", $"Duplicates in notes"], _viewModel.Tabs.Select(static item => item.Header));
    }

    /// <summary>
    /// Shows the Duplicates tab and scans the temporary directory, waiting until the tab lists the files.
    /// </summary>
    /// <returns>The Duplicates tab.</returns>
    private async Task<DuplicateResultsViewModel> ScanAsync()
    {
        _viewModel.SelectedTab = _viewModel.DuplicatesTab;
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        await _viewModel.WaitForDuplicatesAsync();
        return _viewModel.DuplicatesTab;
    }

    /// <summary>
    /// Makes contents that differ from those of another seed.
    /// </summary>
    /// <param name="seed">The seed.</param>
    /// <param name="length">The length.</param>
    /// <returns>The contents.</returns>
    private static byte[] Content(int seed, int length)
    {
        var content = new byte[length];
        new Random(seed).NextBytes(content);
        return content;
    }

    /// <summary>
    /// Gets the absolute path of an item in the scanned directory.
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
}
