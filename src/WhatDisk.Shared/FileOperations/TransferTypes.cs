namespace WhatDisk.FileOperations;

/// <summary>
/// Whether items are moved or copied.
/// </summary>
public enum TransferMode
{
    /// <summary>The items end up only at the destination.</summary>
    Move,

    /// <summary>The items end up at both the source and the destination.</summary>
    Copy,
}

/// <summary>
/// How to handle an item whose name is already taken at the destination.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Delete the existing item and put the new one in its place.</summary>
    Replace,

    /// <summary>Keep the existing item and give the new one a numbered name, such as "Report 2.pdf".</summary>
    KeepBoth,

    /// <summary>Leave this item where it is.</summary>
    Skip,

    /// <summary>Stop the whole operation.</summary>
    Stop,
}

/// <summary>
/// The user's answer to a name conflict.
/// </summary>
/// <param name="Resolution">What to do.</param>
/// <param name="ApplyToAll">Whether to do the same for every later conflict in this operation.</param>
public sealed record ConflictDecision(ConflictResolution Resolution, bool ApplyToAll = false);

/// <summary>
/// An item whose name is already taken at the destination.
/// </summary>
/// <param name="Source">The item being moved or copied.</param>
/// <param name="ExistingPath">The item already at the destination.</param>
/// <param name="IsDirectory">Whether the item being moved or copied is a directory.</param>
public sealed record TransferConflict(string Source, string ExistingPath, bool IsDirectory);

/// <summary>
/// How far an operation has got.
/// </summary>
/// <param name="ItemsDone">The number of items finished.</param>
/// <param name="ItemsTotal">The number of items in the operation.</param>
/// <param name="BytesCopied">The number of bytes copied so far.</param>
/// <param name="CurrentName">The name of the item in progress.</param>
public sealed record TransferProgress(int ItemsDone, int ItemsTotal, long BytesCopied, string? CurrentName);

/// <summary>
/// What happened to one item.
/// </summary>
public enum TransferOutcome
{
    /// <summary>The item was moved or copied.</summary>
    Completed,

    /// <summary>The item was left alone, by choice or because it was already in place.</summary>
    Skipped,

    /// <summary>The item could not be moved or copied.</summary>
    Failed,

    /// <summary>The operation was stopped before the item was finished.</summary>
    Cancelled,
}

/// <summary>
/// The result for one item of an operation.
/// </summary>
/// <param name="Source">The item's original path.</param>
/// <param name="Destination">Where the item ended up, when it was moved or copied.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Replaced">Whether an item already at the destination was replaced.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record TransferResult(string Source, string? Destination, TransferOutcome Outcome, bool Replaced = false, string? Error = null);
