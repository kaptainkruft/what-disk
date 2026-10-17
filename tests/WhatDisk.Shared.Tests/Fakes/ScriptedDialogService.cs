using WhatDisk.FileOperations;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// A dialog service that gives scripted answers and records the questions.
/// </summary>
internal sealed class ScriptedDialogService : IDialogService
{
    /// <summary>
    /// Gets or sets the answer to confirmations.
    /// </summary>
    public bool Confirm { get; set; } = true;

    /// <summary>
    /// Gets or sets the answer to conflicts.
    /// </summary>
    public ConflictDecision Conflict { get; set; } = new(ConflictResolution.KeepBoth);

    /// <summary>
    /// Gets the confirmations asked.
    /// </summary>
    public List<ConfirmationRequest> Confirmations { get; } = [];

    /// <summary>
    /// Gets the conflicts asked about.
    /// </summary>
    public List<TransferConflict> Conflicts { get; } = [];

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        Confirmations.Add(request);
        return Task.FromResult(Confirm);
    }

    /// <inheritdoc/>
    public Task<ConflictDecision> ResolveConflictAsync(TransferConflict conflict)
    {
        Conflicts.Add(conflict);
        return Task.FromResult(Conflict);
    }
}
