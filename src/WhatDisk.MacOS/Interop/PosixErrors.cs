using System.Runtime.InteropServices;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Interop;

/// <summary>
/// Converts macOS <c>errno</c> values into the exceptions the scanner understands.
/// </summary>
internal static class PosixErrors
{
    /// <summary><c>EPERM</c>.</summary>
    public const int NotPermitted = 1;

    /// <summary><c>ENOENT</c>.</summary>
    public const int NoSuchEntry = 2;

    /// <summary><c>EINTR</c>.</summary>
    public const int Interrupted = 4;

    /// <summary><c>EACCES</c>.</summary>
    public const int AccessDenied = 13;

    /// <summary><c>ENOTDIR</c>.</summary>
    public const int NotADirectory = 20;

    /// <summary><c>ENAMETOOLONG</c>.</summary>
    public const int NameTooLong = 63;

    /// <summary>The <c>errno</c> values that indicate an unreachable or failing network share.</summary>
    private static readonly HashSet<int> NetworkErrors =
    [
        50, // ENETDOWN
        51, // ENETUNREACH
        57, // ENOTCONN
        60, // ETIMEDOUT
        64, // EHOSTDOWN
        65, // EHOSTUNREACH
        70, // ESTALE
        80, // EAUTH
        81, // ENEEDAUTH
    ];

    /// <summary>
    /// Creates the exception for a failed call.
    /// </summary>
    /// <param name="errno">The error number.</param>
    /// <param name="path">The path involved.</param>
    /// <returns>The exception to throw.</returns>
    public static Exception ToException(int errno, string path)
    {
        var message = $"{Marshal.GetPInvokeErrorMessage(errno)}: '{path}'";
        return errno switch
        {
            NotPermitted or AccessDenied => new UnauthorizedAccessException(message),
            NoSuchEntry or NotADirectory => new DirectoryNotFoundException(message),
            NameTooLong => new PathTooLongException(message),
            _ when NetworkErrors.Contains(errno) => new FileSystemException(message, ScanErrorKind.Network, errno),
            _ => new FileSystemException(message, ScanErrorKind.IO, errno),
        };
    }
}
