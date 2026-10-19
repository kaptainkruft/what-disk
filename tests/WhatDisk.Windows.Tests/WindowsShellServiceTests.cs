using System.Runtime.Versioning;
using System.Text;
using WhatDisk.Platform;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests the shell actions that do not open windows: the Recycle Bin, Open With and the clipboard.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShellServiceTests : IDisposable
{
    /// <summary>The directory the tests work in.</summary>
    private readonly TempTree _tree;

    /// <summary>The service under test.</summary>
    private readonly WindowsShellService _shell = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsShellServiceTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsShellServiceTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
        _tree = new TempTree();
    }

    /// <summary>
    /// Windows offers everything except Quick Look, including File Explorer's context menu.
    /// </summary>
    [Fact]
    public void Capabilities_OmitQuickLook()
    {
        Assert.False(_shell.Capabilities.HasFlag(ShellCapabilities.QuickLook));
        Assert.True(_shell.Capabilities.HasFlag(ShellCapabilities.Trash | ShellCapabilities.Reveal | ShellCapabilities.ShowInfo | ShellCapabilities.OpenWith | ShellCapabilities.SystemMenu));
    }

    /// <summary>
    /// Moving a file and a folder to the Recycle Bin removes them from their folder.
    /// </summary>
    /// <returns>A task that completes when the items have been moved.</returns>
    [Fact(Timeout = 60_000)]
    public async Task MoveToTrash_RemovesTheItems()
    {
        var file = _tree.Write("recycle-me.txt", 10);
        _tree.Write("Folder/inside.txt", 10);

        await _shell.MoveToTrashAsync(file, TestContext.Current.CancellationToken);
        await _shell.MoveToTrashAsync(_tree.PathOf("Folder"), TestContext.Current.CancellationToken);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(_tree.PathOf("Folder")));
    }

    /// <summary>
    /// Moving a missing item to the Recycle Bin fails with an IO error.
    /// </summary>
    /// <returns>A task that completes when the failure has been observed.</returns>
    [Fact(Timeout = 60_000)]
    public async Task MoveToTrash_FailsForMissingItems() =>
        await Assert.ThrowsAsync<IOException>(() => _shell.MoveToTrashAsync(_tree.PathOf("missing.txt"), TestContext.Current.CancellationToken));

    /// <summary>
    /// Text files have registered applications, the default (if any) first and flagged once; folders have none.
    /// </summary>
    [Fact]
    public void GetApplications_ListsTheRegisteredApplications()
    {
        var file = _tree.Write("notes.txt", 5);

        var applications = _shell.GetApplications(file);

        Assert.NotEmpty(applications);
        Assert.True(applications.Count(static application => application.IsDefault) <= 1);
        Assert.DoesNotContain(applications.Skip(1), static application => application.IsDefault);
        Assert.All(applications, static application => Assert.False(string.IsNullOrWhiteSpace(application.Name)));
        Assert.Empty(_shell.GetApplications(_tree.Root));
    }

    /// <summary>
    /// Copied paths and copied items reach the clipboard in the formats other applications read.
    /// </summary>
    [Fact]
    public void Copy_PutsTextAndFilesOnTheClipboard()
    {
        var first = _tree.Write("one.txt", 1);
        var second = _tree.Write("two.txt", 1);
        try
        {
            _shell.CopyText($"{first}\r\n{second}");
            var text = WindowsClipboard.Get(User32.UnicodeTextFormat);
            Assert.Equal($"{first}\r\n{second}", Encoding.Unicode.GetString(text!).TrimEnd('\0'));

            _shell.CopyItems([first, second]);
            var files = WindowsClipboard.Get(User32.FileDropFormat);
            Assert.Equal([first, second], ClipboardFormats.ReadFileDrop(files!));
        }
        catch (IOException exception) when (exception.Message.StartsWith("Could not open the clipboard", StringComparison.Ordinal))
        {
            Assert.Skip($"The clipboard is not available in this session: {exception.Message}");
        }
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// Moving to the Recycle Bin asks Windows to warn before deleting an item permanently, unless the settings turn
    /// the warning off; the shell's own confirmation and error windows stay off either way.
    /// </summary>
    [Fact]
    public void RecycleFlags_FollowTheSettings()
    {
        var warning = WindowsShellService.RecycleFlags(warnWhenNotRecyclable: true);
        var silent = WindowsShellService.RecycleFlags(warnWhenNotRecyclable: false);

        Assert.NotEqual(0, warning & Shell32.FileOperationWantNukeWarning);
        Assert.Equal(0, silent & Shell32.FileOperationWantNukeWarning);
        Assert.All([warning, silent], static flags => Assert.Equal(
            Shell32.FileOperationAllowUndo | Shell32.FileOperationNoConfirmation | Shell32.FileOperationNoErrorUI | Shell32.FileOperationSilent,
            flags & (Shell32.FileOperationAllowUndo | Shell32.FileOperationNoConfirmation | Shell32.FileOperationNoErrorUI | Shell32.FileOperationSilent)));
    }
}
