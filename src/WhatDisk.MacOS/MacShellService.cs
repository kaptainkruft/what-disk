using System.Diagnostics;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;
using WhatDisk.Platform;

namespace WhatDisk.MacOS;

/// <summary>
/// The Finder's actions on files and folders: the Trash through <c>NSFileManager</c> (so Put Back works), opening with
/// <c>open</c>, revealing through <c>NSWorkspace</c>, Get Info through the Finder, previews through Quick Look and the
/// general pasteboard for copying.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacShellService : IShellService
{
    /// <summary>The pasteboard type for plain text (<c>NSPasteboardTypeString</c>).</summary>
    private const string PlainTextType = "public.utf8-plain-text";

    /// <summary>An AppleScript that opens the Finder's Get Info window for every path passed as an argument.</summary>
    private static readonly string[] GetInfoScript =
    [
        "on run argv",
        "tell application \"Finder\"",
        "activate",
        "repeat with itemPath in argv",
        "open information window of (POSIX file (itemPath as text) as alias)",
        "end repeat",
        "end tell",
        "end run",
    ];

    /// <summary>Guards <see cref="_quickLook"/>.</summary>
    private readonly Lock _quickLookGate = new();

    /// <summary>The Quick Look preview process, while it is open.</summary>
    private Process? _quickLook;

    /// <inheritdoc/>
    public ShellCapabilities Capabilities =>
        ShellCapabilities.Trash | ShellCapabilities.Open | ShellCapabilities.OpenWith | ShellCapabilities.Reveal
        | ShellCapabilities.ShowInfo | ShellCapabilities.QuickLook | ShellCapabilities.CopyItems | ShellCapabilities.CopyText;

    /// <inheritdoc/>
    public Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Task.Run(() => MoveToTrash(path), cancellationToken);
    }

    /// <inheritdoc/>
    public void Open(IReadOnlyList<string> paths) => Launch("/usr/bin/open", [.. paths]);

    /// <inheritdoc/>
    public IReadOnlyList<ApplicationChoice> GetApplications(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!MacFrameworks.Load())
        {
            return [];
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var workspace = ObjectiveC.Send(ObjectiveC.GetClass("NSWorkspace"), ObjectiveC.Selector("sharedWorkspace"));
            var url = ObjectiveC.CreateFileUrl(path);
            var defaultPath = UrlPath(ObjectiveC.Send(workspace, ObjectiveC.Selector("URLForApplicationToOpenURL:"), url));
            var urls = ObjectiveC.Send(workspace, ObjectiveC.Selector("URLsForApplicationsToOpenURL:"), url);
            var count = urls == 0 ? 0 : (int)ObjectiveC.SendForUnsigned(urls, ObjectiveC.Selector("count"));
            var fileManager = ObjectiveC.Send(ObjectiveC.GetClass("NSFileManager"), ObjectiveC.Selector("defaultManager"));
            var choices = new List<ApplicationChoice>(count);
            for (var index = 0; index < count; index++)
            {
                if (UrlPath(ObjectiveC.Send(urls, ObjectiveC.Selector("objectAtIndex:"), (nuint)index)) is not { } appPath
                    || choices.Exists(choice => choice.Path == appPath))
                {
                    continue;
                }

                var name = ObjectiveC.ToManagedString(ObjectiveC.Send(fileManager, ObjectiveC.Selector("displayNameAtPath:"), ObjectiveC.CreateString(appPath)))
                    ?? Path.GetFileNameWithoutExtension(appPath);
                if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^4];
                }

                choices.Add(new ApplicationChoice(name, appPath, appPath == defaultPath));
            }

            return [.. choices.OrderByDescending(static choice => choice.IsDefault).ThenBy(static choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }

    /// <inheritdoc/>
    public void OpenWith(IReadOnlyList<string> paths, ApplicationChoice application)
    {
        ArgumentNullException.ThrowIfNull(application);
        Launch("/usr/bin/open", ["-a", application.Path, .. paths]);
    }

    /// <inheritdoc/>
    public void Reveal(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!MacFrameworks.Load())
        {
            throw new NotSupportedException("The Finder is not available.");
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var workspace = ObjectiveC.Send(ObjectiveC.GetClass("NSWorkspace"), ObjectiveC.Selector("sharedWorkspace"));
            _ = ObjectiveC.Send(workspace, ObjectiveC.Selector("activateFileViewerSelectingURLs:"), ObjectiveC.CreateFileUrlArray(paths));
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }

    /// <inheritdoc/>
    public void ShowInfo(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Launch("/usr/bin/osascript", [.. GetInfoScript.SelectMany(static line => new[] { "-e", line }), .. paths]);
    }

    /// <inheritdoc/>
    public void QuickLook(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_quickLookGate)
        {
            if (_quickLook is { HasExited: false } open)
            {
                open.Kill();
                open.Dispose();
                _quickLook = null;
                return;
            }

            _quickLook?.Dispose();
            _quickLook = Launch("/usr/bin/qlmanage", ["-p", .. paths], keep: true);
        }
    }

    /// <inheritdoc/>
    public void CopyItems(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        WithPasteboard(pasteboard => ObjectiveC.Send(pasteboard, ObjectiveC.Selector("writeObjects:"), ObjectiveC.CreateFileUrlArray(paths)) != 0);
    }

    /// <inheritdoc/>
    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WithPasteboard(pasteboard => ObjectiveC.SendForBool(pasteboard, ObjectiveC.Selector("setString:forType:"), ObjectiveC.CreateString(text), ObjectiveC.CreateString(PlainTextType)) != 0);
    }

    /// <summary>
    /// Not offered: macOS has no public way for another app to show the Finder's context menu for an item.
    /// </summary>
    /// <param name="paths">The items.</param>
    /// <param name="anchor">Where the menu would open.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public void ShowSystemMenu(IReadOnlyList<string> paths, SystemMenuAnchor anchor) =>
        throw new NotSupportedException("The Finder's context menu is not available to other apps.");

    /// <summary>
    /// Moves an item to the Trash with <c>-[NSFileManager trashItemAtURL:resultingItemURL:error:]</c>.
    /// </summary>
    /// <param name="path">The item.</param>
    private static void MoveToTrash(string path)
    {
        if (!MacFrameworks.Load())
        {
            throw new NotSupportedException("The Trash is not available.");
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var fileManager = ObjectiveC.Send(ObjectiveC.GetClass("NSFileManager"), ObjectiveC.Selector("defaultManager"));
            nint resulting = 0, error = 0;
            var moved = ObjectiveC.SendForBool(fileManager, ObjectiveC.Selector("trashItemAtURL:resultingItemURL:error:"), ObjectiveC.CreateFileUrl(path), &resulting, &error);
            if (moved == 0)
            {
                var reason = error == 0 ? null : ObjectiveC.ToManagedString(ObjectiveC.Send(error, ObjectiveC.Selector("localizedDescription")));
                throw new IOException(reason ?? $"'{path}' could not be moved to the Trash.");
            }
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }

    /// <summary>
    /// Gets the file system path of an <c>NSURL</c>.
    /// </summary>
    /// <param name="url">The URL, or zero.</param>
    /// <returns>The path, or <see langword="null"/>.</returns>
    private static string? UrlPath(nint url) => url == 0 ? null : ObjectiveC.ToManagedString(ObjectiveC.Send(url, ObjectiveC.Selector("path")));

    /// <summary>
    /// Clears the general pasteboard and writes to it.
    /// </summary>
    /// <param name="write">Writes the content; returns whether it succeeded.</param>
    private static void WithPasteboard(Func<nint, bool> write)
    {
        if (!MacFrameworks.Load())
        {
            throw new NotSupportedException("The pasteboard is not available.");
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var pasteboard = ObjectiveC.Send(ObjectiveC.GetClass("NSPasteboard"), ObjectiveC.Selector("generalPasteboard"));
            _ = ObjectiveC.Send(pasteboard, ObjectiveC.Selector("clearContents"));
            if (!write(pasteboard))
            {
                throw new IOException("The clipboard could not be written.");
            }
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }

    /// <summary>
    /// Starts a helper program without waiting for it.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="keep">Whether to return the process instead of releasing it.</param>
    /// <returns>The process when <paramref name="keep"/> is set; otherwise <see langword="null"/>.</returns>
    private static Process? Launch(string program, IReadOnlyList<string> arguments, bool keep = false)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new IOException($"{program} could not be started.");
        process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        if (keep)
        {
            return process;
        }

        process.Dispose();
        return null;
    }
}
