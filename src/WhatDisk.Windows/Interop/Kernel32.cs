using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Bindings to the file, volume and memory functions of kernel32.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Kernel32
{
    /// <summary>The right to list a directory.</summary>
    public const uint FileListDirectory = 0x0001;

    /// <summary>The right to read an item's attributes.</summary>
    public const uint FileReadAttributes = 0x0080;

    /// <summary>Lets other processes read, write and delete the item while it is open.</summary>
    public const uint FileShareAll = 0x1 | 0x2 | 0x4;

    /// <summary>Opens an existing item only.</summary>
    public const uint OpenExisting = 3;

    /// <summary>Allows directories to be opened.</summary>
    public const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>Opens a reparse point itself rather than its target.</summary>
    public const uint FileFlagOpenReparsePoint = 0x00200000;

    /// <summary>Allocates movable global memory, as the clipboard requires.</summary>
    public const uint GlobalMoveable = 0x0002;

    /// <summary>
    /// Opens a file or directory.
    /// </summary>
    /// <param name="fileName">The path.</param>
    /// <param name="desiredAccess">The access rights.</param>
    /// <param name="shareMode">The sharing mode.</param>
    /// <param name="securityAttributes">Unused; zero.</param>
    /// <param name="creationDisposition">What to do if the item exists or not.</param>
    /// <param name="flagsAndAttributes">The flags.</param>
    /// <param name="templateFile">Unused; zero.</param>
    /// <returns>The handle, invalid on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    /// <summary>
    /// Reads information about an open item, or the next batch of a directory's entries.
    /// </summary>
    /// <param name="file">The open item.</param>
    /// <param name="infoClass">The kind of information.</param>
    /// <param name="buffer">Receives the information.</param>
    /// <param name="bufferSize">The size of the buffer in bytes.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, void* buffer, uint bufferSize);

    /// <summary>
    /// Reads an open item's volume serial number, file index and link count.
    /// </summary>
    /// <param name="file">The open item.</param>
    /// <param name="information">Receives the information.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    /// <summary>
    /// Gets the volume GUID path of a drive letter, mounted folder or volume (the path must end with a backslash).
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="volumeName">Receives the volume GUID path.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns><see langword="true"/> when the path is a mount point.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* volumeName, uint length);

    /// <summary>
    /// Starts listing the system's volumes.
    /// </summary>
    /// <param name="volumeName">Receives the first volume GUID path.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns>The search handle, or -1.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", SetLastError = true)]
    public static partial nint FindFirstVolume(char* volumeName, uint length);

    /// <summary>
    /// Continues listing the system's volumes.
    /// </summary>
    /// <param name="find">The search handle.</param>
    /// <param name="volumeName">Receives the next volume GUID path.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns><see langword="false"/> when there are no more volumes.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "FindNextVolumeW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindNextVolume(nint find, char* volumeName, uint length);

    /// <summary>
    /// Ends a volume listing.
    /// </summary>
    /// <param name="find">The search handle.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindVolumeClose(nint find);

    /// <summary>
    /// Lists the drive letters and mounted folders of a volume.
    /// </summary>
    /// <param name="volumeName">The volume GUID path.</param>
    /// <param name="pathNames">Receives the paths, as a list of strings ending with an empty string.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <param name="returnLength">Receives the length needed.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathNamesForVolumeName(string volumeName, char* pathNames, uint length, out uint returnLength);

    /// <summary>
    /// Reads a volume's label, serial number, flags and file system name.
    /// </summary>
    /// <param name="rootPath">The volume's root, ending with a backslash.</param>
    /// <param name="volumeName">Receives the label.</param>
    /// <param name="volumeNameSize">The label buffer length in characters.</param>
    /// <param name="serialNumber">Receives the serial number.</param>
    /// <param name="maximumComponentLength">Receives the longest file name allowed.</param>
    /// <param name="fileSystemFlags">Receives the volume's flags.</param>
    /// <param name="fileSystemName">Receives the file system name.</param>
    /// <param name="fileSystemNameSize">The file system name buffer length in characters.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(string rootPath, char* volumeName, uint volumeNameSize, out uint serialNumber, out uint maximumComponentLength, out uint fileSystemFlags, char* fileSystemName, uint fileSystemNameSize);

    /// <summary>
    /// Gets the type of a drive (fixed, removable, network and so on).
    /// </summary>
    /// <param name="rootPath">The drive's root, ending with a backslash.</param>
    /// <returns>The drive type (see <see cref="WindowsVolumeFacts"/>).</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetDriveType(string rootPath);

    /// <summary>
    /// Reads a volume's capacity and free space.
    /// </summary>
    /// <param name="directory">A directory on the volume.</param>
    /// <param name="freeBytesAvailable">Receives the free space available to the user.</param>
    /// <param name="totalBytes">Receives the capacity available to the user.</param>
    /// <param name="totalFreeBytes">Receives the total free space.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    /// <summary>
    /// Lists the drive roots in use, as a list of strings ending with an empty string.
    /// </summary>
    /// <param name="length">The buffer length in characters.</param>
    /// <param name="buffer">Receives the roots.</param>
    /// <returns>The length written, or the length needed when the buffer is too small; zero on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetLogicalDriveStringsW", SetLastError = true)]
    public static partial uint GetLogicalDriveStrings(uint length, char* buffer);

    /// <summary>
    /// Gets the root of the volume holding a path (a drive root, mounted folder or network share root).
    /// </summary>
    /// <param name="fileName">The path.</param>
    /// <param name="volumePathName">Receives the root, ending with a backslash.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathName(string fileName, char* volumePathName, uint length);

    /// <summary>
    /// Gets the target of an MS-DOS device name, for example the folder behind a <c>subst</c> drive.
    /// </summary>
    /// <param name="deviceName">The device name, for example <c>X:</c>.</param>
    /// <param name="targetPath">Receives the targets.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns>The length written, or zero on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint QueryDosDevice(string deviceName, char* targetPath, uint length);

    /// <summary>
    /// Expands 8.3 short names in a path (for example <c>RUNNER~1</c>) to the long names.
    /// </summary>
    /// <param name="shortPath">The path.</param>
    /// <param name="longPath">Receives the long path.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns>The length written, the length needed when the buffer is too small, or zero on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetLongPathName(string shortPath, char* longPath, uint length);

    /// <summary>
    /// Allocates global memory, as the clipboard requires.
    /// </summary>
    /// <param name="flags">The allocation flags.</param>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The memory handle, or zero.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    /// <summary>
    /// Locks global memory and returns its address.
    /// </summary>
    /// <param name="memory">The memory handle.</param>
    /// <returns>The address, or null.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial void* GlobalLock(nint memory);

    /// <summary>
    /// Unlocks global memory.
    /// </summary>
    /// <param name="memory">The memory handle.</param>
    /// <returns><see langword="true"/> while the memory is still locked.</returns>
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    /// <summary>
    /// Frees global memory.
    /// </summary>
    /// <param name="memory">The memory handle.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint memory);

    /// <summary>
    /// Gets the size of global memory.
    /// </summary>
    /// <param name="memory">The memory handle.</param>
    /// <returns>The size in bytes.</returns>
    [LibraryImport("kernel32.dll")]
    public static partial nuint GlobalSize(nint memory);

    /// <summary>
    /// Information about an open item, as returned by <see cref="GetFileInformationByHandle"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        /// <summary>The item's attributes.</summary>
        public uint FileAttributes;

        /// <summary>When the item was created (low half).</summary>
        public uint CreationTimeLow;

        /// <summary>When the item was created (high half).</summary>
        public uint CreationTimeHigh;

        /// <summary>When the item was last read (low half).</summary>
        public uint LastAccessTimeLow;

        /// <summary>When the item was last read (high half).</summary>
        public uint LastAccessTimeHigh;

        /// <summary>When the item was last written (low half).</summary>
        public uint LastWriteTimeLow;

        /// <summary>When the item was last written (high half).</summary>
        public uint LastWriteTimeHigh;

        /// <summary>The serial number of the volume holding the item.</summary>
        public uint VolumeSerialNumber;

        /// <summary>The size (high half).</summary>
        public uint FileSizeHigh;

        /// <summary>The size (low half).</summary>
        public uint FileSizeLow;

        /// <summary>The number of hard links to the item.</summary>
        public uint NumberOfLinks;

        /// <summary>The item's index on its volume (high half).</summary>
        public uint FileIndexHigh;

        /// <summary>The item's index on its volume (low half).</summary>
        public uint FileIndexLow;
    }
}
