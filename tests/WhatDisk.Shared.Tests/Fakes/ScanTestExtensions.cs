using WhatDisk.Scanning;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// Helpers for navigating scan trees in tests.
/// </summary>
internal static class ScanTestExtensions
{
    /// <summary>
    /// Waits for a scan to finish, failing the test if it takes too long.
    /// </summary>
    /// <param name="session">The scan.</param>
    /// <returns>The final status.</returns>
    public static async Task<ScanStatus> WaitAsync(this ScanSession session) =>
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

    /// <summary>
    /// Finds a child of a directory by name.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="name">The child's name.</param>
    /// <returns>The child.</returns>
    public static FileSystemNode Child(this DirectoryNode directory, string name) =>
        directory.Children.Single(child => child.Name == name);

    /// <summary>
    /// Finds a child directory by name.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="name">The child's name.</param>
    /// <returns>The child directory.</returns>
    public static DirectoryNode Dir(this DirectoryNode directory, string name) =>
        Assert.IsType<DirectoryNode>(directory.Child(name), exactMatch: false);
}
