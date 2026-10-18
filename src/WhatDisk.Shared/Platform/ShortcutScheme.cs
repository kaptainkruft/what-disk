using Avalonia.Input;

namespace WhatDisk.Platform;

/// <summary>
/// The keyboard shortcuts and drag modifiers for actions on tree items, following one platform's conventions. The
/// context menu, the menu bar and the tree's key handling all read them from <see cref="PlatformCapabilities.Shortcuts"/>.
/// </summary>
public sealed record ShortcutScheme
{
    /// <summary>
    /// Gets the macOS scheme, following the Finder: ⌘O, ⌘R, Space, ⌘I, ⌘⌫ and so on.
    /// </summary>
    public static ShortcutScheme MacOS { get; } = new()
    {
        CommandKey = KeyModifiers.Meta,
        CopyDragModifier = KeyModifiers.Alt,
        MoveDragModifier = KeyModifiers.Meta,
        Open = new KeyGesture(Key.O, KeyModifiers.Meta),
        OpenAlternate = new KeyGesture(Key.Down, KeyModifiers.Meta),
        Reveal = new KeyGesture(Key.R, KeyModifiers.Meta),
        QuickLook = new KeyGesture(Key.Space),
        QuickLookMenu = new KeyGesture(Key.Y, KeyModifiers.Meta),
        GetInfo = new KeyGesture(Key.I, KeyModifiers.Meta),
        Copy = new KeyGesture(Key.C, KeyModifiers.Meta),
        CopyPath = new KeyGesture(Key.C, KeyModifiers.Meta | KeyModifiers.Alt),
        SelectAll = new KeyGesture(Key.A, KeyModifiers.Meta),
        ExpandAll = new KeyGesture(Key.Right, KeyModifiers.Alt),
        CollapseAll = new KeyGesture(Key.Left, KeyModifiers.Alt),
        Rescan = new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Shift),
        Settings = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        FindDuplicatesInScan = new KeyGesture(Key.D, KeyModifiers.Meta | KeyModifiers.Shift),
        FindDuplicatesInSelection = new KeyGesture(Key.D, KeyModifiers.Meta),
        CloseTab = new KeyGesture(Key.W, KeyModifiers.Meta),
        Quit = new KeyGesture(Key.Q, KeyModifiers.Meta),
        MoveToTrash = new KeyGesture(Key.Back, KeyModifiers.Meta),
        DeletePermanently = new KeyGesture(Key.Back, KeyModifiers.Meta | KeyModifiers.Alt),
    };

    /// <summary>
    /// Gets the Windows scheme, following File Explorer: Delete and Shift+Delete, Alt+Enter for Properties, F5 to
    /// rescan, Ctrl+Shift+C to copy paths and numeric-keypad * to expand everything beneath a folder.
    /// </summary>
    public static ShortcutScheme Windows { get; } = new()
    {
        CommandKey = KeyModifiers.Control,
        CopyDragModifier = KeyModifiers.Control,
        MoveDragModifier = KeyModifiers.Shift,
        Open = new KeyGesture(Key.O, KeyModifiers.Control),
        OpenAlternate = null,
        Reveal = new KeyGesture(Key.R, KeyModifiers.Alt | KeyModifiers.Shift),
        QuickLook = null,
        QuickLookMenu = null,
        GetInfo = new KeyGesture(Key.Enter, KeyModifiers.Alt),
        Copy = new KeyGesture(Key.C, KeyModifiers.Control),
        CopyPath = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
        SelectAll = new KeyGesture(Key.A, KeyModifiers.Control),
        ExpandAll = new KeyGesture(Key.Right, KeyModifiers.Alt),
        ExpandAllAlternate = new KeyGesture(Key.Multiply),
        CollapseAll = new KeyGesture(Key.Left, KeyModifiers.Alt),
        Rescan = new KeyGesture(Key.F5),
        Settings = new KeyGesture(Key.OemComma, KeyModifiers.Control),
        FindDuplicatesInScan = new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift),
        FindDuplicatesInSelection = new KeyGesture(Key.D, KeyModifiers.Control),
        CloseTab = new KeyGesture(Key.W, KeyModifiers.Control),
        MoveToTrash = new KeyGesture(Key.Delete),
        DeletePermanently = new KeyGesture(Key.Delete, KeyModifiers.Shift),
    };

    /// <summary>
    /// Gets the scheme for the operating system the app is running on: the macOS scheme on macOS, the Windows scheme
    /// (whose Control-based shortcuts also suit Linux desktops) elsewhere.
    /// </summary>
    public static ShortcutScheme ForCurrentPlatform => OperatingSystem.IsMacOS() ? MacOS : Windows;

    /// <summary>Gets the platform's command modifier: Command on macOS, Control elsewhere.</summary>
    public required KeyModifiers CommandKey { get; init; }

    /// <summary>Gets the modifier that makes a drag copy: Option on macOS, Control on Windows.</summary>
    public required KeyModifiers CopyDragModifier { get; init; }

    /// <summary>Gets the modifier that makes a drag move: Command on macOS, Shift on Windows.</summary>
    public required KeyModifiers MoveDragModifier { get; init; }

    /// <summary>Gets the shortcut for Open.</summary>
    public required KeyGesture Open { get; init; }

    /// <summary>Gets a second shortcut for Open (⌘↓ on macOS, as in the Finder), or <see langword="null"/>.</summary>
    public KeyGesture? OpenAlternate { get; init; }

    /// <summary>Gets the shortcut for showing items in the file manager.</summary>
    public required KeyGesture Reveal { get; init; }

    /// <summary>Gets the tree's shortcut for previewing items, or <see langword="null"/> where there are no previews.</summary>
    public KeyGesture? QuickLook { get; init; }

    /// <summary>
    /// Gets the menu bar's shortcut for previewing items (⌘Y on macOS: a menu shortcut cannot be a bare Space), or
    /// <see langword="null"/>.
    /// </summary>
    public KeyGesture? QuickLookMenu { get; init; }

    /// <summary>Gets the shortcut for the file manager's information window (Get Info or Properties).</summary>
    public required KeyGesture GetInfo { get; init; }

    /// <summary>Gets the shortcut for copying items.</summary>
    public required KeyGesture Copy { get; init; }

    /// <summary>Gets the shortcut for copying paths.</summary>
    public required KeyGesture CopyPath { get; init; }

    /// <summary>Gets the shortcut for selecting every row.</summary>
    public required KeyGesture SelectAll { get; init; }

    /// <summary>Gets the shortcut for expanding everything beneath the selection.</summary>
    public required KeyGesture ExpandAll { get; init; }

    /// <summary>Gets a second shortcut for expanding everything beneath the selection, or <see langword="null"/>.</summary>
    public KeyGesture? ExpandAllAlternate { get; init; }

    /// <summary>Gets the shortcut for collapsing everything beneath the selection.</summary>
    public required KeyGesture CollapseAll { get; init; }

    /// <summary>Gets the shortcut for rescanning the selection.</summary>
    public required KeyGesture Rescan { get; init; }

    /// <summary>Gets the shortcut for moving items to the Trash or Recycle Bin.</summary>
    public required KeyGesture MoveToTrash { get; init; }

    /// <summary>Gets the shortcut for deleting items permanently.</summary>
    public required KeyGesture DeletePermanently { get; init; }

    /// <summary>Gets the shortcut that opens the context menu from the keyboard (as does the Menu key).</summary>
    public KeyGesture ContextMenu { get; init; } = new(Key.F10, KeyModifiers.Shift);

    /// <summary>
    /// Gets the key that opens the settings window: ⌘, on macOS, Ctrl+, on Windows.
    /// </summary>
    public required KeyGesture Settings { get; init; }

    /// <summary>
    /// Gets the key that finds duplicates among every scanned file: ⇧⌘D on macOS, Ctrl+Shift+D on Windows.
    /// </summary>
    public required KeyGesture FindDuplicatesInScan { get; init; }

    /// <summary>
    /// Gets the key that finds duplicates among the selected items: ⌘D on macOS, Ctrl+D on Windows.
    /// </summary>
    public required KeyGesture FindDuplicatesInSelection { get; init; }

    /// <summary>
    /// Gets the key that closes the tab shown (not the Scan tab): ⌘W on macOS, Ctrl+W on Windows.
    /// </summary>
    public required KeyGesture CloseTab { get; init; }

    /// <summary>
    /// Gets the key that quits, where the app handles it itself (⌘Q on macOS when its menus are drawn in the window,
    /// since the system menu bar's Quit is not there), or <see langword="null"/> where the system handles it (Alt+F4 on
    /// Windows).
    /// </summary>
    public KeyGesture? Quit { get; init; }
}
