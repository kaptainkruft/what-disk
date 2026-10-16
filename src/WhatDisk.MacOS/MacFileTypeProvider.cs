using System.Collections.Concurrent;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS;

/// <summary>
/// The macOS view of file types: packages such as applications count as one item of their own type, and types are
/// described with the system's uniform type identifiers (for example "PDF document").
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacFileTypeProvider : DefaultFileTypeProvider
{
    /// <summary>The descriptions looked up so far, by type key.</summary>
    private readonly ConcurrentDictionary<string, string> _descriptions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string? GetPackageType(DirectoryNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return MacPackages.IsPackageName(directory.Name) ? FileTypes.FromName(directory.Name) : null;
    }

    /// <inheritdoc/>
    public override string Describe(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _descriptions.GetOrAdd(type, key => LookUp(key) ?? base.Describe(key));
    }

    /// <summary>
    /// Looks up the system's description of an extension.
    /// </summary>
    /// <param name="type">The type key.</param>
    /// <returns>The description, or <see langword="null"/> when the system does not know the extension.</returns>
    private static string? LookUp(string type)
    {
        if (type.Length < 2 || !MacFrameworks.Load())
        {
            return null;
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var utType = ObjectiveC.Send(ObjectiveC.GetClass("UTType"), ObjectiveC.Selector("typeWithFilenameExtension:"), ObjectiveC.CreateString(type[1..]));
            if (utType == 0)
            {
                return null;
            }

            var identifier = ObjectiveC.ToManagedString(ObjectiveC.Send(utType, ObjectiveC.Selector("identifier")));
            if (identifier is null || identifier.StartsWith("dyn.", StringComparison.Ordinal))
            {
                // A dynamic type: the system knows nothing about this extension.
                return null;
            }

            var description = ObjectiveC.ToManagedString(ObjectiveC.Send(utType, ObjectiveC.Selector("localizedDescription")));
            return string.IsNullOrWhiteSpace(description) ? null : char.ToUpperInvariant(description[0]) + description[1..];
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }
}
