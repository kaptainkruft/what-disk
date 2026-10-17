using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Runs shell user-interface calls in a single-threaded apartment, as they require: on the calling thread when it is
/// already one (the UI thread), otherwise on a new background thread.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class StaThread
{
    /// <summary>
    /// Runs an action in a single-threaded apartment and waits for it.
    /// </summary>
    /// <param name="action">The action.</param>
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        RunAsync(() =>
        {
            action();
            return true;
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs a function on a new background thread in a single-threaded apartment.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="function">The function.</param>
    /// <returns>A task that completes with the function's result or exception.</returns>
    public static Task<T> RunAsync<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(function());
            }
#pragma warning disable CA1031 // The exception is passed to the caller through the task.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "WhatDisk shell call",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
