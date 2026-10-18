using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.Settings;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Renders the settings view (a tab of the main window) headlessly. Set the <c>WHATDISK_SCREENSHOTS</c> environment variable to a directory to
/// keep the rendered frames for review.
/// </summary>
public sealed class SettingsViewTests : IDisposable
{
    /// <summary>The folder holding the settings file, deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-settings-window-");

    /// <inheritdoc/>
    public void Dispose() => _temp.Delete(recursive: true);

    /// <summary>
    /// The window lists the sections down the left and the chosen section's settings, each with its path in the file;
    /// the platform's own setting is marked with an asterisk, and a modified setting has a Reset button.
    /// </summary>
    /// <param name="variant">The theme variant to render.</param>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Window_ShowsTheSectionsAndTheirSettings(string variant)
    {
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = new AppSettings(TestPlatforms.MacOS, Path.Join(_temp.FullName, AppSettings.FileName));
        settings.Set(SettingsCatalog.Scanning.Threads, 8);
        var (window, page) = Show(settings);

        page.SelectedPage = page.Pages[1];
        Dispatcher.UIThread.RunJobs();

        var titles = window.GetVisualDescendants().OfType<ListBox>().Single().GetRealizedContainers().Select(static item => ((SettingsPageViewModel)item.DataContext!).Title);
        Assert.Equal(["General", "Scanning", "Duplicates", "Deleting"], titles);
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text ?? text.Inlines?.Text).ToList();
        Assert.Contains("scanning.threads", texts);
        Assert.Contains("scanning.macos.showHiddenVolumes", texts);
        Assert.Contains("Show hidden volumes *", texts);
        Assert.Contains(page.PlatformNote, texts);
        var resets = window.GetVisualDescendants().OfType<Button>().Where(static button => button.Content as string == "Reset").ToList();
        Assert.Single(resets, static button => button.IsEffectivelyVisible);
        Capture(window, $"settings-view-{variant.ToLowerInvariant()}.png");
        window.Close();
    }

    /// <summary>
    /// Changing a control saves the setting; the view has no Close button, as its tab closes like any other.
    /// </summary>
    [AvaloniaFact]
    public void Controls_SaveTheirSettings()
    {
        var path = Path.Join(_temp.FullName, AppSettings.FileName);
        var settings = new AppSettings(TestPlatforms.Windows, path);
        var (window, page) = Show(settings);
        page.SelectedPage = page.Pages.Single(static item => item.Key == "deleting");
        Dispatcher.UIThread.RunJobs();

        var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().Single();
        toggle.IsChecked = false;
        var number = window.GetVisualDescendants().OfType<NumericUpDown>().First();
        number.Value = 25;
        Dispatcher.UIThread.RunJobs();

        Assert.False(settings.Get(SettingsCatalog.Deleting.WarnWhenNotRecyclable));
        Assert.Equal(25, settings.Get(SettingsCatalog.Deleting.ConfirmAboveFiles));
        Assert.Contains("\"windows\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), static button => button.Content as string == "Close");
        window.Close();
    }

    /// <summary>
    /// Restore Defaults asks first, through the dialog service, and resets only when confirmed.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task RestoreDefaults_AsksFirst()
    {
        var settings = new AppSettings(TestPlatforms.MacOS, null);
        settings.Set(SettingsCatalog.Scanning.Threads, 8);
        var dialogs = new ScriptedDialogService { Confirm = false };
        using var page = new SettingsViewModel(settings, new RecordingShellService(), dialogs);

        await page.RestoreDefaultsCommand.ExecuteAsync(null);
        Assert.Equal(8, settings.Get(SettingsCatalog.Scanning.Threads));
        Assert.Equal("Restore every setting to its default?", Assert.Single(dialogs.Confirmations).Title);

        dialogs.Confirm = true;
        await page.RestoreDefaultsCommand.ExecuteAsync(null);
        Assert.Equal(0, settings.Get(SettingsCatalog.Scanning.Threads));
    }

    /// <summary>
    /// Shows the settings view in a window the size the settings tab has.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The window and its view model.</returns>
    private static (Window Window, SettingsViewModel Page) Show(AppSettings settings)
    {
        var page = new SettingsViewModel(settings, new RecordingShellService());
        var window = new Window { Width = 860, Height = 600, Content = new SettingsView { DataContext = page } };
        window.Closed += (_, _) => page.Dispose();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, page);
    }

    /// <summary>
    /// Renders the window, keeping the frame when screenshots are requested.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="fileName">The screenshot's file name.</param>
    private static void Capture(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("WHATDISK_SCREENSHOTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            frame.Save(Path.Join(directory, fileName), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
