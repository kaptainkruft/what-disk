using System.Diagnostics;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// A temporary directory for integration tests, deleted (with retries, for files briefly held by antivirus or the
/// search indexer) when disposed.
/// </summary>
internal sealed class TempTree : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TempTree"/> class.
    /// </summary>
    public TempTree() => Root = Directory.CreateTempSubdirectory("whatdisk-win-").FullName;

    /// <summary>
    /// Gets the directory's path.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the absolute path of an item in the directory.
    /// </summary>
    /// <param name="relative">The relative path, with forward or back slashes.</param>
    /// <returns>The absolute path.</returns>
    public string PathOf(string relative) => Path.GetFullPath(Path.Join(Root, relative));

    /// <summary>
    /// Writes a file, creating its folders.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="bytes">The file's length.</param>
    /// <returns>The file's absolute path.</returns>
    public string Write(string relative, int bytes)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>
    /// Creates a junction (which needs no special rights) with <c>mklink /J</c>.
    /// </summary>
    /// <param name="relative">The junction's relative path.</param>
    /// <param name="target">The folder it points to.</param>
    /// <returns>The junction's absolute path.</returns>
    public string Junction(string relative, string target)
    {
        var path = PathOf(relative);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", path, target])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && Directory.Exists(path), $"mklink /J failed: {process.StandardError.ReadToEnd()}");
        return path;
    }

    /// <summary>
    /// Creates a hard link (which needs no special rights) with <c>mklink /H</c>.
    /// </summary>
    /// <param name="relative">The link's relative path.</param>
    /// <param name="target">The file it links to.</param>
    /// <returns>The link's absolute path.</returns>
    public string HardLink(string relative, string target)
    {
        var path = PathOf(relative);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/H", path, target])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && File.Exists(path), $"mklink /H failed: {process.StandardError.ReadToEnd()}");
        return path;
    }

    /// <summary>
    /// Deletes the directory.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    foreach (var file in Directory.EnumerateFiles(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (attempt < 5 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
