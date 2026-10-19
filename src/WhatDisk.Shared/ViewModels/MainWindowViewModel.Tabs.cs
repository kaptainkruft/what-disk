using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace WhatDisk.ViewModels;

/// <summary>
/// The main window's tabs: the Scan tab, which is always first and cannot be closed, then a tab for each duplicate
/// search's results and, when open, the settings.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The tab shown.</summary>
    private TabViewModel _selectedTab;

    /// <summary>
    /// Gets the Scan tab.
    /// </summary>
    public ScanTabViewModel ScanTab { get; }

    /// <summary>
    /// Gets the tabs, in the tab strip's order.
    /// </summary>
    public ObservableCollection<TabViewModel> Tabs { get; }

    /// <summary>
    /// Gets or sets the tab shown. Leaving the Scan tab makes the tree inactive, so the item commands (and their
    /// shortcuts) do not act on a tree that cannot be seen.
    /// </summary>
    public TabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is null || !Tabs.Contains(value))
            {
                // The tab strip clears its selection while a tab is removed; keep showing a tab.
                OnPropertyChanged();
                return;
            }

            var previous = _selectedTab;
            if (!SetProperty(ref _selectedTab, value))
            {
                return;
            }

            previous.IsSelected = false;
            value.IsSelected = true;
            OnPropertyChanged(nameof(IsScanTabSelected));
            CloseTabCommand.NotifyCanExecuteChanged();
            if (value is SettingsViewModel settings)
            {
                settings.Reload();
            }

            if (!IsScanTabSelected)
            {
                IsTreeActive = false;
            }

            if (ReferenceEquals(value, DuplicatesTab) && _duplicatesStale)
            {
                UpdateDuplicates();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Scan tab is shown.
    /// </summary>
    public bool IsScanTabSelected => ReferenceEquals(_selectedTab, ScanTab);

    /// <summary>
    /// Adds a tab after the others and shows it.
    /// </summary>
    /// <param name="tab">The tab.</param>
    public void OpenTab(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    /// <summary>
    /// Called when the main window becomes active again, for example after editing the settings file in another app:
    /// the settings tab, if shown, reads the file again.
    /// </summary>
    public void OnWindowActivated()
    {
        if (_selectedTab is SettingsViewModel settings)
        {
            settings.Reload();
        }
    }

    /// <summary>
    /// Closes a tab (the one shown, when none is given) and shows the tab before it. The Scan tab cannot be closed.
    /// </summary>
    /// <param name="tab">The tab, or <see langword="null"/> for the one shown.</param>
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab(TabViewModel? tab)
    {
        tab ??= _selectedTab;
        var index = Tabs.IndexOf(tab);
        if (!tab.CanClose || index < 0)
        {
            return;
        }

        if (ReferenceEquals(tab, _selectedTab))
        {
            SelectedTab = Tabs[index - 1];
        }

        Tabs.Remove(tab);
        tab.OnClosed();
    }

    /// <summary>
    /// Determines whether a tab can be closed.
    /// </summary>
    /// <param name="tab">The tab, or <see langword="null"/> for the one shown.</param>
    /// <returns><see langword="true"/> when the tab can be closed and no question is being asked.</returns>
    private bool CanCloseTab(TabViewModel? tab) => (tab ?? _selectedTab).CanClose && !IsDialogOpen;
}
