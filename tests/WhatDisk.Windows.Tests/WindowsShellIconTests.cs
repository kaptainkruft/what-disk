using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests shell icons and type names.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShellIconTests
{
    /// <summary>The icon provider under test.</summary>
    private readonly WindowsIconProvider _icons = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsShellIconTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsShellIconTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");

    /// <summary>
    /// Folder, file-type and drive icons load at small and large sizes as visible premultiplied pixels.
    /// </summary>
    /// <param name="pixelSize">The size requested.</param>
    /// <returns>A task that completes when the icons have loaded on a background thread, as the icon cache loads them.</returns>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public async Task LoadIcon_LoadsShellIcons(int pixelSize)
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
        IconKey[] keys =
        [
            new(IconKey.FolderCategory),
            new(IconKey.FileCategory, ".txt"),
            new(IconKey.FileCategory, ".unknown-extension-for-test"),
            new(IconKey.VolumeCategory, drive),
            new(IconKey.PathCategory, Environment.ProcessPath),
        ];

        foreach (var key in keys)
        {
            var image = await Task.Run(() => _icons.LoadIcon(key, pixelSize), TestContext.Current.CancellationToken);
            Assert.NotNull(image);
            Assert.Equal(IconImageFormat.Bgra32Premultiplied, image.Format);
            Assert.True(image.Width >= Math.Min(pixelSize, 16) && image.Height == image.Width, $"{key} gave a {image.Width}x{image.Height} icon.");
            Assert.Equal(image.Width * image.Height * 4, image.Data.Length);
            Assert.True(IconPixels.HasAlpha(image.Data), $"{key} is fully transparent.");
        }
    }

    /// <summary>
    /// Programs, shortcuts and special folders are keyed by path (they have their own icons); other items by type.
    /// </summary>
    /// <returns>A task that completes when the folder has been scanned.</returns>
    [Fact]
    public async Task GetKey_UsesPathsForItemsWithTheirOwnIcons()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.PathOf("Downloads"));
        Directory.CreateDirectory(tree.PathOf("Projects"));
        tree.Write("setup.exe", 1);
        tree.Write("Notes.TXT", 1);
        using var session = new DiskScanner(new WindowsFileSystemEnumerator(), new WindowsVolumeService(), TimeProvider.System).Start(new ScanRequest([tree.Root]));
        Assert.Equal(ScanStatus.Completed, await session.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        var children = ((DirectoryNode)session.Roots[0]).Children.ToDictionary(static child => child.Name, StringComparer.Ordinal);

        Assert.Equal(new IconKey(IconKey.PathCategory, children["Downloads"].FullPath), _icons.GetKey(children["Downloads"]));
        Assert.Equal(new IconKey(IconKey.FolderCategory), _icons.GetKey(children["Projects"]));
        Assert.Equal(new IconKey(IconKey.PathCategory, children["setup.exe"].FullPath), _icons.GetKey(children["setup.exe"]));
        Assert.Equal(new IconKey(IconKey.FileCategory, ".txt"), _icons.GetKey(children["Notes.TXT"]));
    }

    /// <summary>
    /// Registered types are described by Windows; unregistered ones still get a description.
    /// </summary>
    [Fact]
    public void Describe_UsesWindowsTypeNames()
    {
        var types = new WindowsFileTypeProvider();

        var text = types.Describe(".txt");
        var unknown = types.Describe(".unknown-extension-for-test");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual(new DefaultFileTypeProvider().Describe(".txt"), text);
        Assert.False(string.IsNullOrWhiteSpace(unknown));
        Assert.Equal(new DefaultFileTypeProvider().Describe(string.Empty), types.Describe(string.Empty));
    }
}
