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
            IOException => ScanErrorKind.IO,
            _ => ScanErrorKind.Unknown,
        };

        return new NodeError(kind, exception.Message);
    }
}
