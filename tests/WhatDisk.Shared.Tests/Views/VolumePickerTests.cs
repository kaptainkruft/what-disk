using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Tests the volume flyout in a real (headless) window.
/// </summary>
public sealed class VolumePickerTests
{
    /// <summary>
    /// Clicking a volume closes the flyout and scans that volume.
    /// </summary>
    /// <returns>A task that completes when the scan has started.</returns>
    [AvaloniaFact]
    public async Task ClickingAVolume_ScansIt()
    {
        var fs = new FakeFileSystem();
        fs.File("music/song.mp3", 500);
        var volumes = new FakeVolumeService();
        volumes.Selectable.Add(FakeVolumeService.CreateVolume("m", fs.PathOf("music")));
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = new MainWindowViewModel(new DiskScanner(fs, volumes, time), volumes, new FakeFolderPicker(), PlatformCapabilities.Generic, time);
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = viewModel };
        window.Show();
        await viewModel.LoadVolumesCommand.ExecuteAsync(null);

        var dropDown = window.GetVisualDescendants().OfType<DropDownButton>().Single();
        dropDown.Flyout!.ShowAt(dropDown);
        Dispatcher.UIThread.RunJobs();
        var picker = Assert.IsType<VolumePicker>(((Flyout)dropDown.Flyout).Content);
        var volumeButton = picker.GetVisualDescendants().OfType<Button>().First(static button => button.Classes.Contains("volume"));

        ControlAutomationPeer.CreatePeerForElement(volumeButton).GetProvider<IInvokeProvider>()!.Invoke();
        await Eventually.True(() => viewModel.Session is { IsRunning: false }, "the volume was scanned");

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(fs.PathOf("music"), viewModel.Session!.Roots[0].RootPath);
        Assert.False(dropDown.Flyout.IsOpen);
        window.Close();
    }
}
