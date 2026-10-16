using System.Globalization;
using WhatDisk.Formatting;

namespace WhatDisk.Tests.Formatting;

/// <summary>
/// Tests byte size formatting.
/// </summary>
public sealed class ByteSizeFormatterTests
{
    /// <summary>
    /// Decimal sizes use powers of 1000 and three significant digits.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <param name="expected">The expected text.</param>
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(-3, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(999, "999 bytes")]
    [InlineData(1000, "1.00 KB")]
    [InlineData(1234, "1.23 KB")]
    [InlineData(12_345, "12.3 KB")]
    [InlineData(123_456, "123 KB")]
    [InlineData(999_999, "1.00 MB")]
    [InlineData(1_500_000_000, "1.50 GB")]
    [InlineData(2_000_000_000_000, "2.00 TB")]
    public void Format_UsesDecimalUnits(long bytes, string expected) =>
        Assert.Equal(expected, new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture).Format(bytes));

    /// <summary>
    /// Binary sizes use powers of 1024.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <param name="expected">The expected text.</param>
    [Theory]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1_048_576, "1.00 MB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    public void Format_UsesBinaryUnits(long bytes, string expected) =>
        Assert.Equal(expected, new ByteSizeFormatter(ByteUnitConvention.PowersOf1024, CultureInfo.InvariantCulture).Format(bytes));

    /// <summary>
    /// Formatting honours the culture's decimal separator.
    /// </summary>
    [Fact]
    public void Format_UsesTheCulturesDecimalSeparator() =>
        Assert.Equal("1,50 KB", new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.GetCultureInfo("de-DE")).Format(1500));

    /// <summary>
    /// Exact sizes group digits.
    /// </summary>
    [Fact]
    public void FormatExact_GroupsDigits()
    {
        var formatter = new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture);

        Assert.Equal("1,234,567 bytes", formatter.FormatExact(1_234_567));
        Assert.Equal("1 byte", formatter.FormatExact(1));
    }
}
