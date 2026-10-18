using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests Tools › Find Duplicates on a real scan of a temporary directory.
/// </summary>
public sealed class FindDuplicatesTests : IDisposable
{
    /// <summary>The scanned directory, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-find-duplicates-");

    /// <summary>The scanned directory's path with symbolic links resolved, as the scanner reports it.</summary>
    private readonly string _base;

    /// <summary>The volumes: the whole file system.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>Records the results shown.</summary>
    private readonly ScriptedDialogService _dialogs = new();

    /// <summary>Reads the directories, and can hold the search in the middle.</summary>
    private readonly GatedEnumerator _enumerator = new();

    /// <summary>The view model.</summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="FindDuplicatesTests"/> class with two identical photos in
    /// different folders, a different file of the same length, and a file of another length.
    /// </summary>
    public FindDuplicatesTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", Path.GetPathRoot(_base)!);
        var photo = new byte[5000];
        new Random(1).NextBytes(photo);
        Write("photos/beach.jpg", photo);
        Write("backup/beach copy.jpg", photo);
        photo[2500] ^= 1;
        Write("photos/edited.jpg", photo);
        Write("notes.txt", new byte[10]);
        _viewModel = new MainWindowViewModel(
            new DiskScanner(_enumerator, _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            PlatformCapabilities.Generic,
            TimeProvider.System,
            shell: new RecordingShellService(),
            dialogs: _dialogs,
            enumerator: _enumerator)
        {
            PathText = _temp.FullName,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _enumerator.Release.Set();
        _viewModel.Dispose();
        _enumerator.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// Find Duplicates is disabled until a scan has finished; it then shows the identical files in the Duplicates
    /// window and summarises them in the status bar, whatever has focus.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicates_AfterAScan_ShowsTheResults()
    {
        Assert.False(_viewModel.FindDuplicatesInScanCommand.CanExecute(null));
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        _viewModel.IsTreeActive = false;
        Assert.True(_viewModel.FindDuplicatesInScanCommand.CanExecute(null));

        await _viewModel.FindDuplicatesInScanCommand.ExecuteAsync(null);

        var results = Assert.Single(_viewModel.DuplicateTabs());
        var set = Assert.Single(results.Result.Sets);
        Assert.Equal([PathOf("backup/beach copy.jpg"), PathOf("photos/beach.jpg")], set.Paths);
        Assert.Equal(3, results.Result.SizeMatches);
        Assert.Equal($"Duplicates in {_temp.FullName}", results.Title);
        Assert.Equal("Found 1 set of duplicates (2 files, 5.00 KB could be freed)", _viewModel.StatusText);
        Assert.False(_viewModel.IsOperationRunning);
        Assert.True(_viewModel.FindDuplicatesInScanCommand.CanExecute(null));
    }

    /// <summary>
    /// Finding duplicates in a selection compares only the selected items: one folder without the copy finds nothing,
    /// both folders together find the copy, and the results are titled with what was searched.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicatesInSelection_SearchesOnlyTheSelection()
    {
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        _viewModel.IsTreeActive = true;
        var root = (DirectoryNode)_viewModel.Tree.RootRows[0].Node;
        var photos = _viewModel.Tree.FindVisible(root.Children.Single(static child => child.Name == "photos"))!;
        var backup = _viewModel.Tree.FindVisible(root.Children.Single(static child => child.Name == "backup"))!;

        _viewModel.Selection.Clear();
        _viewModel.Selection.Select(_viewModel.Tree.IndexOf(photos));
        await _viewModel.FindDuplicatesInSelectionCommand.ExecuteAsync(null);

        Assert.Empty(_viewModel.DuplicateTabs()[0].Sets);
        Assert.Equal($"Duplicates in {PathOf("photos")}", _viewModel.DuplicateTabs()[0].Title);

        _viewModel.Selection.Select(_viewModel.Tree.IndexOf(backup));
        await _viewModel.FindDuplicatesInSelectionCommand.ExecuteAsync(null);

        Assert.Equal([PathOf("backup/beach copy.jpg"), PathOf("photos/beach.jpg")], Assert.Single(_viewModel.DuplicateTabs()[1].Sets).Paths);
        Assert.Equal(3, _viewModel.DuplicateTabs()[1].Result.SizeMatches);
    }

    /// <summary>
    /// Without duplicates the window still opens, saying so, and the status bar says how many files were compared.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task FindDuplicates_WithNone_SaysSo()
    {
        File.Delete(PathOf("backup/beach copy.jpg"));
        await _viewModel.StartScanCommand.ExecuteAsync(null);

        await _viewModel.FindDuplicatesInScanCommand.ExecuteAsync(null);

        Assert.Empty(Assert.Single(_viewModel.DuplicateTabs()).Rows);
        Assert.Equal("No duplicates among 3 files", _viewModel.StatusText);
    }

    /// <summary>
    /// While the search runs, nothing else can be chosen to scan: the volume picker, the path box, Browse and Scan are
    /// disabled (a new scan would replace the tree being searched), while Stop stays enabled. They are enabled again
    /// when the search ends.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task WhileSearching_ChoosingWhatToScanIsDisabled()
    {
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = _viewModel };
        window.Show();
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Control[] controls =
        [
            window.FindControl<DropDownButton>("VolumesButton")!,
            window.FindControl<TextBox>("PathBox")!,
            .. window.GetVisualDescendants().OfType<Button>().Where(static button => button.Content is "Browse…" or "Scan"),
        ];
        Assert.Equal(4, controls.Length);
        Assert.All(controls, static control => Assert.True(control.IsEffectivelyEnabled));

        _enumerator.IsGated = true;
        var search = _viewModel.FindDuplicatesInScanCommand.ExecuteAsync(null);
        Assert.True(await Task.Run(() => _enumerator.Entered.Wait(TimeSpan.FromSeconds(10))), "The search did not start.");
        Dispatcher.UIThread.RunJobs();

        Assert.True(_viewModel.IsFindingDuplicates);
        Assert.All(controls, static control => Assert.False(control.IsEffectivelyEnabled));
        Assert.False(_viewModel.StartScanCommand.CanExecute(null));
        Assert.False(_viewModel.BrowseCommand.CanExecute(null));
        Assert.True(_viewModel.StopScanCommand.CanExecute(null));

        _enumerator.Release.Set();
        await search;
        Dispatcher.UIThread.RunJobs();

        Assert.False(_viewModel.IsFindingDuplicates);
        Assert.All(controls, static control => Assert.True(control.IsEffectivelyEnabled));
        Assert.Single(_viewModel.DuplicateTabs());
        window.Close();
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

    /// <summary>
    /// The portable enumerator, whose identity lookups (made only by the duplicate search's sampling stage) can be
    /// held until the test releases them.
    /// </summary>
    private sealed class GatedEnumerator : IFileSystemEnumerator, IDisposable
    {
        /// <summary>Reads the directories.</summary>
        private readonly ManagedFileSystemEnumerator _inner = new();

        /// <summary>
        /// Gets or sets a value indicating whether identity lookups wait for <see cref="Release"/>.
        /// </summary>
        public bool IsGated { get; set; }

        /// <summary>
        /// Gets the event set when a lookup starts waiting.
        /// </summary>
        public ManualResetEventSlim Entered { get; } = new();

        /// <summary>
        /// Gets the event that lets waiting lookups continue.
        /// </summary>
        public ManualResetEventSlim Release { get; } = new();

        /// <inheritdoc/>
        public string Name => _inner.Name;

        /// <inheritdoc/>
        public FileIdentity GetIdentity(string path)
        {
            if (IsGated)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }

            return _inner.GetIdentity(path);
        }

        /// <inheritdoc/>
        public string GetCanonicalPath(string path) => _inner.GetCanonicalPath(path);

        /// <inheritdoc/>
        public FileSystemEntryInfo? GetEntry(string path) => ((IFileSystemEnumerator)_inner).GetEntry(path);

        /// <inheritdoc/>
        public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken) =>
            _inner.ReadDirectory(path, entries, cancellationToken);

        /// <inheritdoc/>
        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
