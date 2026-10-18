using System.Globalization;

namespace WhatDisk.Formatting;

/// <summary>
/// Formats byte counts for display with three significant digits, for example <c>1.23 GB</c> or <c>456 MB</c>.
/// </summary>
/// <param name="convention">Whether units are powers of 1000 or 1024.</param>
/// <param name="culture">The culture used for number formatting; <see langword="null"/> for the current culture.</param>
public sealed class ByteSizeFormatter(ByteUnitConvention convention, CultureInfo? culture = null)
{
    /// <summary>The unit labels, from kilobytes upwards.</summary>
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB", "EB"];

    /// <summary>
    /// Gets or sets the convention used to scale values. Changing it changes how every later value is formatted; texts
    /// already formatted are not updated.
    /// </summary>
    public ByteUnitConvention Convention { get; set; } = convention;

    /// <summary>
    /// Gets the culture used for number formatting.
    /// </summary>
    private CultureInfo Culture => culture ?? CultureInfo.CurrentCulture;

    /// <summary>
    /// Formats a byte count compactly, for example <c>12.3 MB</c>.
    /// </summary>
    /// <param name="bytes">The number of bytes; negative values are treated as zero.</param>
    /// <returns>The formatted size.</returns>
    public string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Create(Culture, $"0 bytes");
        }

        var step = Convention == ByteUnitConvention.PowersOf1024 ? 1024d : 1000d;
        if (bytes < step)
        {
            return bytes == 1 ? "1 byte" : string.Create(Culture, $"{bytes} bytes");
        }

        var value = (double)bytes;
        var unit = -1;
        while (value >= step && unit < Units.Length - 1)
        {
            value /= step;
            unit++;
        }

        // Rounding can carry into the next unit (999.96 KB -> 1000 KB), so step up once more if needed.
        var rounded = RoundToThreeSignificantDigits(value);
        if (rounded >= step && unit < Units.Length - 1)
        {
            rounded = RoundToThreeSignificantDigits(rounded / step);
            unit++;
        }

        var format = rounded < 10 ? "0.00" : rounded < 100 ? "0.0" : "0";
        return string.Concat(rounded.ToString(format, Culture), " ", Units[unit]);
    }

    /// <summary>
    /// Formats a byte count exactly, with digit grouping, for example <c>1,234,567 bytes</c>.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The formatted size.</returns>
    public string FormatExact(long bytes) =>
        bytes == 1 ? "1 byte" : string.Create(Culture, $"{Math.Max(0, bytes):N0} bytes");

    /// <summary>
    /// Rounds a value to three significant digits.
    /// </summary>
    /// <param name="value">A value of at least 1.</param>
    /// <returns>The rounded value.</returns>
    private static double RoundToThreeSignificantDigits(double value) => value switch
    {
        < 10 => Math.Round(value, 2, MidpointRounding.AwayFromZero),
        < 100 => Math.Round(value, 1, MidpointRounding.AwayFromZero),
        _ => Math.Round(value, 0, MidpointRounding.AwayFromZero),
    };
}
