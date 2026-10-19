using System.Buffers.Binary;
using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Settings;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// The Windows shell's actions on items: the Recycle Bin, Open With, File Explorer windows, Properties, the clipboard
/// and File Explorer's context menu. Windows has no Quick Look, so previews are not offered.
/// </summary>
/// <param name="settings">
/// The settings, which can turn off the warning before items that cannot be recycled are deleted permanently;
/// <see langword="null"/> for the defaults.
/// </param>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsShellService(AppSettings? settings = null) : DefaultShellService
{
    /// <summary>The name of the clipboard format that tells File Explorer whether a paste copies or moves.</summary>
    private const string PreferredDropEffectFormat = "Preferred DropEffect";

    /// <inheritdoc/>
    public override ShellCapabilities Capabilities =>
        ShellCapabilities.Trash | ShellCapabilities.Open | ShellCapabilities.OpenWith | ShellCapabilities.Reveal
        | ShellCapabilities.ShowInfo | ShellCapabilities.CopyItems | ShellCapabilities.CopyText | ShellCapabilities.SystemMenu;

    /// <summary>
    /// Moves an item to the Recycle Bin. Items that cannot be recycled (on network shares, or larger than the Recycle
    /// Bin) are deleted permanently only after Windows has warned and the user has agreed, unless the settings turn the
    /// warning off.
    /// </summary>
    /// <param name="path">The item.</param>
    /// <param name="cancellationToken">Unused: the shell operation cannot be interrupted.</param>
    /// <returns>A task that completes when the item has been moved.</returns>
    public override Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var flags = RecycleFlags(settings?.Get(SettingsCatalog.Deleting.WarnWhenNotRecyclable) ?? true);
        return StaThread.RunAsync(() =>
        {
            Recycle(path, flags);
            return true;
        });
    }

    /// <inheritdoc/>
    public override IReadOnlyList<ApplicationChoice> GetApplications(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension) || Directory.Exists(path))
        {
            return [];
        }

        using var apartment = ComApartment.Enter();
        var handlers = EnumerateHandlers(extension);
        try
        {
            var defaultExecutable = QueryAssociation(extension, Ole32.AssocExecutable);
            var defaultName = QueryAssociation(extension, Ole32.AssocFriendlyAppName);
            var choices = handlers
                .Where(static handler => !string.IsNullOrWhiteSpace(handler.DisplayName) && !string.IsNullOrEmpty(handler.Name))
                .DistinctBy(static handler => handler.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(handler => new ApplicationChoice(
                    handler.DisplayName!,
                    handler.Name!,
                    string.Equals(handler.Name, defaultExecutable, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(handler.DisplayName, defaultName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var firstDefault = choices.FindIndex(static choice => choice.IsDefault);
            return
            [
                .. choices.Where((choice, index) => index == firstDefault),
                .. choices.Where((choice, index) => index != firstDefault).Select(static choice => choice with { IsDefault = false }),
            ];
        }
        finally
        {
            handlers.ForEach(static handler => ComObject.Release(handler.Instance));
        }
    }

    /// <inheritdoc/>
    public override void OpenWith(IReadOnlyList<string> paths, ApplicationChoice application)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(application);
        if (paths.Count == 0)
        {
            return;
        }

        StaThread.Run(() =>
        {
            var handlers = EnumerateHandlers(Path.GetExtension(paths[0]));
            try
            {
                var handler = handlers.Find(candidate => string.Equals(candidate.Name, application.Path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"{application.Name} can no longer open these items.");
                using var items = new ShellItems(paths);
                var dataObject = items.CreateDataObject();
                try
                {
                    var invoke = (delegate* unmanaged<nint, nint, int>)ComObject.Method(handler.Instance, 8);
                    var result = invoke(handler.Instance, dataObject);
                    if (result < 0)
                    {
                        throw new IOException($"{application.Name} could not open the items (0x{result:X8}).");
                    }
                }
                finally
                {
                    ComObject.Release(dataObject);
                }
            }
            finally
            {
                handlers.ForEach(static handler => ComObject.Release(handler.Instance));
            }
        });
    }

    /// <summary>
    /// Opens a File Explorer window for each folder holding selected items, with those items selected. A drive root
    /// opens This PC with the drive selected.
    /// </summary>
    /// <param name="paths">The items.</param>
    public override void Reveal(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        StaThread.Run(() =>
        {
            foreach (var group in paths.GroupBy(static path => Path.GetDirectoryName(PathUtilities.TrimTrailingSeparator(path)) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0)
                {
                    foreach (var root in group)
                    {
                        using var item = new ShellItems([root]);
                        Check(Shell32.SHOpenFolderAndSelectItems(item.IdLists[0], 0, null, 0), "show", root);
                    }

                    continue;
                }

                using var folder = new ShellItems([group.Key]);
                using var items = new ShellItems([.. group]);
                var children = new nint[items.IdLists.Length];
                for (var index = 0; index < children.Length; index++)
                {
                    children[index] = Shell32.ILFindLastID(items.IdLists[index]);
                }

                fixed (nint* pointer = children)
                {
                    Check(Shell32.SHOpenFolderAndSelectItems(folder.IdLists[0], (uint)children.Length, pointer, 0), "show", group.Key);
                }
            }
        });
    }

    /// <summary>
    /// Opens the Properties window: one for a single item, a combined one for several.
    /// </summary>
    /// <param name="paths">The items.</param>
    public override void ShowInfo(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        StaThread.Run(() =>
        {
            if (paths.Count == 1)
            {
                if (!Shell32.SHObjectProperties(0, Shell32.PropertiesFilePath, paths[0], null))
                {
                    throw new IOException($"Could not show the properties of “{paths[0]}”.");
                }

                return;
            }

            using var items = new ShellItems(paths);
            var dataObject = items.CreateDataObject();
            try
            {
                Check(Shell32.SHMultiFileProperties(dataObject, 0), "show the properties of", $"{paths.Count} items");
            }
            finally
            {
                ComObject.Release(dataObject);
            }
        });
    }

    /// <summary>
    /// Puts the items on the clipboard so that pasting in File Explorer copies them.
    /// </summary>
    /// <param name="paths">The items.</param>
    public override void CopyItems(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var effect = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(effect, ClipboardFormats.DropEffectCopy);
        WindowsClipboard.Set(
        [
            (User32.FileDropFormat, ClipboardFormats.BuildFileDrop(paths)),
            (User32.RegisterClipboardFormat(PreferredDropEffectFormat), effect),
        ]);
    }

    /// <inheritdoc/>
    public override void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WindowsClipboard.Set([(User32.UnicodeTextFormat, ClipboardFormats.BuildText(text))]);
    }

    /// <summary>
    /// Shows File Explorer's context menu for the items (on Windows 11, the one under Show more options) and runs the
    /// command chosen from it.
    /// </summary>
    /// <param name="paths">The items, all in one folder.</param>
    /// <param name="anchor">The window that owns the menu and where it opens.</param>
    public override void ShowSystemMenu(IReadOnlyList<string> paths, SystemMenuAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(anchor);
        WindowsContextMenu.Show(anchor.Window, paths, anchor.ScreenPosition, anchor.ExtendedVerbs);
    }

    /// <summary>
    /// Gets the <c>SHFileOperation</c> flags for moving to the Recycle Bin: allow undo, without the shell's own
    /// confirmation, error and progress windows (WhatDisk asks first itself), and, unless turned off, with the warning
    /// before an item that cannot be recycled is deleted permanently.
    /// </summary>
    /// <param name="warnWhenNotRecyclable">Whether Windows warns before deleting an item it cannot recycle.</param>
    /// <returns>The flags.</returns>
    internal static ushort RecycleFlags(bool warnWhenNotRecyclable) => (ushort)(
        Shell32.FileOperationAllowUndo | Shell32.FileOperationNoConfirmation | Shell32.FileOperationNoErrorUI | Shell32.FileOperationSilent
        | (warnWhenNotRecyclable ? Shell32.FileOperationWantNukeWarning : 0));

    /// <summary>
    /// Deletes an item to the Recycle Bin with <c>SHFileOperation</c>.
    /// </summary>
    /// <param name="path">The item.</param>
    /// <param name="flags">The operation's flags, from <see cref="RecycleFlags"/>.</param>
    private static void Recycle(string path, ushort flags)
    {
        var name = Path.GetFileName(PathUtilities.TrimTrailingSeparator(path));
        var from = path + "\0\0";
        fixed (char* pointer = from)
        {
            var operation = new Shell32.FileOperation
            {
                Function = Shell32.FileOperationDelete,
                From = pointer,
                Flags = flags,
            };
            var result = Shell32.SHFileOperation(ref operation);
            if (operation.AnyOperationsAborted != 0)
            {
                throw new IOException($"Moving “{name}” to the Recycle Bin was cancelled.");
            }

            if (result != 0)
            {
                throw new IOException($"Could not move “{name}” to the Recycle Bin (error 0x{result:X}).");
            }
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"“{name}” is still there after moving it to the Recycle Bin.");
        }
    }

    /// <summary>
    /// Lists the applications recommended for an extension. The caller releases each handler.
    /// </summary>
    /// <param name="extension">The extension, including its dot.</param>
    /// <returns>The handlers.</returns>
    private static List<AssociationHandler> EnumerateHandlers(string extension)
    {
        var handlers = new List<AssociationHandler>();
        if (string.IsNullOrEmpty(extension) || Shell32.SHAssocEnumHandlers(extension, Shell32.AssocFilterRecommended, out var enumerator) < 0 || enumerator == 0)
        {
            return handlers;
        }

        try
        {
            var next = (delegate* unmanaged<nint, uint, nint*, uint*, int>)ComObject.Method(enumerator, 3);
            while (true)
            {
                nint handler = 0;
                uint fetched = 0;
                if (next(enumerator, 1, &handler, &fetched) != 0 || fetched == 0 || handler == 0)
                {
                    break;
                }

                handlers.Add(new AssociationHandler(handler, GetHandlerString(handler, 3), GetHandlerString(handler, 4)));
            }
        }
        finally
        {
            ComObject.Release(enumerator);
        }

        return handlers;
    }

    /// <summary>
    /// Calls an <c>IAssocHandler</c> method that returns a string.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="slot">The method's slot: 3 for <c>GetName</c>, 4 for <c>GetUIName</c>.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static string? GetHandlerString(nint handler, int slot)
    {
        char* value = null;
        var method = (delegate* unmanaged<nint, char**, int>)ComObject.Method(handler, slot);
        return method(handler, &value) >= 0 ? ComObject.TakeString(value) : null;
    }

    /// <summary>
    /// Looks up the default application of an extension.
    /// </summary>
    /// <param name="extension">The extension.</param>
    /// <param name="query">What to look up.</param>
    /// <returns>The answer, or <see langword="null"/>.</returns>
    private static string? QueryAssociation(string extension, int query)
    {
        var buffer = stackalloc char[1024];
        var length = 1024u;
        return Ole32.AssocQueryString(0, query, extension, null, buffer, ref length) == 0 ? new string(buffer) : null;
    }

    /// <summary>
    /// Throws when a shell call failed.
    /// </summary>
    /// <param name="result">The HRESULT.</param>
    /// <param name="action">What was attempted.</param>
    /// <param name="subject">What it was attempted on.</param>
    private static void Check(int result, string action, string subject)
    {
        if (result < 0)
        {
            throw new IOException($"Could not {action} “{subject}” (0x{result:X8}).");
        }
    }

    /// <summary>
    /// An application registered for a file type.
    /// </summary>
    /// <param name="Instance">The <c>IAssocHandler</c>.</param>
    /// <param name="Name">The application's identity, usually its executable's path.</param>
    /// <param name="DisplayName">The name shown to the user.</param>
    private sealed record AssociationHandler(nint Instance, string? Name, string? DisplayName);
}
