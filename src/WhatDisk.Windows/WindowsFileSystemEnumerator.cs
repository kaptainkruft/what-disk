using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// Reads directories with <c>GetFileInformationByHandleEx</c>, which returns names, sizes, allocated sizes,
/// attributes, reparse tags and file IDs for a whole batch of entries in one call. Symbolic links and junctions are
/// reported as links and never followed; junction-type mount points of other volumes are reported as mount points.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsFileSystemEnumerator : IFileSystemEnumerator
{
    /// <summary>The listing buffer size: 64 KB, the most network file systems accept per call.</summary>
    private const int BufferSize = 64 * 1024;

    /// <summary>The <c>FileBasicInfo</c> information class.</summary>
    private const int BasicInfoClass = 0;

    /// <summary>The <c>FileStandardInfo</c> information class.</summary>
    private const int StandardInfoClass = 1;

    /// <summary>The <c>FileAttributeTagInfo</c> information class.</summary>
    private const int AttributeTagInfoClass = 9;

    /// <summary>The longest volume GUID path, including the terminator.</summary>
    private const int VolumeNameLength = 64;

    /// <summary>
    /// Gets the name of the enumeration method, shown in the status bar.
    /// </summary>
    public string Name => "GetFileInformationByHandleEx";

    /// <summary>
    /// Creates the Windows enumerator on Windows, or the portable one elsewhere.
    /// </summary>
    /// <returns>The enumerator.</returns>
    [SupportedOSPlatformGuard("windows")]
    public static IFileSystemEnumerator CreateOrFallback() =>
        OperatingSystem.IsWindows() ? new WindowsFileSystemEnumerator() : new ManagedFileSystemEnumerator();

    /// <inheritdoc/>
    public FileIdentity GetIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var handle = Kernel32.CreateFile(WindowsPaths.ToExtended(path), Kernel32.FileReadAttributes, Kernel32.FileShareAll, 0, Kernel32.OpenExisting, Kernel32.FileFlagBackupSemantics, 0);
        return handle.IsInvalid ? FileIdentity.Unknown : GetIdentity(handle);
    }

    /// <summary>
    /// Resolves symbolic links and junctions in a path and expands 8.3 short names (such as <c>RUNNER~1</c>), so that
    /// equivalent paths compare equal.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The canonical path.</returns>
    public string GetCanonicalPath(string path)
    {
        var resolved = PathUtilities.ResolveLinks(path);
        var buffer = stackalloc char[1024];
        var length = Kernel32.GetLongPathName(resolved, buffer, 1024);
        return length is > 0 and < 1024 ? PathUtilities.TrimTrailingSeparator(new string(buffer, 0, (int)length)) : resolved;
    }

    /// <inheritdoc/>
    public FileSystemEntryInfo? GetEntry(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var handle = Kernel32.CreateFile(
            WindowsPaths.ToExtended(path), Kernel32.FileReadAttributes, Kernel32.FileShareAll, 0, Kernel32.OpenExisting,
            Kernel32.FileFlagBackupSemantics | Kernel32.FileFlagOpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        BasicInfo basic;
        StandardInfo standard;
        AttributeTagInfo tag;
        if (!Kernel32.GetFileInformationByHandleEx(handle, BasicInfoClass, &basic, (uint)sizeof(BasicInfo))
            || !Kernel32.GetFileInformationByHandleEx(handle, StandardInfoClass, &standard, (uint)sizeof(StandardInfo)))
        {
            return null;
        }

        var reparseTag = Kernel32.GetFileInformationByHandleEx(handle, AttributeTagInfoClass, &tag, (uint)sizeof(AttributeTagInfo)) ? tag.ReparseTag : 0;
        var identity = GetIdentity(handle);
        var raw = new RawDirectoryEntry(
            Path.GetFileName(PathUtilities.TrimTrailingSeparator(path)),
            basic.FileAttributes,
            (basic.FileAttributes & DirectoryEntryParser.ReparsePointAttribute) != 0 ? reparseTag : 0,
            standard.EndOfFile,
            standard.AllocationSize,
            basic.LastWriteTime,
            identity.FileId);
        var parent = Path.GetDirectoryName(path) ?? path;
        return DirectoryEntryParser.ToEntry(in raw, identity.Device, name => IsVolumeMountPoint(Path.Join(parent, name))) with
        {
            LinkCount = standard.NumberOfLinks,
        };
    }

    /// <inheritdoc/>
    public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        using var handle = Kernel32.CreateFile(
            WindowsPaths.ToExtended(path), Kernel32.FileListDirectory | Kernel32.FileReadAttributes, Kernel32.FileShareAll, 0,
            Kernel32.OpenExisting, Kernel32.FileFlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            throw Win32Errors.ToException(Marshal.GetLastPInvokeError(), path);
        }

        var identity = GetIdentity(handle);
        var raw = new List<RawDirectoryEntry>(64);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            ReadAll(handle, path, buffer, raw, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        foreach (var entry in raw)
        {
            entries.Add(DirectoryEntryParser.ToEntry(in entry, identity.Device, name => IsVolumeMountPoint(Path.Join(path, name))));
        }

        return identity;
    }

    /// <summary>
    /// Determines whether a junction-type reparse point mounts a volume (rather than linking to a folder).
    /// </summary>
    /// <param name="path">The path of the reparse point.</param>
    /// <returns><see langword="true"/> for a mounted volume.</returns>
    internal static bool IsVolumeMountPoint(string path)
    {
        var volumeName = stackalloc char[VolumeNameLength];
        return Kernel32.GetVolumeNameForVolumeMountPoint(WindowsPaths.WithTrailingSeparator(path), volumeName, VolumeNameLength);
    }

    /// <summary>
    /// Reads every batch of a directory listing, falling back to simpler formats on file systems that do not support
    /// the richer ones (FAT, some network servers).
    /// </summary>
    /// <param name="handle">The open directory.</param>
    /// <param name="path">The directory's path, for error messages.</param>
    /// <param name="buffer">The listing buffer.</param>
    /// <param name="entries">Receives the entries.</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    private static void ReadAll(SafeFileHandle handle, string path, byte[] buffer, List<RawDirectoryEntry> entries, CancellationToken cancellationToken)
    {
        var listingClass = DirectoryListingClass.IdExtended;
        var first = true;
        fixed (byte* pointer = buffer)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Kernel32.GetFileInformationByHandleEx(handle, (int)listingClass, pointer, BufferSize))
                {
                    DirectoryEntryParser.Parse(new ReadOnlySpan<byte>(pointer, BufferSize), listingClass, entries);
                    first = false;
                    continue;
                }

                var error = Marshal.GetLastPInvokeError();
                if (error == Win32Errors.NoMoreFiles)
                {
                    return;
                }

                if (first && Win32Errors.IsUnsupportedRequest(error) && listingClass != DirectoryListingClass.Full)
                {
                    listingClass = listingClass == DirectoryListingClass.IdExtended ? DirectoryListingClass.IdBoth : DirectoryListingClass.Full;
                    continue;
                }

                throw Win32Errors.ToException(error, path);
            }
        }
    }

    /// <summary>
    /// Reads the volume serial number and file index of an open item.
    /// </summary>
    /// <param name="handle">The open item.</param>
    /// <returns>The identity, or <see cref="FileIdentity.Unknown"/>.</returns>
    private static FileIdentity GetIdentity(SafeFileHandle handle) =>
        Kernel32.GetFileInformationByHandle(handle, out var information)
            ? new FileIdentity(information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow)
            : FileIdentity.Unknown;

    /// <summary>
    /// The <c>FILE_BASIC_INFO</c> structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo
    {
        /// <summary>When the item was created.</summary>
        public long CreationTime;

        /// <summary>When the item was last read.</summary>
        public long LastAccessTime;

        /// <summary>When the item was last written.</summary>
        public long LastWriteTime;

        /// <summary>When the item's metadata last changed.</summary>
        public long ChangeTime;

        /// <summary>The item's attributes.</summary>
        public uint FileAttributes;
    }

    /// <summary>
    /// The <c>FILE_STANDARD_INFO</c> structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        /// <summary>The space allocated on disk.</summary>
        public long AllocationSize;

        /// <summary>The size in bytes.</summary>
        public long EndOfFile;

        /// <summary>The number of hard links.</summary>
        public uint NumberOfLinks;

        /// <summary>Whether the item is being deleted.</summary>
        public byte DeletePending;

        /// <summary>Whether the item is a directory.</summary>
        public byte Directory;
    }

    /// <summary>
    /// The <c>FILE_ATTRIBUTE_TAG_INFO</c> structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        /// <summary>The item's attributes.</summary>
        public uint FileAttributes;

        /// <summary>The reparse tag.</summary>
        public uint ReparseTag;
    }
}
