namespace WhatDisk.ViewModels;

/// <summary>
/// Receives notifications from the rows it owns.
/// </summary>
internal interface ITreeRowOwner
{
    /// <summary>
    /// Called when a row is expanded or collapsed.
    /// </summary>
    /// <param name="row">The row whose expansion changed.</param>
    void OnExpansionChanged(TreeRowViewModel row);
}
