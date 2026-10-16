using WhatDisk.Platform;

namespace WhatDisk.Tests.Platform;

/// <summary>
/// Tests path normalisation and ancestry checks.
/// </summary>
public sealed class PathUtilitiesTests
{
    /// <summary>
    /// Descendant checks respect path segment boundaries.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="root">The candidate ancestor.</param>
    /// <param name="expected">Whether <paramref name="path"/> is the root or beneath it.</param>
    [Theory]
    [InlineData("/home/user", "/home", true)]
    [InlineData("/home", "/home", true)]
    [InlineData("/homeless", "/home", false)]
    [InlineData("/anything", "/", true)]
    [InlineData("/home", "/home/user", false)]
    public void IsSameOrDescendant_RespectsSegmentBoundaries(string path, string root, bool expected)
    {
        if (OperatingSystem.IsWindows())
        {
            path = path.Replace('/', '\\');
            root = root.Replace('/', '\\');
        }

        Assert.Equal(expected, PathUtilities.IsSameOrDescendant(path, root, StringComparison.Ordinal));
    }

    /// <summary>
    /// A path is not a strict descendant of itself.
    /// </summary>
    [Fact]
    public void IsStrictDescendant_ExcludesThePathItself()
    {
        var root = Path.GetTempPath();
        Assert.False(PathUtilities.IsStrictDescendant(PathUtilities.TrimTrailingSeparator(root), PathUtilities.TrimTrailingSeparator(root), StringComparison.Ordinal));
        Assert.True(PathUtilities.IsStrictDescendant(Path.Join(root, "x"), PathUtilities.TrimTrailingSeparator(root), StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalisation removes trailing separators but keeps file system roots intact.
    /// </summary>
    [Fact]
    public void Normalize_TrimsTrailingSeparatorsExceptForRoots()
    {
        var temp = Path.GetTempPath();
        var root = Path.GetPathRoot(temp)!;

        Assert.Equal(Path.TrimEndingDirectorySeparator(temp), PathUtilities.Normalize(temp));
        Assert.Equal(root, PathUtilities.Normalize(root));
        Assert.Equal(Path.Join(Path.TrimEndingDirectorySeparator(temp), "a"), PathUtilities.Normalize(Path.Join(temp, "a", "b", "..")));
    }

    /// <summary>
    /// A leading tilde expands to the home directory on Unix-like systems.
    /// </summary>
    [Fact]
    public void ExpandHomeDirectory_ExpandsALeadingTildeOnUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Tilde expansion only applies to Unix-like systems.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(home + "/Downloads", PathUtilities.ExpandHomeDirectory("~/Downloads"));
        Assert.Equal(home, PathUtilities.ExpandHomeDirectory("~"));
        Assert.Equal("~other/x", PathUtilities.ExpandHomeDirectory("~other/x"));
        Assert.Equal("/abs", PathUtilities.ExpandHomeDirectory("/abs"));
    }

    /// <summary>
    /// Links anywhere in a path are resolved, and paths without links are unchanged.
    /// </summary>
    [Fact]
    public void ResolveLinks_ResolvesLinksInEveryComponent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires extra privileges on Windows.");
        var temp = Directory.CreateTempSubdirectory("whatdisk-links-");
        try
        {
            var real = Directory.CreateDirectory(Path.Join(temp.FullName, "real", "inner"));
            Directory.CreateSymbolicLink(Path.Join(temp.FullName, "alias"), Path.Join(temp.FullName, "real"));
            var expected = PathUtilities.ResolveLinks(real.FullName);

            Assert.Equal(expected, PathUtilities.ResolveLinks(Path.Join(temp.FullName, "alias", "inner")));
            Assert.Equal(expected, PathUtilities.ResolveLinks(expected));
            Assert.Equal(Path.Join(expected, "missing"), PathUtilities.ResolveLinks(Path.Join(temp.FullName, "alias", "inner", "missing")));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
