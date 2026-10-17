using System.Runtime.InteropServices;
using WhatDisk.Scanning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Win32 error codes used by the Windows head, and their mapping to scan error kinds. Pure code, tested on any OS.
/// </summary>
internal static class Win32Errors
{
    /// <summary>The function is not supported for this item (for example a directory listing class on FAT).</summary>
    public const int InvalidFunction = 1;

    /// <summary>The file was not found.</summary>
    public const int FileNotFound = 2;

    /// <summary>The path was not found.</summary>
    public const int PathNotFound = 3;

    /// <summary>Access was denied.</summary>
    public const int AccessDenied = 5;

    /// <summary>There are no more entries.</summary>
    public const int NoMoreFiles = 18;

    /// <summary>The device is not ready (for example an empty card reader).</summary>
    public const int NotReady = 21;

    /// <summary>The item is in use by another process.</summary>
    public const int SharingViolation = 32;

    /// <summary>The request is not supported.</summary>
    public const int NotSupported = 50;

    /// <summary>The network path was not found.</summary>
    public const int BadNetworkPath = 53;

    /// <summary>An unexpected network error occurred.</summary>
    public const int UnexpectedNetworkError = 59;

    /// <summary>The network name is no longer available.</summary>
    public const int NetworkNameDeleted = 64;

    /// <summary>The network name was not found.</summary>
    public const int BadNetworkName = 67;

    /// <summary>A parameter is incorrect (for example an unsupported information class).</summary>
    public const int InvalidParameter = 87;

    /// <summary>The information level is not supported.</summary>
    public const int InvalidLevel = 124;

    /// <summary>The file name or extension is too long.</summary>
    public const int FileNameTooLong = 206;

    /// <summary>The buffer is too small.</summary>
    public const int MoreData = 234;

    /// <summary>The directory name is invalid (the path is a file).</summary>
    public const int DirectoryNameInvalid = 267;

    /// <summary>The file cannot be accessed by the system (for example a cloud file that cannot be fetched).</summary>
    public const int CannotAccessFile = 1920;

    /// <summary>The network location cannot be reached.</summary>
    public const int NetworkUnreachable = 1231;

    /// <summary>No network provider accepted the path.</summary>
    public const int NoNetwork = 1222;

    /// <summary>The user name or password is incorrect.</summary>
    public const int LogonFailure = 1326;

    /// <summary>
    /// Classifies a Win32 error code.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <returns>The scan error kind.</returns>
    public static ScanErrorKind Classify(int error) => error switch
    {
        AccessDenied or CannotAccessFile or LogonFailure => ScanErrorKind.AccessDenied,
        FileNotFound or PathNotFound or DirectoryNameInvalid => ScanErrorKind.NotFound,
        FileNameTooLong => ScanErrorKind.PathTooLong,
        BadNetworkPath or UnexpectedNetworkError or NetworkNameDeleted or BadNetworkName or NetworkUnreachable or NoNetwork => ScanErrorKind.Network,
        _ => ScanErrorKind.IO,
    };

    /// <summary>
    /// Determines whether an error means the requested directory listing format is not supported, so a simpler one
    /// should be tried.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <returns><see langword="true"/> for "not supported" errors.</returns>
    public static bool IsUnsupportedRequest(int error) => error is InvalidFunction or NotSupported or InvalidParameter or InvalidLevel;

    /// <summary>
    /// Creates the exception for a failed call on a path.
    /// </summary>
    /// <param name="error">The error code.</param>
    /// <param name="path">The path.</param>
    /// <returns>The exception.</returns>
    public static FileSystemException ToException(int error, string path) =>
        new($"{Marshal.GetPInvokeErrorMessage(error)} ({path})", Classify(error), error);
}
