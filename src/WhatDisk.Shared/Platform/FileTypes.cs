namespace WhatDisk.Platform;

/// <summary>
/// Identifies file types by extension. A type key is the lower-case extension including its dot (for example
/// <c>.pdf</c>), or <see cref="NoExtension"/> for names without a usable extension.
/// </summary>
public static class FileTypes
{
    /// <summary>
    /// The type key of files without an extension.
    /// </summary>
    public const string NoExtension = "";

    /// <summary>The longest extension (including the dot) treated as a file type.</summary>
    private const int MaxExtensionLength = 16;

    /// <summary>
    /// Gets the type key of a file name.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The type key.</returns>
    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var extension = Path.GetExtension(name.AsSpan());
        if (extension.Length is <= 1 or > MaxExtensionLength || extension.Length == name.Length)
        {
            // No extension, an over-long one, or a dot-file such as ".profile".
            return NoExtension;
        }

        return extension.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Gets the short label of a type for the chart legend.
    /// </summary>
    /// <param name="type">The type key.</param>
    /// <returns>The extension, or "No extension".</returns>
    public static string Label(string type) => type.Length == 0 ? "No extension" : type;
}
