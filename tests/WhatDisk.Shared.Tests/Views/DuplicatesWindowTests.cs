using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk.Tests.Views;

/// <summary>
/// Renders the Duplicates window headlessly. Set the <c>WHATDISK_SCREENSHOTS</c> environment variable to a directory
/// to keep the rendered frames for review.
/// </summary>
public sealed class DuplicatesWindowTests
{
    /// <summary>Records the copies.</summary>
    private readonly RecordingShellService _shell = new();

    /// <summary>
    /// The window lists every file with its set, path, size and hash under a header, and Copy CSV copies the CSV.
    /// </summary>
    /// <param name="variant">The theme variant to render.</param>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Window_ListsTheFiles_AndCopiesTheCsv(string variant)
    {
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var results = Create(
            new DuplicateSet(1, 734_003_200, null, ["/Users/me/Desktop/Holiday copy.mov", "/Users/me/Movies/Holiday.mov", "/Volumes/Backup/2024/Holiday.mov"], DuplicateComparison.Samples),
            new DuplicateSet(2, 2_400_000, "3c59dc048e8850243be8079a5c74d079", ["/Users/me/Downloads/IMG_0042.HEIC", "/Users/me/Pictures/IMG_0042.HEIC"], DuplicateComparison.Hash),
            new DuplicateSet(3, 18_000, "b6d767d2f8ed5d21a44b0e5886680cb9", ["/Users/me/Documents/Invoices, 2025/March.pdf", "/Users/me/Downloads/March.pdf"], DuplicateComparison.Hash));
        var window = new DuplicatesWindow { DataContext = results };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Duplicates in /Users/me", window.Title);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Equal(7, list.ItemCount);
        var titles = window.GetVisualDescendants().OfType<TextBlock>().Where(static text => text.Classes.Contains("column-title")).Select(static text => text.Text);
        Assert.Equal(["Set", "Full Path", "Size", "Hash (XXH128)", "Comparison"], titles);
        Assert.Equal(["1", "/Users/me/Desktop/Holiday copy.mov", "734 MB", "—", "Sampled"], RowTexts(list, 0));
        Assert.Equal(["2", "/Users/me/Downloads/IMG_0042.HEIC", "2.40 MB", "3c59dc048e8850243be8079a5c74d079", "Hash"], RowTexts(list, 3));
        Assert.True(window.GetVisualDescendants().OfType<Button>().Single(static button => button.Content as string == "Verify All by Hash").IsEffectivelyEnabled);
        Assert.False(window.GetVisualDescendants().OfType<Button>().Single(static button => button.Content as string == "Stop").IsVisible);
        Capture(window, $"duplicates-window-{variant.ToLowerInvariant()}.png");

        var copy = window.GetVisualDescendants().OfType<Button>().Single(static button => button.Content as string == "Copy CSV");
        Assert.True(copy.IsEffectivelyEnabled);
        copy.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([$"copy text: {results.Csv}"], _shell.Calls);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), static text => text.Text == "Copied 7 rows to the clipboard.");
        window.Close();
    }

    /// <summary>
    /// Double-clicking a row shows its file in the file manager.
    /// </summary>
    [AvaloniaFact]
    public void DoubleClick_RevealsTheFile()
    {
        var folder = Directory.CreateTempSubdirectory("whatdisk-reveal-");
        try
        {
            var first = Path.Join(folder.FullName, "a.bin");
            var second = Path.Join(folder.FullName, "b.bin");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [1]);
            var window = new DuplicatesWindow { DataContext = Create(new DuplicateSet(1, 1, "00000000000000000000000000000001", [first, second], DuplicateComparison.Hash)) };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            var point = list.ContainerFromIndex(1)!.TranslatePoint(new Point(200, 13), window)!.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(_shell.Calls);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([$"reveal: {second}"], _shell.Calls);
            window.Close();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Right-clicking a row selects it and opens its context menu, whose one item hashes the row's set; the set is then
    /// marked as confirmed by hash, and the item is disabled for it.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [AvaloniaFact]
    public async Task ContextMenu_VerifiesTheRowsSet()
    {
        var folder = Directory.CreateTempSubdirectory("whatdisk-verify-set-");
        try
        {
            var content = new byte[1_200_000];
            new Random(1).NextBytes(content);
            var first = Path.Join(folder.FullName, "a.bin");
            var second = Path.Join(folder.FullName, "b.bin");
            File.WriteAllBytes(first, content);
            File.WriteAllBytes(second, content);
            var results = Create(new DuplicateSet(1, 1_200_000, null, [first, second], DuplicateComparison.Samples));
            var window = new DuplicatesWindow { DataContext = results };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            var point = list.ContainerFromIndex(1)!.TranslatePoint(new Point(200, 13), window)!.Value;

            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
            Assert.True(menu.IsOpen);
            var item = Assert.Single(menu.Items.OfType<MenuItem>());
            Assert.Equal("Verify Set by Hash", item.Header);
            Assert.Same(results.Rows[1], item.CommandParameter);
            Assert.True(item.IsEffectivelyEnabled);

            await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(item.CommandParameter);
            menu.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Hash", "Hash"], results.Rows.Select(static row => row.ComparisonText));
            Assert.Equal("Hashed set 1: every file is identical.", results.Message);
            Assert.False(item.Command!.CanExecute(item.CommandParameter));
            window.Close();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Without duplicates the window says so, and Copy and Export are disabled.
    /// </summary>
    [AvaloniaFact]
    public void Window_WithNoDuplicates_SaysSo()
    {
        var window = new DuplicatesWindow { DataContext = Create() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var hint = window.GetVisualDescendants().OfType<TextBlock>().Single(static text => text.Classes.Contains("empty-hint"));
        Assert.True(hint.IsEffectivelyVisible);
        Assert.All(
            window.GetVisualDescendants().OfType<Button>().Where(static button => button.Content is "Copy CSV" or "Export CSV…"),
            static button => Assert.False(button.IsEffectivelyEnabled));
        window.Close();
    }

    /// <summary>
    /// The dialog service shows the results in a window the main window owns, without waiting for it to close.
    /// </summary>
    [AvaloniaFact]
    public void DialogService_ShowsTheWindowOwnedByTheMainWindow()
    {
        var main = new Window();
        main.Show();
        var results = Create(new DuplicateSet(1, 10, "00000000000000000000000000000001", ["/a", "/b"], DuplicateComparison.Hash));

        new WindowDialogService(() => main).ShowDuplicates(results);

        var shown = Assert.IsType<DuplicatesWindow>(Assert.Single(main.OwnedWindows));
        Assert.Same(results, shown.DataContext);
        Assert.True(shown.IsVisible);
        shown.Close();
        main.Close();
    }

    /// <summary>
    /// Creates the view model for some sets.
    /// </summary>
    /// <param name="sets">The sets.</param>
    /// <returns>The view model.</returns>
    private DuplicateResultsViewModel Create(params DuplicateSet[] sets) => new(
        new DuplicateSearchResult(sets, 12_345, 40, 31, 9, 2, 0, 1, [new DuplicateFailure("/Users/me/Library/Mail/locked.emlx", "Access denied")]),
        "/Users/me",
        new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture),
        _shell,
        new DuplicateFinder(),
        "Finder",
        DateTimeOffset.Now);

    /// <summary>
    /// Gets the texts of a row's cells.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="index">The row's index.</param>
    /// <returns>The texts, left to right.</returns>
    private static List<string?> RowTexts(ListBox list, int index) =>
        [.. list.ContainerFromIndex(index)!.GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text)];

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
