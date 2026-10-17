using WhatDisk.Platform;

namespace WhatDisk.Tests.Platform;

/// <summary>
/// Tests the path helpers' handling of Windows paths. The string helpers run everywhere; normalisation, which uses
/// the platform's path rules, runs on Windows only.
/// </summary>
public sealed class PathUtilitiesWindowsTests
{
    /// <summary>
    /// Drive letters are upper-cased; other paths are unchanged.
    /// </summary>
    [Fact]
    public void UpperCaseDriveLetter_UpperCasesOnlyTheDrive()
    {
        Assert.Equal(@"C:\users\Me", PathUtilities.UpperCaseDriveLetter(@"c:\users\Me"));
        Assert.Equal(@"D:\", PathUtilities.UpperCaseDriveLetter(@"D:\"));
        Assert.Equal(@"\\server\share", PathUtilities.UpperCaseDriveLetter(@"\\server\share"));
        Assert.Equal("/home/me", PathUtilities.UpperCaseDriveLetter("/home/me"));
    }

    /// <summary>
    /// Network paths are recognised; device paths and local paths are not.
    /// </summary>
    [Fact]
    public void IsNetworkPath_RecognisesShares()
    {
        Assert.True(PathUtilities.IsNetworkPath(@"\\nas\media"));
        Assert.True(PathUtilities.IsNetworkPath(@"\\?\UNC\nas\media"));
        Assert.False(PathUtilities.IsNetworkPath(@"\\?\C:\Users"));
        Assert.False(PathUtilities.IsNetworkPath(@"\\.\PhysicalDrive0"));
        Assert.False(PathUtilities.IsNetworkPath(@"C:\Users"));
        Assert.False(PathUtilities.IsNetworkPath("/Volumes/Backup"));
    }

    /// <summary>
    /// On Windows, typed and pasted paths are normalised as File Explorer's address bar would: quotes from "Copy as
    /// path" are removed, environment variables are expanded, a bare drive letter means its root, and the drive letter
    /// is upper-cased.
    /// </summary>
    /// <param name="typed">What the user typed or pasted.</param>
    /// <param name="expected">The normalised path.</param>
    [Theory]
    [InlineData("\"C:\\Program Files\"", @"C:\Program Files")]
    [InlineData("d:", @"D:\")]
    [InlineData("D:\"", @"D:\")]
    [InlineData(@"c:\Windows\..\Users\", @"C:\Users")]
    [InlineData(@"\\nas\media\", @"\\nas\media")]
    public void Normalize_HandlesWindowsInput(string typed, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path rules apply on Windows only.");

        Assert.Equal(expected, PathUtilities.Normalize(typed));
    }

    /// <summary>
    /// On Windows, environment variables in typed paths are expanded.
    /// </summary>
    [Fact]
    public void Normalize_ExpandsEnvironmentVariables()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path rules apply on Windows only.");

        Assert.Equal(
            PathUtilities.TrimTrailingSeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
            PathUtilities.Normalize("%SystemRoot%"),
            StringComparer.OrdinalIgnoreCase);
    }
}
