using System.Text;
using System.Text.RegularExpressions;

namespace WhatDisk.Duplicates;

/// <summary>
/// Matches files in a duplicates list against what the user typed in its filter box, ignoring case: plain text
/// matches any path that contains it (as <c>*text*</c> would), and a glob (with <c>*</c>, <c>?</c> or <c>[…]</c>)
/// must match the whole path or the file's name, so <c>*.mov</c>, <c>IMG_*</c> and <c>*/Backup/*</c> all work.
/// </summary>
public static class DuplicateFilter
{
    /// <summary>The characters that make a filter a glob.</summary>
    private static readonly char[] GlobCharacters = ['*', '?', '['];

    /// <summary>
    /// Creates the test for a filter.
    /// </summary>
    /// <param name="text">What the user typed; surrounding spaces are ignored.</param>
    /// <returns>A test of a file's full path, or <see langword="null"/> when the filter is empty and every file matches.</returns>
    public static Func<string, bool>? Create(string? text)
    {
        var term = text?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return null;
        }

        if (term.IndexOfAny(GlobCharacters) < 0)
        {
            return path => path.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        // Non-backtracking matching takes time in proportion to the path, whatever the pattern.
        var glob = new Regex(ToPattern(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        return path => glob.IsMatch(path) || glob.IsMatch(Path.GetFileName(path));
    }

    /// <summary>
    /// Turns a glob into an anchored regular expression: <c>*</c> matches any characters (separators included),
    /// <c>?</c> any one character, and <c>[abc]</c>, <c>[a-z]</c> or <c>[!abc]</c> one character of (or not of) a set.
    /// A <c>[</c> without a closing <c>]</c> is an ordinary character, as is everything else.
    /// </summary>
    /// <param name="glob">The glob.</param>
    /// <returns>The regular expression.</returns>
    internal static string ToPattern(string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);
        var pattern = new StringBuilder("^");
        for (var index = 0; index < glob.Length; index++)
        {
            switch (glob[index])
            {
                case '*':
                    pattern.Append(".*");
                    break;
                case '?':
                    pattern.Append('.');
                    break;
                case '[' when FindClassEnd(glob, index) is var end and > 0:
                    AppendClass(pattern, glob[(index + 1)..end]);
                    index = end;
                    break;
                default:
                    pattern.Append(Regex.Escape(glob[index].ToString()));
                    break;
            }
        }

        return pattern.Append('$').ToString();
    }

    /// <summary>
    /// Finds the <c>]</c> that closes a character set. A <c>]</c> straight after the opening <c>[</c> (or <c>[!</c>)
    /// is a member of the set rather than its end.
    /// </summary>
    /// <param name="glob">The glob.</param>
    /// <param name="start">The position of the <c>[</c>.</param>
    /// <returns>The position of the closing <c>]</c>, or -1 when there is none.</returns>
    private static int FindClassEnd(string glob, int start)
    {
        var position = start + 1;
        if (position < glob.Length && glob[position] is '!' or '^')
        {
            position++;
        }

        if (position < glob.Length && glob[position] == ']')
        {
            position++;
        }

        return glob.IndexOf(']', position);
    }

    /// <summary>
    /// Appends a glob character set as a regular expression character class.
    /// </summary>
    /// <param name="pattern">The regular expression so far.</param>
    /// <param name="members">The set's contents, between the brackets.</param>
    private static void AppendClass(StringBuilder pattern, string members)
    {
        pattern.Append('[');
        if (members.Length > 0 && members[0] is '!' or '^')
        {
            pattern.Append('^');
            members = members[1..];
        }

        foreach (var member in members)
        {
            if (member is '\\' or ']' or '[' or '^')
            {
                pattern.Append('\\');
            }

            pattern.Append(member);
        }

        pattern.Append(']');
    }
}
