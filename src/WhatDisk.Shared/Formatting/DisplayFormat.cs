using System.Globalization;

namespace WhatDisk.Formatting;

/// <summary>
/// Formats percentages, counts, durations and timestamps for display.
/// </summary>
public static class DisplayFormat
{
    /// <summary>
    /// Formats a fraction as a percentage with one decimal place, showing very small non-zero values as
    /// <c>&lt;0.1%</c> so they are not mistaken for nothing.
    /// </summary>
    /// <param name="fraction">A value between 0 and 1.</param>
    /// <param name="culture">The culture; <see langword="null"/> for the current culture.</param>
    /// <returns>The formatted percentage.</returns>
    public static string Percent(double fraction, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(fraction) || fraction <= 0)
        {
            return string.Create(culture, $"{0:0.0}%");
        }

        if (fraction < 0.0005)
        {
            return string.Create(culture, $"<{0.1:0.0}%");
        }

        var percent = Math.Min(fraction, 1) * 100;
        return string.Create(culture, $"{percent:0.0}%");
    }

    /// <summary>
    /// Formats a count with digit grouping.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="culture">The culture; <see langword="null"/> for the current culture.</param>
    /// <returns>The formatted count.</returns>
    public static string Count(long count, CultureInfo? culture = null) =>
        string.Create(culture ?? CultureInfo.CurrentCulture, $"{count:N0}");

    /// <summary>
    /// Formats a count with a noun that is pluralised when the count is not one, for example <c>3 files</c>.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The noun for a count of one.</param>
    /// <param name="plural">The noun for any other count.</param>
    /// <param name="culture">The culture; <see langword="null"/> for the current culture.</param>
    /// <returns>The formatted phrase.</returns>
    public static string Quantity(long count, string singular, string plural, CultureInfo? culture = null) =>
        string.Concat(Count(count, culture), " ", count == 1 ? singular : plural);

    /// <summary>
    /// Formats an elapsed time as <c>m:ss</c>, or <c>h:mm:ss</c> from one hour upwards.
    /// </summary>
    /// <param name="duration">The elapsed time; negative values are treated as zero.</param>
    /// <returns>The formatted duration.</returns>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}:{duration.Seconds:00}");
    }

    /// <summary>
    /// Formats a UTC timestamp in local time using the culture's short date and time patterns.
    /// </summary>
    /// <param name="utc">The timestamp in UTC, or <see langword="null"/>.</param>
    /// <param name="culture">The culture; <see langword="null"/> for the current culture.</param>
    /// <returns>The formatted timestamp, or an em dash when unknown.</returns>
    public static string Timestamp(DateTime? utc, CultureInfo? culture = null) =>
        utc is { } value ? value.ToLocalTime().ToString("g", culture ?? CultureInfo.CurrentCulture) : "—";
}
