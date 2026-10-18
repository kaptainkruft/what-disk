using CommunityToolkit.Mvvm.ComponentModel;

namespace WhatDisk.ViewModels;

/// <summary>
/// One tab of the main window. Everything the app shows lives in the main window: the scan (which cannot be closed),
/// the results of each duplicate search, and the settings. Keeping one window means the macOS menu bar never switches
/// between windows' menus, which Avalonia does unreliably on macOS 27.
/// </summary>
public abstract partial class TabViewModel : ObservableObject
{
    /// <summary>
    /// Gets the tab's title in the tab strip.
    /// </summary>
    public abstract string Header { get; }

    /// <summary>
    /// Gets the tab's tooltip, such as the full path its title shortens, or <see langword="null"/>.
    /// </summary>
    public virtual string? ToolTip => null;

    /// <summary>
    /// Gets a value indicating whether the tab can be closed.
    /// </summary>
    public virtual bool CanClose => true;

    /// <summary>
    /// Gets a value indicating whether the tab is the one shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }

    /// <summary>
    /// Called after the tab has been closed, to stop its work and release what it holds.
    /// </summary>
    public virtual void OnClosed()
    {
    }
}

/// <summary>
/// The Scan tab: choosing what to scan, and the tree, chart and details of the results. It is always there and cannot
/// be closed; its content is the main window view model's.
/// </summary>
public sealed class ScanTabViewModel : TabViewModel
{
    /// <inheritdoc/>
    public override string Header => "Scan";

    /// <inheritdoc/>
    public override bool CanClose => false;
}
