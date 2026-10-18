using System.Globalization;
using System.Text;

namespace WhatDisk.Duplicates;

/// <summary>
/// Writes duplicate sets as CSV (RFC 4180): a header row, then one row per file with its set number, full path,
/// length in bytes, hash (empty for sampled files) and how it was compared ("Hash" or "Sampled"). Rows end with
/// CR LF, and fields that contain a comma, quote or line break are quoted. The same text is copied to the clipboard
/// and written to files.
/// </summary>
public static class DuplicateCsv
{
    /// <summary>
    /// The header row.
    /// </summary>
    public const string Header = "SetID,Full Path,Size,Hash,Comparison";

    /// <summary>
    /// The end of each row.
    /// </summary>
    public const string RowEnd = "\r\n";

    /// <summary>
    /// Formats sets as CSV.
    /// </summary>
    /// <param name="sets">The sets.</param>
    /// <returns>The CSV text, header first.</returns>
    public static string Format(IEnumerable<DuplicateSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var builder = new StringBuilder(Header).Append(RowEnd);
        foreach (var set in sets)
        {
            foreach (var path in set.Paths)
            {
                builder.Append(set.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Escape(path)).Append(',')
                    .Append(set.Length.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(set.Hash).Append(',')
                    .Append(ComparisonName(set.Comparison)).Append(RowEnd);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Names how a set was compared, as in the Comparison column: "Hash" or "Sampled".
    /// </summary>
    /// <param name="comparison">How the set was compared.</param>
    /// <returns>The name.</returns>
    public static string ComparisonName(DuplicateComparison comparison) => comparison == DuplicateComparison.Hash ? "Hash" : "Sampled";

    /// <summary>
    /// Quotes a field when it contains a comma, quote, line break, or leading or trailing space, doubling any quotes.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The field as it appears in CSV.</returns>
    internal static string Escape(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 || (field.Length > 0 && (char.IsWhiteSpace(field[0]) || char.IsWhiteSpace(field[^1])))
            ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : field;
}
