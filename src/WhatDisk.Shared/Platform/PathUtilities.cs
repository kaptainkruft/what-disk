namespace WhatDisk.Platform;

/// <summary>
/// Helpers for normalising and comparing file system paths using the conventions of the current platform.
/// </summary>
public static class PathUtilities
{
    /// <summary>
    /// Normalises a path for scanning and comparison: expands a leading <c>~</c> on Unix-like systems, makes the
    /// path absolute and removes any trailing directory separator unless the path is a root.
    /// </summary>
    /// <param name="path">The path to normalise.</param>
    /// <returns>The normalised, absolute path.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = ExpandHomeDirectory(path.Trim());
        if (OperatingSystem.IsWindows() && candidate is [var drive, ':'] && char.IsAsciiLetter(drive))
        {
            // "C:" on its own means the root of the drive, not its current directory.
            candidate += Path.DirectorySeparatorChar;
        }

        return TrimTrailingSeparator(Path.GetFullPath(candidate));
    }

    /// <summary>
    /// Resolves symbolic links (and junctions) in every component of an absolute path, so that it can be compared with
    /// mount points, which the operating system reports as real paths (for example <c>/tmp</c> is
    /// <c>/private/tmp</c> on macOS). Components that cannot be inspected are left as they are.
    /// </summary>
    /// <param name="path">An absolute, normalised path.</param>
    /// <returns>The path with links resolved.</returns>
    public static string ResolveLinks(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var current = path;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var resolved = ResolveLinksOnce(current);
            if (string.Equals(resolved, current, StringComparison.Ordinal))
            {
                return current;
            }

            current = resolved;
        }

        return current;
    }

    /// <summary>
    /// Removes one trailing directory separator from <paramref name="path"/> unless the path is a root.
    /// </summary>
    /// <param name="path">The path to trim.</param>
    /// <returns>The trimmed path.</returns>
    public static string TrimTrailingSeparator(string path) => Path.TrimEndingDirectorySeparator(path);

    /// <summary>
    /// Determines whether <paramref name="path"/> is equal to <paramref name="root"/> or lies beneath it.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="root">The candidate ancestor.</param>
    /// <param name="comparison">How path characters are compared.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is <paramref name="root"/> or a descendant.</returns>
    public static bool IsSameOrDescendant(string path, string root, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(root);
        if (!path.StartsWith(root, comparison))
        {
            return false;
        }

        return path.Length == root.Length
            || (root.Length > 0 && IsDirectorySeparator(root[^1]))
            || IsDirectorySeparator(path[root.Length]);
    }

    /// <summary>
    /// Determines whether <paramref name="path"/> lies strictly beneath <paramref name="root"/>.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="root">The candidate ancestor.</param>
    /// <param name="comparison">How path characters are compared.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is a descendant of <paramref name="root"/>.</returns>
    public static bool IsStrictDescendant(string path, string root, StringComparison comparison) =>
        path.Length > root.Length && IsSameOrDescendant(path, root, comparison);

    /// <summary>
    /// Determines whether a character separates directories on the current platform.
    /// </summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> for a directory separator.</returns>
    public static bool IsDirectorySeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    /// <summary>
    /// Expands a leading <c>~</c> to the user's home directory on Unix-like systems.
    /// </summary>
    /// <param name="path">The path that may start with <c>~</c>.</param>
    /// <returns>The expanded path, or <paramref name="path"/> unchanged.</returns>
    public static string ExpandHomeDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (OperatingSystem.IsWindows() || path.Length == 0 || path[0] != '~')
        {
            return path;
        }

        if (path.Length > 1 && !IsDirectorySeparator(path[1]))
        {
            // "~other" refers to another user's home directory, which is not expanded.
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? path : string.Concat(home, path.AsSpan(1));
    }

    /// <summary>
    /// Resolves the links found while walking a path from its root once; targets may themselves contain links.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The path with the links found resolved.</returns>
    private static string ResolveLinksOnce(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return path;
        }

        var current = root;
        var segments = path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        try
        {
            foreach (var segment in segments)
            {
                var next = Path.Join(current, segment);
                var info = new DirectoryInfo(next);
                current = info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
                    ? TrimTrailingSeparator(target.FullName)
                    : next;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }

        return TrimTrailingSeparator(current);
    }
}
