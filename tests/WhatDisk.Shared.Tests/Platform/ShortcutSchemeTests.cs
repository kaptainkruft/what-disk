using Avalonia.Input;
using WhatDisk.Platform;

namespace WhatDisk.Tests.Platform;

/// <summary>
/// Tests the platforms' keyboard shortcuts and drag modifiers.
/// </summary>
public sealed class ShortcutSchemeTests
{
    /// <summary>
    /// The macOS scheme follows the Finder.
    /// </summary>
    [Fact]
    public void MacOS_FollowsTheFinder()
    {
        var keys = ShortcutScheme.MacOS;

        Assert.Equal(KeyModifiers.Meta, keys.CommandKey);
        Assert.Equal((KeyModifiers.Alt, KeyModifiers.Meta), (keys.CopyDragModifier, keys.MoveDragModifier));
        Assert.Equal(new KeyGesture(Key.Back, KeyModifiers.Meta), keys.MoveToTrash);
        Assert.Equal(new KeyGesture(Key.Back, KeyModifiers.Meta | KeyModifiers.Alt), keys.DeletePermanently);
        Assert.Equal(new KeyGesture(Key.I, KeyModifiers.Meta), keys.GetInfo);
        Assert.Equal(new KeyGesture(Key.Space), keys.QuickLook);
        Assert.Equal(new KeyGesture(Key.Down, KeyModifiers.Meta), keys.OpenAlternate);
        Assert.Equal(new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Shift), keys.Rescan);
    }

    /// <summary>
    /// The Windows scheme follows File Explorer: Delete and Shift+Delete, Alt+Enter, F5, Ctrl+Shift+C, Ctrl to copy and
    /// Shift to move when dragging, and no Quick Look.
    /// </summary>
    [Fact]
    public void Windows_FollowsFileExplorer()
    {
        var keys = ShortcutScheme.Windows;

        Assert.Equal(KeyModifiers.Control, keys.CommandKey);
        Assert.Equal((KeyModifiers.Control, KeyModifiers.Shift), (keys.CopyDragModifier, keys.MoveDragModifier));
        Assert.Equal(new KeyGesture(Key.Delete), keys.MoveToTrash);
        Assert.Equal(new KeyGesture(Key.Delete, KeyModifiers.Shift), keys.DeletePermanently);
        Assert.Equal(new KeyGesture(Key.Enter, KeyModifiers.Alt), keys.GetInfo);
        Assert.Equal(new KeyGesture(Key.F5), keys.Rescan);
        Assert.Equal(new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift), keys.CopyPath);
        Assert.Equal(new KeyGesture(Key.Multiply), keys.ExpandAllAlternate);
        Assert.Null(keys.QuickLook);
        Assert.Null(keys.QuickLookMenu);
        Assert.Null(keys.OpenAlternate);
    }

    /// <summary>
    /// No two actions share a shortcut in either scheme, and both open the context menu with Shift+F10.
    /// </summary>
    /// <param name="name">The scheme.</param>
    [Theory]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public void Schemes_HaveNoClashes(string name)
    {
        var keys = name == "macOS" ? ShortcutScheme.MacOS : ShortcutScheme.Windows;
        KeyGesture?[] gestures =
        [
            keys.Open, keys.OpenAlternate, keys.Reveal, keys.QuickLook, keys.QuickLookMenu, keys.GetInfo, keys.Copy,
            keys.CopyPath, keys.SelectAll, keys.ExpandAll, keys.ExpandAllAlternate, keys.CollapseAll, keys.Rescan,
            keys.MoveToTrash, keys.DeletePermanently, keys.ContextMenu,
        ];

        var present = gestures.OfType<KeyGesture>().ToList();
        Assert.Equal(present.Count, present.Distinct().Count());
        Assert.Equal(new KeyGesture(Key.F10, KeyModifiers.Shift), keys.ContextMenu);
        Assert.DoesNotContain(present, static gesture => gesture is { Key: Key.Enter, KeyModifiers: KeyModifiers.None });
    }

    /// <summary>
    /// The current platform's scheme is the macOS one on macOS and the Windows one elsewhere.
    /// </summary>
    [Fact]
    public void ForCurrentPlatform_MatchesTheOperatingSystem() =>
        Assert.Same(OperatingSystem.IsMacOS() ? ShortcutScheme.MacOS : ShortcutScheme.Windows, ShortcutScheme.ForCurrentPlatform);
}
