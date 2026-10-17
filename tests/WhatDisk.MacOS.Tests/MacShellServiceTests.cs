using System.Runtime.Versioning;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests the macOS shell service and single-item lookups against the real system. Actions that open windows or replace
/// the clipboard are not exercised. Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacShellServiceTests : IDisposable
{
    /// <summary>A temporary directory deleted after each test.</summary>
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("whatdisk-shell-");

    /// <summary>The service under test.</summary>
    private readonly MacShellService _shell = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MacShellServiceTests"/> class, skipping off macOS.
    /// </summary>
    public MacShellServiceTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// Moving an item to the Trash removes it from its folder and puts it in the user's Trash.
    /// </summary>
    /// <returns>A task that completes when the item has been trashed.</returns>
    [Fact]
    public async Task MoveToTrash_PutsTheItemInTheTrash()
    {
        var name = $"whatdisk-test-{Guid.NewGuid():N}.txt";
        var path = Path.Join(_temp.FullName, name);
        await File.WriteAllTextAsync(path, "trash me", TestContext.Current.CancellationToken);
        var trash = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");

        await _shell.MoveToTrashAsync(path, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(path));
        var trashed = Path.Join(trash, name);
        try
        {
            Assert.True(File.Exists(trashed), $"Expected {trashed} in the Trash.");
        }
        finally
        {
            File.Delete(trashed);
        }
    }

    /// <summary>
    /// A missing item cannot be trashed.
    /// </summary>
    /// <returns>A task that completes when the failure has been observed.</returns>
    [Fact]
    public async Task MoveToTrash_FailsForMissingItems() =>
        await Assert.ThrowsAsync<IOException>(() => _shell.MoveToTrashAsync(Path.Join(_temp.FullName, "missing"), TestContext.Current.CancellationToken));

    /// <summary>
    /// The applications for a text file include its default application, listed first.
    /// </summary>
    [Fact]
    public void GetApplications_ListsTheDefaultFirst()
    {
        var path = Path.Join(_temp.FullName, "note.txt");
        File.WriteAllText(path, "hello");

        var applications = _shell.GetApplications(path);

        Assert.NotEmpty(applications);
        Assert.True(applications[0].IsDefault);
        Assert.Single(applications, static application => application.IsDefault);
        Assert.All(applications, static application => Assert.EndsWith(".app", application.Path, StringComparison.Ordinal));
    }

    /// <summary>
    /// A single item is described with its allocated size, and a folder as a folder.
    /// </summary>
    [Fact]
    public void GetEntry_DescribesSingleItems()
    {
        var enumerator = new MacFileSystemEnumerator();
        var file = Path.Join(_temp.FullName, "data.bin");
        File.WriteAllBytes(file, new byte[10_000]);

        var entry = enumerator.GetEntry(file);
        var folder = enumerator.GetEntry(_temp.FullName);

        Assert.NotNull(entry);
        Assert.Equal("data.bin", entry.Value.Name);
        Assert.Equal(EntryType.File, entry.Value.Type);
        Assert.Equal(10_000, entry.Value.LogicalSize);
        Assert.True(entry.Value.AllocatedSize >= 10_000);
        Assert.Equal(EntryType.Directory, folder?.Type);
        Assert.Null(enumerator.GetEntry(Path.Join(_temp.FullName, "missing")));
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _temp.Delete(recursive: true);
}
