using WhatDisk.Duplicates;

namespace WhatDisk.Tests.Duplicates;

/// <summary>
/// Tests the duplicates list's filter: plain text and globs.
/// </summary>
public sealed class DuplicateFilterTests
{
    /// <summary>
    /// An empty filter (or only spaces) lets everything through.
    /// </summary>
    /// <param name="text">The filter.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_MatchesEverything(string? text) => Assert.Null(DuplicateFilter.Create(text));

    /// <summary>
    /// Plain text matches paths containing it anywhere, ignoring case, with no special characters (as <c>*text*</c>).
    /// </summary>
    /// <param name="text">The filter.</param>
    /// <param name="path">The path.</param>
    /// <param name="expected">Whether it matches.</param>
    [Theory]
    [InlineData("holiday", "/Users/me/Movies/Holiday.mov", true)]
    [InlineData(" Movies/Hol ", "/Users/me/Movies/Holiday.mov", true)]
    [InlineData("backup", "/Users/me/Movies/Holiday.mov", false)]
    [InlineData("copy (1)", "/Users/me/Photo copy (1).jpg", true)]
    [InlineData("a+b", "/data/a+b.txt", true)]
    [InlineData("a+b", "/data/aab.txt", false)]
    public void Text_MatchesAnywhere(string text, string path, bool expected) => Assert.Equal(expected, DuplicateFilter.Create(text)!(path));

    /// <summary>
    /// A glob matches the whole path or the file's name, ignoring case: <c>*</c> any characters, <c>?</c> one,
    /// <c>[…]</c> one of a set or range, <c>[!…]</c> one not in it; a lone <c>[</c> is an ordinary character.
    /// </summary>
    /// <param name="glob">The filter.</param>
    /// <param name="path">The path.</param>
    /// <param name="expected">Whether it matches.</param>
    [Theory]
    [InlineData("*.mov", "/Users/me/Movies/Holiday.MOV", true)]
    [InlineData("*.mov", "/Users/me/Movies/Holiday.mov.txt", false)]
    [InlineData("IMG_*", "/Users/me/Pictures/img_0042.heic", true)]
    [InlineData("IMG_*", "/Users/me/IMG_folder/a.heic", false)]
    [InlineData("*/Backup/*", "/Volumes/Backup/2024/Holiday.mov", true)]
    [InlineData("*/Backup/*", "/Volumes/Backups/Holiday.mov", false)]
    [InlineData("IMG_00?2.*", "/p/IMG_0042.HEIC", true)]
    [InlineData("IMG_00?2.*", "/p/IMG_00422.HEIC", false)]
    [InlineData("report[0-9].pdf", "/docs/Report7.pdf", true)]
    [InlineData("report[!0-9].pdf", "/docs/report7.pdf", false)]
    [InlineData("report[!0-9].pdf", "/docs/reportA.pdf", true)]
    [InlineData("*[draft*", "/docs/notes [draft].txt", true)]
    [InlineData("*(1).*", "/docs/notes (1).txt", true)]
    public void Glob_MatchesThePathOrTheName(string glob, string path, bool expected) => Assert.Equal(expected, DuplicateFilter.Create(glob)!(path));
}
