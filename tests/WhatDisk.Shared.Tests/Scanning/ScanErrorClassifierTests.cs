using WhatDisk.Scanning;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the classification of directory read failures.
/// </summary>
public sealed class ScanErrorClassifierTests
{
    /// <summary>
    /// Each exception type maps to the expected error kind and keeps its message.
    /// </summary>
    [Fact]
    public void Classify_MapsExceptionTypesToKinds()
    {
        Assert.Equal(ScanErrorKind.AccessDenied, ScanErrorClassifier.Classify(new UnauthorizedAccessException()).Kind);
        Assert.Equal(ScanErrorKind.NotFound, ScanErrorClassifier.Classify(new DirectoryNotFoundException()).Kind);
        Assert.Equal(ScanErrorKind.PathTooLong, ScanErrorClassifier.Classify(new PathTooLongException()).Kind);
        Assert.Equal(ScanErrorKind.IO, ScanErrorClassifier.Classify(new IOException()).Kind);
        Assert.Equal(ScanErrorKind.Network, ScanErrorClassifier.Classify(new FileSystemException("Host is down", ScanErrorKind.Network, 64)).Kind);
        Assert.Equal(ScanErrorKind.Unknown, ScanErrorClassifier.Classify(new InvalidOperationException("odd")).Kind);
        Assert.Equal("odd", ScanErrorClassifier.Classify(new InvalidOperationException("odd")).Message);
    }

    /// <summary>
    /// A classified file system exception keeps its native error code.
    /// </summary>
    [Fact]
    public void FileSystemException_KeepsTheNativeErrorCode()
    {
        var exception = new FileSystemException("Stale handle", ScanErrorKind.Network, 70);

        Assert.Equal(70, exception.NativeErrorCode);
        Assert.Equal(ScanErrorKind.IO, new FileSystemException("x").Kind);
    }
}
