using System.Globalization;
using WhatDisk.Formatting;

namespace WhatDisk.Tests.Formatting;

/// <summary>
/// Tests percentage, count and duration formatting.
/// </summary>
public sealed class DisplayFormatTests
{
    /// <summary>
    /// Percentages have one decimal place and tiny values are shown as below 0.1%.
    /// </summary>
    /// <param name="fraction">The fraction.</param>
    /// <param name="expected">The expected text.</param>
    [Theory]
    [InlineData(0, "0.0%")]
    [InlineData(double.NaN, "0.0%")]
    [InlineData(0.0001, "<0.1%")]
    [InlineData(0.123, "12.3%")]
    [InlineData(1, "100.0%")]
    [InlineData(1.5, "100.0%")]
    public void Percent_FormatsFractions(double fraction, string expected) =>
        Assert.Equal(expected, DisplayFormat.Percent(fraction, CultureInfo.InvariantCulture));

    /// <summary>
    /// Durations switch to hours when needed.
    /// </summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <param name="expected">The expected text.</param>
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3661, "1:01:01")]
    public void Duration_FormatsElapsedTime(int seconds, string expected) =>
        Assert.Equal(expected, DisplayFormat.Duration(TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// Quantities pluralise their noun.
    /// </summary>
    [Fact]
    public void Quantity_PluralisesTheNoun()
    {
        Assert.Equal("1 file", DisplayFormat.Quantity(1, "file", "files", CultureInfo.InvariantCulture));
        Assert.Equal("2,000 files", DisplayFormat.Quantity(2000, "file", "files", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Unknown timestamps are shown as a dash.
    /// </summary>
    [Fact]
    public void Timestamp_ShowsADashWhenUnknown()
    {
        Assert.Equal("—", DisplayFormat.Timestamp(null));
        Assert.NotEqual("—", DisplayFormat.Timestamp(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CultureInfo.InvariantCulture));
    }
}
