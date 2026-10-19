using System.Runtime.Versioning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests getting File Explorer's context menu for items, without showing it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsContextMenuTests : IDisposable
{
    /// <summary>The directory the tests work in.</summary>
    private readonly TempTree _tree;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsContextMenuTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsContextMenuTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
        _tree = new TempTree();
    }

    /// <summary>
    /// The shell fills the menu for a file, and for a file and a folder in one folder, with commands that include
    /// Properties; with the extended commands, the menu has at least as many.
    /// </summary>
    /// <returns>A task that completes when the menus have been checked.</returns>
    [Fact(Timeout = 60_000)]
    public async Task Create_FillsTheShellsCommands()
    {
        var file = _tree.Write("menu.txt", 10);
        _tree.Write("Folder/inside.txt", 10);

        var (normal, extended, pair) = await StaThread.RunAsync(() =>
            (Verbs([file], extendedVerbs: false), Verbs([file], extendedVerbs: true), Verbs([file, _tree.PathOf("Folder")], extendedVerbs: false)));

        Assert.Contains("properties", normal, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("properties", pair, StringComparer.OrdinalIgnoreCase);
        Assert.True(extended.Count >= normal.Count, $"Extended: {string.Join(", ", extended)}; normal: {string.Join(", ", normal)}.");
    }

    /// <summary>
    /// Items in different folders have no menu in common.
    /// </summary>
    /// <returns>A task that completes when the failure has been observed.</returns>
    [Fact(Timeout = 60_000)]
    public async Task Create_RefusesItemsInDifferentFolders()
    {
        var outer = _tree.Write("outer.txt", 1);
        var inner = _tree.Write("Folder/inner.txt", 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => StaThread.RunAsync(() => WindowsContextMenu.Create([outer, inner], 0, extendedVerbs: false)));
    }

    /// <summary>
    /// A missing item fails with an IO error.
    /// </summary>
    /// <returns>A task that completes when the failure has been observed.</returns>
    [Fact(Timeout = 60_000)]
    public async Task Create_MissingItem_Throws()
    {
        var missing = _tree.PathOf("missing.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(() => StaThread.RunAsync(() => WindowsContextMenu.Create([missing], 0, extendedVerbs: false)));
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// Gets the menu for items and lists its commands' verbs.
    /// </summary>
    /// <param name="paths">The items.</param>
    /// <param name="extendedVerbs">Whether to include the commands normally shown only with Shift held.</param>
    /// <returns>The verbs.</returns>
    private static List<string> Verbs(string[] paths, bool extendedVerbs)
    {
        using var menu = WindowsContextMenu.Create(paths, 0, extendedVerbs);
        Assert.True(User32.GetMenuItemCount(menu.Menu) > 0);
        return menu.GetVerbs();
    }
}
