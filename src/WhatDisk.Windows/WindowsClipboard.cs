using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// Writes to the Windows clipboard from any thread. A hidden message-only window owns the clipboard while it is
/// changed, because data set without an owner window is rejected.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsClipboard
{
    /// <summary>How many times to try opening the clipboard while another application holds it.</summary>
    private const int OpenAttempts = 10;

    /// <summary>
    /// Replaces the clipboard's contents.
    /// </summary>
    /// <param name="formats">The formats and their data.</param>
    /// <exception cref="IOException">The clipboard could not be opened or written.</exception>
    public static void Set(IReadOnlyList<(uint Format, byte[] Data)> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var owner = User32.CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, User32.MessageOnlyParent, 0, 0, 0);
        try
        {
            Open(owner);
            try
            {
                if (!User32.EmptyClipboard())
                {
                    throw Failure("empty");
                }

                foreach (var (format, data) in formats)
                {
                    var memory = Copy(data);
                    if (User32.SetClipboardData(format, memory) == 0)
                    {
                        Kernel32.GlobalFree(memory);
                        throw Failure("write");
                    }
                }
            }
            finally
            {
                User32.CloseClipboard();
            }
        }
        finally
        {
            if (owner != 0)
            {
                User32.DestroyWindow(owner);
            }
        }
    }

    /// <summary>
    /// Reads one format from the clipboard.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The data, or <see langword="null"/> when the clipboard does not hold the format.</returns>
    public static byte[]? Get(uint format)
    {
        Open(0);
        try
        {
            var memory = User32.GetClipboardData(format);
            if (memory == 0)
            {
                return null;
            }

            var size = (int)Kernel32.GlobalSize(memory);
            var pointer = Kernel32.GlobalLock(memory);
            try
            {
                return new ReadOnlySpan<byte>(pointer, size).ToArray();
            }
            finally
            {
                Kernel32.GlobalUnlock(memory);
            }
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    /// <summary>
    /// Opens the clipboard, retrying briefly while another application holds it.
    /// </summary>
    /// <param name="owner">The window that will own the clipboard.</param>
    private static void Open(nint owner)
    {
        for (var attempt = 1; !User32.OpenClipboard(owner); attempt++)
        {
            if (attempt == OpenAttempts)
            {
                throw Failure("open");
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Copies data to movable global memory.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The memory handle.</returns>
    private static nint Copy(byte[] data)
    {
        var memory = Kernel32.GlobalAlloc(Kernel32.GlobalMoveable, (nuint)data.Length);
        if (memory == 0)
        {
            throw Failure("allocate memory for");
        }

        var pointer = Kernel32.GlobalLock(memory);
        data.CopyTo(new Span<byte>(pointer, data.Length));
        Kernel32.GlobalUnlock(memory);
        return memory;
    }

    /// <summary>
    /// Creates the exception for a failed clipboard call.
    /// </summary>
    /// <param name="action">What failed.</param>
    /// <returns>The exception.</returns>
    private static IOException Failure(string action) =>
        new($"Could not {action} the clipboard: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
}
