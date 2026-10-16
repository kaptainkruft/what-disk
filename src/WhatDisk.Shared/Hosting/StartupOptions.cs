namespace WhatDisk.Hosting;

/// <summary>
/// Options given on the command line: <c>WhatDisk [path] [--exit-when-done]</c>.
/// </summary>
/// <param name="ScanPath">A path to scan as soon as the window opens, or <see langword="null"/>.</param>
/// <param name="ExitWhenDone">Whether to print the final status and quit when that scan finishes.</param>
public sealed record StartupOptions(string? ScanPath, bool ExitWhenDone)
{
    /// <summary>
    /// The option that makes the application quit after the startup scan.
    /// </summary>
    public const string ExitWhenDoneOption = "--exit-when-done";

    /// <summary>
    /// Gets the options used when nothing is given on the command line.
    /// </summary>
    public static StartupOptions None { get; } = new(null, false);

    /// <summary>
    /// Parses the command-line arguments. Unknown options (such as those macOS adds when launching from the Finder)
    /// are ignored.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    public static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null or { Count: 0 })
        {
            return None;
        }

        var exitWhenDone = args.Contains(ExitWhenDoneOption, StringComparer.Ordinal);
        var path = args.FirstOrDefault(static argument => !argument.StartsWith('-') && !string.IsNullOrWhiteSpace(argument));
        return new StartupOptions(path, exitWhenDone && path is not null);
    }
}
