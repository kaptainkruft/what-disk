using WhatDisk.Scanning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests the icon pixel, clipboard and error helpers (pure code; runs on any OS).
/// </summary>
public sealed class PureHelpersTests
{
    /// <summary>
    /// Icons with an alpha channel are premultiplied; icons without one take their transparency from the mask.
    /// </summary>
    [Fact]
    public void IconPixels_PremultipliesAndAppliesTheMask()
    {
        byte[] withAlpha = [200, 100, 50, 128, 10, 20, 30, 255, 99, 99, 99, 0];
        IconPixels.Finish(withAlpha, []);
        Assert.Equal([100, 50, 25, 128, 10, 20, 30, 255, 0, 0, 0, 0], withAlpha);

        byte[] withoutAlpha = [10, 20, 30, 0, 40, 50, 60, 0];
        byte[] mask = [0, 0, 0, 0, 255, 255, 255, 0];
        IconPixels.Finish(withoutAlpha, mask);
        Assert.Equal([10, 20, 30, 255, 0, 0, 0, 0], withoutAlpha);
    }

    /// <summary>
    /// A file list for the clipboard has the <c>DROPFILES</c> header with wide characters, then the paths, and reads
    /// back unchanged.
    /// </summary>
    [Fact]
    public void ClipboardFormats_BuildsFileListsAndText()
    {
        string[] paths = [@"C:\Users\me\report.pdf", @"D:\Fotos\été"];

        var block = ClipboardFormats.BuildFileDrop(paths);

        Assert.Equal(ClipboardFormats.DropFilesHeaderSize, BitConverter.ToInt32(block, 0));
        Assert.Equal(1, BitConverter.ToInt32(block, 16));
        Assert.Equal(ClipboardFormats.DropFilesHeaderSize + ((paths[0].Length + paths[1].Length + 3) * 2), block.Length);
        Assert.Equal(paths, ClipboardFormats.ReadFileDrop(block));
        Assert.Equal("a\r\nb\0", System.Text.Encoding.Unicode.GetString(ClipboardFormats.BuildText("a\r\nb")));
    }

    /// <summary>
    /// Win32 errors map to scan error kinds, and "not supported" errors trigger the simpler listing formats.
    /// </summary>
    [Fact]
    public void Win32Errors_ClassifiesErrors()
    {
        Assert.Equal(ScanErrorKind.AccessDenied, Win32Errors.Classify(Win32Errors.AccessDenied));
        Assert.Equal(ScanErrorKind.NotFound, Win32Errors.Classify(Win32Errors.PathNotFound));
        Assert.Equal(ScanErrorKind.NotFound, Win32Errors.Classify(Win32Errors.DirectoryNameInvalid));
        Assert.Equal(ScanErrorKind.PathTooLong, Win32Errors.Classify(Win32Errors.FileNameTooLong));
        Assert.Equal(ScanErrorKind.Network, Win32Errors.Classify(Win32Errors.BadNetworkPath));
        Assert.Equal(ScanErrorKind.Network, Win32Errors.Classify(Win32Errors.NetworkNameDeleted));
        Assert.Equal(ScanErrorKind.IO, Win32Errors.Classify(Win32Errors.SharingViolation));
        Assert.True(Win32Errors.IsUnsupportedRequest(Win32Errors.InvalidParameter));
        Assert.True(Win32Errors.IsUnsupportedRequest(Win32Errors.InvalidLevel));
        Assert.False(Win32Errors.IsUnsupportedRequest(Win32Errors.AccessDenied));

        var exception = Win32Errors.ToException(Win32Errors.AccessDenied, @"C:\secret");
        Assert.Equal(ScanErrorKind.AccessDenied, exception.Kind);
        Assert.Equal(Win32Errors.AccessDenied, exception.NativeErrorCode);
        Assert.Contains(@"C:\secret", exception.Message, StringComparison.Ordinal);
    }
}
