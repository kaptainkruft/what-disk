using Avalonia.Input;

namespace WhatDisk.Views;

/// <summary>
/// The keyboard shortcuts and drag modifiers for actions on tree items, following the platform's conventions
/// (Command on macOS, Control elsewhere). The context menu, the menu bar and the tree's key handling all use these.
/// </summary>
public static class ItemShortcuts
{
    /// <summary>
    /// Gets the platform's command modifier: Command on macOS, Control elsewhere.
    /// </summary>
    public static KeyModifiers CommandKey { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// Gets the modifier that makes a drag copy: Option on macOS, Control elsewhere.
    /// </summary>
    public static KeyModifiers CopyDragModifier { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control;

    /// <summary>
    /// Gets the modifier that makes a drag move: Command on macOS, Shift elsewhere.
    /// </summary>
    public static KeyModifiers MoveDragModifier { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Shift;

    /// <summary>Gets the shortcut for Open.</summary>
    public static KeyGesture Open { get; } = new(Key.O, CommandKey);

    /// <summary>Gets the second shortcut for Open, as in Finder.</summary>
    public static KeyGesture OpenAlternate { get; } = new(Key.Down, CommandKey);

    /// <summary>Gets the shortcut for showing items in the file manager.</summary>
    public static KeyGesture Reveal { get; } = new(Key.R, CommandKey);

    /// <summary>Gets the shortcut for Quick Look in the tree.</summary>
    public static KeyGesture QuickLook { get; } = new(Key.Space);

    /// <summary>Gets the menu bar's shortcut for Quick Look, as in Finder.</summary>
    public static KeyGesture QuickLookMenu { get; } = new(Key.Y, CommandKey);

    /// <summary>Gets the shortcut for Get Info.</summary>
    public static KeyGesture GetInfo { get; } = new(Key.I, CommandKey);

    /// <summary>Gets the shortcut for copying items.</summary>
    public static KeyGesture Copy { get; } = new(Key.C, CommandKey);

    /// <summary>Gets the shortcut for copying paths.</summary>
    public static KeyGesture CopyPath { get; } = new(Key.C, CommandKey | KeyModifiers.Alt);

    /// <summary>Gets the shortcut for selecting every row.</summary>
    public static KeyGesture SelectAll { get; } = new(Key.A, CommandKey);

    /// <summary>Gets the shortcut for expanding everything beneath the selection.</summary>
    public static KeyGesture ExpandAll { get; } = new(Key.Right, KeyModifiers.Alt);

    /// <summary>Gets the shortcut for collapsing everything beneath the selection.</summary>
    public static KeyGesture CollapseAll { get; } = new(Key.Left, KeyModifiers.Alt);

    /// <summary>Gets the shortcut for rescanning the selection.</summary>
    public static KeyGesture Rescan { get; } = new(Key.R, CommandKey | KeyModifiers.Shift);

    /// <summary>Gets the shortcut for moving items to the Trash.</summary>
    public static KeyGesture MoveToTrash { get; } = new(Key.Back, CommandKey);

    /// <summary>Gets the shortcut for deleting items permanently.</summary>
    public static KeyGesture DeleteImmediately { get; } = new(Key.Back, CommandKey | KeyModifiers.Alt);

    /// <summary>Gets the shortcut that opens the context menu from the keyboard.</summary>
    public static KeyGesture ContextMenu { get; } = new(Key.F10, KeyModifiers.Shift);
}
