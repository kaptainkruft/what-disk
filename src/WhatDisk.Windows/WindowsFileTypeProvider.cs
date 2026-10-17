using System.Collections.Concurrent;
using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// Describes file types the way File Explorer does ("Text Document", "PNG File"), from the type names registered
/// with Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsFileTypeProvider : DefaultFileTypeProvider
{
    /// <summary>The attributes assumed for files looked up by type.</summary>
    private const uint NormalAttribute = 0x80;

    /// <summary>Descriptions already looked up.</summary>
    private readonly ConcurrentDictionary<string, string> _descriptions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string Describe(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _descriptions.GetOrAdd(type, key => LookUp(key) ?? base.Describe(key));
    }

    /// <summary>
    /// Asks the shell for a type's name.
    /// </summary>
    /// <param name="type">The type key (the lower-case extension).</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    private static string? LookUp(string type)
    {
        if (type.Length < 2)
        {
            return null;
        }

        using var apartment = ComApartment.Enter();
        var info = default(Shell32.FileInfo);
        if (Shell32.SHGetFileInfo("file" + type, NormalAttribute, ref info, (uint)sizeof(Shell32.FileInfo), Shell32.InfoTypeName | Shell32.InfoUseFileAttributes) == 0)
        {
            return null;
        }

        var name = new string(info.TypeName).Trim();
        return name.Length == 0 ? null : name;
    }
}
