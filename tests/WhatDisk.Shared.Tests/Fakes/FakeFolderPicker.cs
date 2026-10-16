using WhatDisk.Platform;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// A folder picker that returns a preset answer.
/// </summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    /// <summary>
    /// Gets or sets the folder returned by <see cref="PickFolderAsync"/>, or <see langword="null"/> to simulate cancel.
    /// </summary>
    public string? Answer { get; set; }

    /// <inheritdoc/>
    public bool IsSupported { get; set; } = true;

    /// <inheritdoc/>
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult(Answer);
}
