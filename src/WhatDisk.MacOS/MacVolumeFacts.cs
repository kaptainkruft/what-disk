using System.Buffers.Binary;
using System.Text;
using WhatDisk.MacOS.Interop;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS;

/// <summary>
/// What <c>getattrlist</c> reports about a mounted volume, with the pure logic that decodes and classifies it. Kept
/// free of native calls so it can be tested on any platform.
/// </summary>
/// <param name="DeviceId">The physical device number.</param>
/// <param name="Name">The volume name.</param>
/// <param name="MountFlags">The <c>MNT_*</c> flags.</param>
internal readonly record struct MacVolumeFacts(ulong? DeviceId, string? Name, uint? MountFlags)
{
    /// <summary>
    /// Classifies a volume from its file system type and mount flags.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="fileSystem">The file system type name.</param>
    /// <param name="mountFlags">The mount flags, or <see langword="null"/> when they could not be read.</param>
    /// <returns>The kind of volume.</returns>
    public static VolumeKind Classify(string mountPoint, string? fileSystem, uint? mountFlags)
    {
        if (FileSystemClassifier.Classify(fileSystem) is { } kind)
        {
            return kind;
        }

        if (mountFlags is not { } flags)
        {
            return VolumeKind.Unknown;
        }

        if ((flags & AttributeConstants.MountSnapshot) != 0 && (flags & AttributeConstants.MountRootFileSystem) == 0 && mountPoint != "/")
        {
            // Time Machine local snapshots and similar: their contents are counted on the live volume.
            return VolumeKind.Virtual;
        }

        if ((flags & AttributeConstants.MountLocal) == 0)
        {
            return VolumeKind.Network;
        }

        return (flags & AttributeConstants.MountRemovable) != 0 ? VolumeKind.Removable : VolumeKind.Fixed;
    }

    /// <summary>
    /// Decodes the result of the volume facts request: length, device, name reference, mount flags.
    /// </summary>
    /// <param name="buffer">The attribute buffer.</param>
    /// <returns>The facts.</returns>
    public static MacVolumeFacts Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(buffer) < 20)
        {
            return default;
        }

        var device = (ulong)(uint)BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]);
        var nameOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]);
        var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]);
        string? name = null;
        var start = 8 + nameOffset;
        if (nameOffset > 0 && nameLength > 0 && start + nameLength <= buffer.Length)
        {
            var bytes = buffer.Slice(start, nameLength);
            var terminator = bytes.IndexOf((byte)0);
            name = Encoding.UTF8.GetString(terminator >= 0 ? bytes[..terminator] : bytes);
        }

        return new MacVolumeFacts(device, string.IsNullOrEmpty(name) ? null : name, flags);
    }
}
