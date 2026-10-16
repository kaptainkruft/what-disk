namespace WhatDisk.Tests.Fakes;

/// <summary>
/// Waits for conditions that become true on another thread.
/// </summary>
internal static class Eventually
{
    /// <summary>
    /// Polls a condition until it holds, failing the test after a timeout.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="because">Describes the condition for the failure message.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task True(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
