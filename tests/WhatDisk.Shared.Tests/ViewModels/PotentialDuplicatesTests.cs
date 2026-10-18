using Avalonia.Headless.XUnit;
using WhatDisk.Formatting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the Potential duplicate space pane on a real scan of a temporary folder: it follows the chart's scope, the
/// minimum length setting and changes to the tree.
/// </summary>
public sealed class PotentialDuplicatesTests : IDisposable
{
    /// <summary>The scanned folder, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-potential-");

    /// <summary>The scanned folder's path with symbolic links resolved.</summary>
    private readonly string _base;

    /// <summary>The volumes: the whole file system.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>The settings, kept in memory.</summary>
    private readonly AppSettings _settings = new(TestPlatforms.MacOS, null);

    /// <summary>The view model, once created.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="PotentialDuplicatesTests"/> class with two 1.5 MB files of the
    /// same length in different folders, a larger file of its own length and some small files of equal length.
    /// </summary>
    public PotentialDuplicatesTests()
    {
        _base = PathUtilities.ResolveLinks(_temp.FullName);
        _volumes.QueriedVolume = _volumes.Mount("root", Path.GetPathRoot(_base)!);
        Write("photos/holiday.mov", 1_500_000);
        Write("backup/holiday copy.mov", 1_500_000);
        Write("photos/other.mov", 3_000_000);
        Write("notes/a.txt", 100);
        Write("notes/b.txt", 100);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _viewModel?.Dispose();
        _temp.Delete(recursive: true);
    }

    /// <summary>
    /// After a scan the pane shows the space beyond one copy of the 1.5 MB file for the whole scan; selecting a folder
    /// with only one of the copies shows none; small files never count.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Pane_FollowsTheChartsScope()
    {
        var viewModel = await ScanAsync();
        var copy = (FileNode)Find(viewModel, "photos").Children.Single(static child => child.Name == "holiday.mov");

        Assert.Equal(new ByteSizeFormatter(ByteUnitConvention.PowersOf1000).Format(copy.Size), viewModel.PotentialDuplicates.AmountText);
        Assert.Equal($"Beyond one copy of each size, among files over 1 MiB in {viewModel.Tree.RootRows[0].Name}: 1 group, 2 files.", viewModel.PotentialDuplicates.DetailText);
        Assert.Equal((double)copy.Size / viewModel.Tree.RootRows[0].Node.Size, viewModel.PotentialDuplicates.Fraction, 6);

        viewModel.SelectedRow = viewModel.Tree.Rows.Single(static row => row.Name == "photos");
        await viewModel.WaitForChartAsync();

        Assert.Equal("None", viewModel.PotentialDuplicates.AmountText);
        Assert.Equal("No files over 1 MiB in photos share a size.", viewModel.PotentialDuplicates.DetailText);
    }

    /// <summary>
    /// Raising the minimum length re-estimates at once; deleting a copy updates the estimate.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task Pane_FollowsTheSettingAndChanges()
    {
        var viewModel = await ScanAsync();
        Assert.NotEqual("None", viewModel.PotentialDuplicates.AmountText);

        _settings.Set(SettingsCatalog.Duplicates.EstimateOverMiB, 2);
        await viewModel.WaitForChartAsync();
        Assert.Equal("No files over 2 MiB in " + viewModel.Tree.RootRows[0].Name + " share a size.", viewModel.PotentialDuplicates.DetailText);

        _settings.Reset(SettingsCatalog.Duplicates.EstimateOverMiB);
        await viewModel.WaitForChartAsync();
        Assert.NotEqual("None", viewModel.PotentialDuplicates.AmountText);

        viewModel.SelectNodeCommand.Execute(Find(viewModel, "backup").Children[0]);
        viewModel.IsTreeActive = true;
        await viewModel.DeletePermanentlyCommand.ExecuteAsync(null);
        viewModel.SelectedRow = null;
        await viewModel.WaitForChartAsync();

        Assert.False(File.Exists(Path.Join(_base, "backup", "holiday copy.mov")));
        Assert.Equal("None", viewModel.PotentialDuplicates.AmountText);
    }

    /// <summary>
    /// Finds a folder beneath the scanned folder.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="name">The folder's name.</param>
    /// <returns>The folder.</returns>
    private static DirectoryNode Find(MainWindowViewModel viewModel, string name) =>
        (DirectoryNode)((DirectoryNode)viewModel.Tree.RootRows[0].Node).Children.Single(child => child.Name == name);

    /// <summary>
    /// Scans the temporary folder and waits for the panes.
    /// </summary>
    /// <returns>The view model.</returns>
    private async Task<MainWindowViewModel> ScanAsync()
    {
        _viewModel = new MainWindowViewModel(
            new DiskScanner(new ManagedFileSystemEnumerator(), _volumes, TimeProvider.System),
            _volumes,
            new FakeFolderPicker(),
            TestPlatforms.MacOS,
            TimeProvider.System,
            shell: new RecordingShellService(),
            dialogs: new ScriptedDialogService(),
            settings: _settings)
        {
            PathText = _temp.FullName,
        };
        await _viewModel.StartScanCommand.ExecuteAsync(null);
        _viewModel.SelectedRow = null;
        await _viewModel.WaitForChartAsync();
        return _viewModel;
    }

    /// <summary>
    /// Writes a file of zeros, creating its folders.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="length">The length.</param>
    private void Write(string relative, int length)
    {
        var path = Path.Join(_base, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
