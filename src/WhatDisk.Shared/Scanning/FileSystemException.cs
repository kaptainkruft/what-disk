namespace WhatDisk.Scanning;

/// <summary>
/// An input/output failure reported by a native file system call, carrying a platform-independent classification.
/// </summary>
public sealed class FileSystemException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemException"/> class.
    /// </summary>
    public FileSystemException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public FileSystemException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public FileSystemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemException"/> class with a classification.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="kind">The classification of the failure.</param>
    /// <param name="nativeErrorCode">The native error code (errno or Win32 error).</param>
    public FileSystemException(string message, ScanErrorKind kind, int nativeErrorCode)
        : base(message)
    {
        Kind = kind;
        NativeErrorCode = nativeErrorCode;
    }

    /// <summary>
    /// Gets the classification of the failure.
    /// </summary>
    public ScanErrorKind Kind { get; } = ScanErrorKind.IO;

    /// <summary>
    /// Gets the native error code (errno or Win32 error) that caused the failure, or zero.
    /// </summary>
    public int NativeErrorCode { get; }
}
