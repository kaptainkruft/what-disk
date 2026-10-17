using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WhatDisk.Hosting;

/// <summary>
/// The product name, version and release time of the running application, read from its assembly attributes. The
/// release pipeline sets them from the git tag being built (see <c>Directory.Build.props</c>).
/// </summary>
/// <param name="ProductName">The product name.</param>
/// <param name="Version">The semantic version, for example <c>0.1.3-alpha</c>.</param>
/// <param name="Commit">The abbreviated commit the build was made from, when known.</param>
/// <param name="BuildTimestamp">When the release was built, in UTC; <see langword="null"/> for development builds.</param>
/// <param name="Copyright">The copyright notice.</param>
public sealed record AppVersionInfo(string ProductName, string Version, string? Commit, DateTimeOffset? BuildTimestamp, string? Copyright)
{
    /// <summary>The number of commit hash characters shown.</summary>
    private const int ShortCommitLength = 7;

    /// <summary>
    /// Gets the version line, for example "Version 0.1.3-alpha".
    /// </summary>
    public string VersionText => $"Version {Version}";

    /// <summary>
    /// Gets the release line: when the build was made, in UTC, or a note that this is a development build.
    /// </summary>
    public string ReleasedText => BuildTimestamp is { } timestamp
        ? string.Create(CultureInfo.InvariantCulture, $"Released {timestamp.UtcDateTime:yyyy-MM-dd HH:mm} UTC")
        : "Development build";

    /// <summary>
    /// Gets the commit line, or an empty string when the commit is unknown.
    /// </summary>
    public string CommitText => Commit is null ? string.Empty : $"Commit {Commit}";

    /// <summary>
    /// Gets a description of the runtime and operating system.
    /// </summary>
    public static string RuntimeText =>
        $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";

    /// <summary>
    /// Reads the version information of an assembly.
    /// </summary>
    /// <param name="assembly">The assembly, normally the application's entry assembly.</param>
    /// <returns>The version information.</returns>
    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Parse(
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(static item => item.Key == "BuildTimestamp")?.Value,
            assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
    }

    /// <summary>
    /// Builds the version information from raw attribute values.
    /// </summary>
    /// <param name="product">The product name.</param>
    /// <param name="informationalVersion">The informational version, such as <c>0.1.3-alpha+0123abc…</c>.</param>
    /// <param name="buildTimestamp">The ISO 8601 build timestamp, if any.</param>
    /// <param name="copyright">The copyright notice.</param>
    /// <returns>The version information.</returns>
    public static AppVersionInfo Parse(string? product, string? informationalVersion, string? buildTimestamp, string? copyright)
    {
        var version = string.IsNullOrWhiteSpace(informationalVersion) ? "0.0.0-dev" : informationalVersion.Trim();
        string? commit = null;
        if (version.IndexOf('+') is var plus and >= 0)
        {
            var metadata = version[(plus + 1)..];
            version = version[..plus];
            if (metadata.Length > 0)
            {
                commit = metadata.Length > ShortCommitLength ? metadata[..ShortCommitLength] : metadata;
            }
        }

        DateTimeOffset? timestamp = DateTimeOffset.TryParse(buildTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
        return new AppVersionInfo(string.IsNullOrWhiteSpace(product) ? "WhatDisk" : product, version, commit, timestamp, copyright);
    }
}
