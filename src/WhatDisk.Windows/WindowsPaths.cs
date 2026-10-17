namespace WhatDisk.Windows;

/// <summary>
/// Windows path helpers that do not touch the file system. Pure code, tested on any OS.
/// </summary>
internal static class WindowsPaths
{
    /// <summary>The prefix that lifts the 260-character limit and turns off path normalisation.</summary>
    public const string ExtendedPrefix = @"\\?\";

    /// <summary>The extended prefix of a network path.</summary>
    public const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Converts a full path to its extended-length form, which Win32 calls accept beyond 260 characters and which
    /// keeps names with trailing dots or spaces intact.
    /// </summary>
    /// <param name="path">A full path such as <c>C:\Users</c> or <c>\\server\share\folder</c>.</param>
    /// <returns>The extended path, for example <c>\\?\C:\Users</c> or <c>\\?\UNC\server\share\folder</c>.</returns>
    public static string ToExtended(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return ExtendedUncPrefix + normalized[2..];
        }

        // "C:" alone is the drive's root here.
        return normalized is [_, ':'] ? ExtendedPrefix + normalized + "\\" : ExtendedPrefix + normalized;
    }

    /// <summary>
    /// Removes the extended-length prefix from a path returned by Windows.
    /// </summary>
    /// <param name="path">The path, for example <c>\\?\C:\Users</c> or <c>\\?\UNC\server\share</c>.</param>
    /// <returns>The ordinary path, for example <c>C:\Users</c> or <c>\\server\share</c>.</returns>
    public static string FromExtended(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[ExtendedUncPrefix.Length..];
        }

        return path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) && path.Length > ExtendedPrefix.Length + 1 && path[ExtendedPrefix.Length + 1] == ':'
            ? path[ExtendedPrefix.Length..]
            : path;
    }

    /// <summary>
    /// Determines whether a path is the root of a drive letter, such as <c>C:\</c>.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a drive root.</returns>
    public static bool IsDriveRoot(string path) =>
        path is [_, ':', '\\' or '/'] or [_, ':'] && char.IsAsciiLetter(path[0]);

    /// <summary>
    /// Adds a trailing backslash, as volume functions require.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path ending with a backslash.</returns>
    public static string WithTrailingSeparator(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith('\\') ? path : path + "\\";
    }

    /// <summary>
    /// Adds the trailing backslash a drive root needs (<c>C:</c> alone means the drive's current directory).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path, with a backslash after a bare drive letter.</returns>
    public static string WithTrailingSeparatorIfRoot(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path is [_, ':'] ? path + "\\" : path;
    }

    /// <summary>
    /// Splits a list of strings each ending with a null character, with an empty string at the end, as returned by
    /// <c>GetLogicalDriveStrings</c> and <c>GetVolumePathNamesForVolumeName</c>.
    /// </summary>
    /// <param name="buffer">The characters.</param>
    /// <returns>The strings.</returns>
    public static List<string> SplitMultiString(ReadOnlySpan<char> buffer)
    {
        var result = new List<string>();
        while (buffer.Length > 0)
        {
            var end = buffer.IndexOf('\0');
            if (end <= 0)
            {
                break;
            }

            result.Add(buffer[..end].ToString());
            buffer = buffer[(end + 1)..];
        }

        return result;
    }
}
