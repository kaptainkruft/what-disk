using WhatDisk.Duplicates;

namespace WhatDisk.Tests.Duplicates;

/// <summary>
/// Tests the CSV written for duplicate sets.
/// </summary>
public sealed class DuplicateCsvTests
{
    /// <summary>
    /// The CSV has a header row, then one row per file with its set, path, exact length, hash and how it was compared,
    /// each ending CR LF; sampled files have no hash.
    /// </summary>
    [Fact]
    public void Format_WritesAHeaderAndARowPerFile()
    {
        DuplicateSet[] sets =
        [
            new(1, 1_234_567, null, ["/data/a.bin", "/data/b.bin"], DuplicateComparison.Samples),
            new(2, 10, "ffeeddccbbaa99887766554433221100", [@"C:\Users\me\x.txt", @"D:\y.txt"], DuplicateComparison.Hash),
        ];

        var csv = DuplicateCsv.Format(sets);

        Assert.Equal(
            "SetID,Full Path,Size,Hash,Comparison\r\n"
            + "1,/data/a.bin,1234567,,Sampled\r\n"
            + "1,/data/b.bin,1234567,,Sampled\r\n"
            + "2,C:\\Users\\me\\x.txt,10,ffeeddccbbaa99887766554433221100,Hash\r\n"
            + "2,D:\\y.txt,10,ffeeddccbbaa99887766554433221100,Hash\r\n",
            csv);
    }

    /// <summary>
    /// With no sets, the CSV is the header row alone.
    /// </summary>
    [Fact]
    public void Format_WithNoSets_IsTheHeaderAlone() => Assert.Equal("SetID,Full Path,Size,Hash,Comparison\r\n", DuplicateCsv.Format([]));

    /// <summary>
    /// Paths with commas, quotes, line breaks or surrounding spaces are quoted, with quotes doubled; others are not.
    /// </summary>
    /// <param name="field">The path.</param>
    /// <param name="expected">How it appears in the CSV.</param>
    [Theory]
    [InlineData("/plain/path.txt", "/plain/path.txt")]
    [InlineData("/Photos, 2024/a.jpg", "\"/Photos, 2024/a.jpg\"")]
    [InlineData("/say \"hi\".txt", "\"/say \"\"hi\"\".txt\"")]
    [InlineData("/line\nbreak", "\"/line\nbreak\"")]
    [InlineData("/ends with space ", "\"/ends with space \"")]
    [InlineData("/Ünïcødé/日本.txt", "/Ünïcødé/日本.txt")]
    public void Escape_QuotesOnlyWhenNeeded(string field, string expected) => Assert.Equal(expected, DuplicateCsv.Escape(field));
}
