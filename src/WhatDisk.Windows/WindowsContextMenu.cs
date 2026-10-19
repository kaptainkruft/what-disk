using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// The shell's context menu for items in one folder: the menu File Explorer shows, which Windows 11 shows under Show
/// more options. The folder's <c>IShellFolder::GetUIObjectOf</c> gives the items' <c>IContextMenu</c>, which fills a
/// pop-up menu that <c>TrackPopupMenuEx</c> shows, and <c>InvokeCommand</c> runs the command chosen. While the menu is
/// open, the owner window passes the menu's messages to <c>IContextMenu3</c> (or <c>IContextMenu2</c>), which fill
/// submenus such as Send To and draw their icons. Rename is left out, as it needs File Explorer's own view.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsContextMenu : IDisposable
{
    /// <summary>The identifier of the shell's first command; zero means no command was chosen.</summary>
    private const uint FirstCommand = 1;

    /// <summary>The highest identifier the shell's commands may use.</summary>
    private const uint LastCommand = 0x7FFF;

    /// <summary>The HRESULT of a command the user cancelled (<c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c>).</summary>
    private const int Cancelled = unchecked((int)0x800704C7);

    /// <summary>The items' <c>IContextMenu</c>.</summary>
    private nint _contextMenu;

    /// <summary>The items' <c>IContextMenu2</c>, when they have it and not <c>IContextMenu3</c>; otherwise zero.</summary>
    private nint _contextMenu2;

    /// <summary>The items' <c>IContextMenu3</c>, or zero.</summary>
    private nint _contextMenu3;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsContextMenu"/> class.
    /// </summary>
    /// <param name="contextMenu">The items' <c>IContextMenu</c>, which the instance releases.</param>
    private WindowsContextMenu(nint contextMenu)
    {
        _contextMenu = contextMenu;
        _contextMenu3 = ComObject.QueryInterface(contextMenu, ComObject.ContextMenu3Id);
        _contextMenu2 = _contextMenu3 == 0 ? ComObject.QueryInterface(contextMenu, ComObject.ContextMenu2Id) : 0;
    }

    /// <summary>
    /// Gets the pop-up menu, filled with the shell's commands.
    /// </summary>
    public nint Menu { get; private set; }

    /// <summary>
    /// Shows the shell's context menu for items, waits until it closes, and runs the command chosen. Must be called on
    /// the UI thread, which owns the window and is a single-threaded apartment.
    /// </summary>
    /// <param name="owner">The window that owns the menu.</param>
    /// <param name="paths">The items, all in one folder.</param>
    /// <param name="position">The menu's top-left corner, in physical screen pixels.</param>
    /// <param name="extendedVerbs">Whether to include the commands normally shown only with Shift held.</param>
    /// <exception cref="InvalidOperationException">The window has no handle, or the items are in different folders.</exception>
    /// <exception cref="IOException">An item could not be found, or the shell failed.</exception>
    public static void Show(TopLevel owner, IReadOnlyList<string> paths, PixelPoint position, bool extendedVerbs)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(paths);
        Dispatcher.UIThread.VerifyAccess();
        var window = owner.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("The window has no handle.");

        // Commands such as Copy and Cut put items on the OLE clipboard, which needs OLE on this thread.
        var ole = Ole32.OleInitialize(0);
        try
        {
            using var menu = Create(paths, window, extendedVerbs);
            var command = menu.Track(owner, window, position);
            if (command >= FirstCommand)
            {
                menu.Invoke(window, command, position);
            }
        }
        finally
        {
            if (ole >= 0)
            {
                Ole32.OleUninitialize();
            }
        }
    }

    /// <summary>
    /// Gets the shell's context menu for items, filled into a pop-up menu but not shown. Must be called in a
    /// single-threaded apartment, where the instance must be used and disposed of.
    /// </summary>
    /// <param name="paths">The items, all in one folder.</param>
    /// <param name="owner">The window that owns any window the shell shows, or zero.</param>
    /// <param name="extendedVerbs">Whether to include the commands normally shown only with Shift held.</param>
    /// <returns>The menu.</returns>
    /// <exception cref="InvalidOperationException">The items are in different folders.</exception>
    /// <exception cref="IOException">An item could not be found, or the shell failed.</exception>
    public static WindowsContextMenu Create(IReadOnlyList<string> paths, nint owner, bool extendedVerbs)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfZero(paths.Count);
        using var items = new ShellItems(paths);
        RequireOneFolder(items.IdLists);

        var folderId = ComObject.ShellFolderId;
        Check(Shell32.SHBindToParent(items.IdLists[0], &folderId, out var folder, out _), paths[0]);
        nint contextMenu = 0;
        try
        {
            var children = new nint[items.IdLists.Length];
            for (var index = 0; index < children.Length; index++)
            {
                children[index] = Shell32.ILFindLastID(items.IdLists[index]);
            }

            var menuId = ComObject.ContextMenuId;
            var getUIObjectOf = (delegate* unmanaged<nint, nint, uint, nint*, Guid*, uint*, nint*, int>)ComObject.Method(folder, 10);
            fixed (nint* pointer = children)
            {
                Check(getUIObjectOf(folder, owner, (uint)children.Length, pointer, &menuId, null, &contextMenu), paths[0]);
            }
        }
        finally
        {
            ComObject.Release(folder);
        }

        var menu = new WindowsContextMenu(contextMenu);
        try
        {
            menu.Fill(extendedVerbs);
            return menu;
        }
        catch
        {
            menu.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the verbs of the menu's top-level commands, such as <c>open</c> and <c>properties</c>, where their
    /// handlers give one.
    /// </summary>
    /// <returns>The verbs, in menu order.</returns>
    public List<string> GetVerbs()
    {
        var verbs = new List<string>();
        var getCommandString = (delegate* unmanaged<nint, nuint, uint, uint*, char*, uint, int>)ComObject.Method(_contextMenu, 5);
        var buffer = stackalloc char[256];
        var count = User32.GetMenuItemCount(Menu);
        for (var position = 0; position < count; position++)
        {
            var command = User32.GetMenuItemID(Menu, position);
            if (command is < FirstCommand or > LastCommand)
            {
                continue;
            }

            buffer[0] = '\0';
            if (getCommandString(_contextMenu, command - FirstCommand, Shell32.CommandStringVerb, null, buffer, 256) >= 0 && buffer[0] != '\0')
            {
                verbs.Add(new string(buffer));
            }
        }

        return verbs;
    }

    /// <summary>
    /// Destroys the menu and releases the shell's objects.
    /// </summary>
    public void Dispose()
    {
        if (Menu != 0)
        {
            User32.DestroyMenu(Menu);
            Menu = 0;
        }

        ComObject.Release(_contextMenu3);
        ComObject.Release(_contextMenu2);
        ComObject.Release(_contextMenu);
        _contextMenu3 = 0;
        _contextMenu2 = 0;
        _contextMenu = 0;
    }

    /// <summary>
    /// Throws unless every item is in the folder of the first, as <c>GetUIObjectOf</c> requires. Drive roots are in
    /// This PC.
    /// </summary>
    /// <param name="idLists">The items' absolute ID lists.</param>
    /// <exception cref="InvalidOperationException">The items are in different folders.</exception>
    private static void RequireOneFolder(ReadOnlySpan<nint> idLists)
    {
        var folder = Shell32.ILClone(idLists[0]);
        if (folder == 0)
        {
            throw new IOException("The items could not be prepared for the shell.");
        }

        try
        {
            Shell32.ILRemoveLastID(folder);
            foreach (var idList in idLists)
            {
                if (!Shell32.ILIsParent(folder, idList, immediate: true))
                {
                    throw new InvalidOperationException("The items are in different folders.");
                }
            }
        }
        finally
        {
            Shell32.ILFree(folder);
        }
    }

    /// <summary>
    /// Throws when a shell call failed.
    /// </summary>
    /// <param name="result">The HRESULT.</param>
    /// <param name="subject">The item it was about.</param>
    private static void Check(int result, string subject)
    {
        if (result < 0)
        {
            throw new IOException($"The shell has no context menu for “{subject}” (0x{result:X8}).");
        }
    }

    /// <summary>
    /// Creates the pop-up menu and asks the shell to fill it. Without <c>CMF_CANRENAME</c>, the shell leaves out
    /// Rename.
    /// </summary>
    /// <param name="extendedVerbs">Whether to include the commands normally shown only with Shift held.</param>
    private void Fill(bool extendedVerbs)
    {
        Menu = User32.CreatePopupMenu();
        if (Menu == 0)
        {
            throw new IOException($"The menu could not be created (error {Marshal.GetLastPInvokeError()}).");
        }

        var flags = extendedVerbs ? Shell32.ContextMenuExtendedVerbs : Shell32.ContextMenuNormal;
        var queryContextMenu = (delegate* unmanaged<nint, nint, uint, uint, uint, uint, int>)ComObject.Method(_contextMenu, 3);
        var result = queryContextMenu(_contextMenu, Menu, 0, FirstCommand, LastCommand, flags);
        if (result < 0)
        {
            throw new IOException($"The shell could not fill the context menu (0x{result:X8}).");
        }
    }

    /// <summary>
    /// Shows the menu and waits until it closes, passing the owner window's menu messages to the shell meanwhile.
    /// </summary>
    /// <param name="owner">The window that owns the menu.</param>
    /// <param name="window">The window's handle.</param>
    /// <param name="position">The menu's top-left corner, in physical screen pixels.</param>
    /// <returns>The identifier of the command chosen, or zero.</returns>
    private uint Track(TopLevel owner, nint window, PixelPoint position)
    {
        Win32Properties.CustomWndProcHookCallback hook = ForwardMenuMessage;
        Win32Properties.AddWndProcHookCallback(owner, hook);
        try
        {
            return (uint)User32.TrackPopupMenuEx(Menu, User32.TrackReturnCommand | User32.TrackRightButton, position.X, position.Y, window, 0);
        }
        finally
        {
            Win32Properties.RemoveWndProcHookCallback(owner, hook);
        }
    }

    /// <summary>
    /// Passes the messages of the menu's owner-drawn items and submenus to the shell, which fills submenus as they open
    /// and draws their icons. Other messages, and those of the window's own controls, are left to the window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="message">The message.</param>
    /// <param name="wParam">The message's first parameter.</param>
    /// <param name="lParam">The message's second parameter.</param>
    /// <param name="handled">Set when the shell handled the message.</param>
    /// <returns>The message's result.</returns>
    private nint ForwardMenuMessage(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        var isMenuMessage = message switch
        {
            User32.InitMenuPopupMessage or User32.MenuCharMessage => true,
            // Zero identifies a menu as the sender; anything else is a control's identifier.
            User32.DrawItemMessage or User32.MeasureItemMessage => wParam == 0,
            _ => false,
        };
        if (!isMenuMessage)
        {
            return 0;
        }

        if (_contextMenu3 != 0)
        {
            nint result = 0;
            var handleMenuMessage2 = (delegate* unmanaged<nint, uint, nint, nint, nint*, int>)ComObject.Method(_contextMenu3, 7);
            if (handleMenuMessage2(_contextMenu3, message, wParam, lParam, &result) >= 0)
            {
                handled = true;
                return result;
            }
        }
        else if (_contextMenu2 != 0 && message != User32.MenuCharMessage)
        {
            var handleMenuMessage = (delegate* unmanaged<nint, uint, nint, nint, int>)ComObject.Method(_contextMenu2, 6);
            if (handleMenuMessage(_contextMenu2, message, wParam, lParam) >= 0)
            {
                handled = true;
            }
        }

        return 0;
    }

    /// <summary>
    /// Runs a command chosen from the menu.
    /// </summary>
    /// <param name="window">The window that owns any window the command shows.</param>
    /// <param name="command">The command's identifier.</param>
    /// <param name="position">Where the command was chosen, in physical screen pixels.</param>
    private void Invoke(nint window, uint command, PixelPoint position)
    {
        var info = new Shell32.CommandInvokeInfo
        {
            Size = (uint)sizeof(Shell32.CommandInvokeInfo),
            Mask = Shell32.InvokeUnicode | Shell32.InvokePoint,
            Window = window,
            Verb = (nint)(command - FirstCommand),
            VerbW = (nint)(command - FirstCommand),
            Show = Shell32.ShowNormal,
            InvokeX = position.X,
            InvokeY = position.Y,
        };
        var invokeCommand = (delegate* unmanaged<nint, Shell32.CommandInvokeInfo*, int>)ComObject.Method(_contextMenu, 4);
        var result = invokeCommand(_contextMenu, &info);
        if (result < 0 && result != Cancelled)
        {
            throw new IOException($"The command could not run (0x{result:X8}).");
        }
    }
}
