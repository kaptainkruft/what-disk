namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests the Windows path helpers (pure code; runs on any OS).
/// </summary>
public sealed class WindowsPathsTests
{
    /// <summary>
    /// Local and network paths gain the extended-length prefix; paths that already have a device prefix keep it.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="expected">The extended path.</param>
    [Theory]
    [InlineData(@"C:\Users\me", @"\\?\C:\Users\me")]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData("C:", @"\\?\C:\")]
    [InlineData("D:/Media/Films", @"\\?\D:\Media\Films")]
    [InlineData(@"\\server\share\folder", @"\\?\UNC\server\share\folder")]
    [InlineData(@"\\?\C:\already", @"\\?\C:\already")]
    [InlineData(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\", @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\")]
    [InlineData(@"\\.\PhysicalDrive0", @"\\.\PhysicalDrive0")]
    public void ToExtended_AddsThePrefix(string path, string expected) => Assert.Equal(expected, WindowsPaths.ToExtended(path));

    /// <summary>
    /// Paths returned by Windows lose the extended-length prefix; volume GUID paths keep it, having no other form.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="expected">The ordinary path.</param>
    [Theory]
    [InlineData(@"\\?\C:\Users", @"C:\Users")]
    [InlineData(@"\\?\UNC\server\share", @"\\server\share")]
    [InlineData(@"C:\plain", @"C:\plain")]
    [InlineData(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\", @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\")]
    public void FromExtended_RemovesThePrefix(string path, string expected) => Assert.Equal(expected, WindowsPaths.FromExtended(path));

    /// <summary>
    /// Only a drive letter with an optional separator is a drive root.
    /// </summary>
    [Fact]
    public void IsDriveRoot_RecognisesDriveRoots()
    {
        Assert.True(WindowsPaths.IsDriveRoot(@"C:\"));
        Assert.True(WindowsPaths.IsDriveRoot("z:/"));
        Assert.True(WindowsPaths.IsDriveRoot("D:"));
        Assert.False(WindowsPaths.IsDriveRoot(@"C:\Users"));
        Assert.False(WindowsPaths.IsDriveRoot(@"\\server\share"));
        Assert.False(WindowsPaths.IsDriveRoot(@"1:\"));
    }

    /// <summary>
    /// A bare drive letter gains its root separator; other paths are unchanged.
    /// </summary>
    [Fact]
    public void Separators_AreAddedWhereVolumeFunctionsNeedThem()
    {
        Assert.Equal(@"C:\", WindowsPaths.WithTrailingSeparatorIfRoot("C:"));
        Assert.Equal(@"C:\x", WindowsPaths.WithTrailingSeparatorIfRoot(@"C:\x"));
        Assert.Equal(@"C:\mnt\backup\", WindowsPaths.WithTrailingSeparator(@"C:\mnt\backup"));
        Assert.Equal(@"C:\", WindowsPaths.WithTrailingSeparator(@"C:\"));
    }

    /// <summary>
    /// Lists of null-terminated strings are split, stopping at the empty string that ends them.
    /// </summary>
    [Fact]
    public void SplitMultiString_SplitsAtNulls()
    {
        Assert.Equal([@"C:\", @"D:\", @"Z:\"], WindowsPaths.SplitMultiString("C:\\\0D:\\\0Z:\\\0\0stale\0"));
        Assert.Empty(WindowsPaths.SplitMultiString("\0"));
        Assert.Empty(WindowsPaths.SplitMultiString(string.Empty));
    }
}
