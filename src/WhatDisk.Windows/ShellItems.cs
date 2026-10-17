using System.Runtime.Versioning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// Shell item ID lists for a set of paths, freed together. Each list is absolute (relative to the desktop).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class ShellItems : IDisposable
{
    /// <summary>The item ID lists.</summary>
    private readonly nint[] _idLists;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellItems"/> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <exception cref="FileNotFoundException">A path does not exist.</exception>
    public ShellItems(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _idLists = new nint[paths.Count];
        for (var index = 0; index < paths.Count; index++)
        {
            _idLists[index] = Shell32.ILCreateFromPath(paths[index]);
            if (_idLists[index] == 0)
            {
                Dispose();
                throw new FileNotFoundException($"“{paths[index]}” could not be found.", paths[index]);
            }
        }
    }

    /// <summary>
    /// Gets the item ID lists.
    /// </summary>
    public ReadOnlySpan<nint> IdLists => _idLists;

    /// <summary>
    /// Creates an <c>IDataObject</c> holding the items, as shell handlers and Properties expect.
    /// </summary>
    /// <returns>The data object; release it with <see cref="ComObject.Release"/>.</returns>
    public nint CreateDataObject()
    {
        var interfaceId = ComObject.DataObjectId;
        fixed (nint* items = _idLists)
        {
            var result = Shell32.SHCreateDataObject(0, (uint)_idLists.Length, items, 0, &interfaceId, out var dataObject);
            return result >= 0 ? dataObject : throw new IOException($"The items could not be prepared for the shell (0x{result:X8}).");
        }
    }

    /// <summary>
    /// Frees the item ID lists.
    /// </summary>
    public void Dispose()
    {
        for (var index = 0; index < _idLists.Length; index++)
        {
            if (_idLists[index] != 0)
            {
                Shell32.ILFree(_idLists[index]);
                _idLists[index] = 0;
            }
        }
    }
}
