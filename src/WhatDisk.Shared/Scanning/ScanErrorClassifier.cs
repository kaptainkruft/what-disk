namespace WhatDisk.Scanning;

/// <summary>
/// Converts exceptions raised while reading directories into <see cref="NodeError"/> values.
/// </summary>
public static class ScanErrorClassifier
{
    /// <summary>
    /// Classifies an exception.
    /// </summary>
    /// <param name="exception">The exception raised while reading a directory.</param>
    /// <returns>The classified error.</returns>
    public static NodeError Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var kind = exception switch
        {
            FileSystemException classified => classified.Kind,
            UnauthorizedAccessException or System.Security.SecurityException => ScanErrorKind.AccessDenied,
            DirectoryNotFoundException or FileNotFoundException => ScanErrorKind.NotFound,
            PathTooLongException => ScanErrorKind.PathTooLong,
            IOException io when IsNetworkError(io.HResult) => ScanErrorKind.Network,
            IOException => ScanErrorKind.IO,
            _ => ScanErrorKind.Unknown,
        };

        return new NodeError(kind, exception.Message);
    }

    /// <summary>
    /// Determines whether an HRESULT wraps a Windows network error, such as a share that went away.
    /// </summary>
    /// <param name="result">The HRESULT.</param>
    /// <returns><see langword="true"/> for network errors.</returns>
    private static bool IsNetworkError(int result) =>
        (result & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
        && (result & 0xFFFF) is 51 or 53 or 59 or 64 or 67 or 121 or 1222 or 1231;
}
