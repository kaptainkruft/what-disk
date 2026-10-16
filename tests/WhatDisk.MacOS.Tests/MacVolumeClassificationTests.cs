using System.Buffers.Binary;
using System.Text;
using WhatDisk.MacOS.Interop;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests how macOS volumes are classified from their file system and mount flags. These run on every platform.
/// </summary>
public sealed class MacVolumeClassificationTests
{
    /// <summary>
    /// Mount flags and file system names determine the kind of volume.
    /// </summary>
    [Fact]
    public void Classify_UsesFileSystemAndMountFlags()
    {
        const uint Local = AttributeConstants.MountLocal;
        Assert.Equal(VolumeKind.Fixed, MacVolumeFacts.Classify("/", "apfs", Local | AttributeConstants.MountSnapshot | AttributeConstants.MountRootFileSystem));
        Assert.Equal(VolumeKind.Virtual, MacVolumeFacts.Classify("/Volumes/com.apple.TimeMachine.localsnapshots/x", "apfs", Local | AttributeConstants.MountSnapshot));
        Assert.Equal(VolumeKind.Network, MacVolumeFacts.Classify("/Volumes/share", "smbfs", 0x18));
        Assert.Equal(VolumeKind.Network, MacVolumeFacts.Classify("/Volumes/odd", "apfs", 0));
        Assert.Equal(VolumeKind.Removable, MacVolumeFacts.Classify("/Volumes/CARD", "msdos", Local | AttributeConstants.MountRemovable));
        Assert.Equal(VolumeKind.Virtual, MacVolumeFacts.Classify("/dev", "devfs", null));
        Assert.Equal(VolumeKind.Unknown, MacVolumeFacts.Classify("/x", "apfs", null));
    }

    /// <summary>
    /// The volume facts buffer decodes device, name and flags.
    /// </summary>
    [Fact]
    public void ParseVolumeFacts_DecodesDeviceNameAndFlags()
    {
        var name = Encoding.UTF8.GetBytes("Macintosh HD\0");
        var bytes = new byte[20 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x01000013);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)name.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4480d001);
        name.CopyTo(bytes, 20);

        var facts = MacVolumeFacts.Parse(bytes);

        Assert.Equal(0x01000013ul, facts.DeviceId);
        Assert.Equal("Macintosh HD", facts.Name);
        Assert.Equal(0x4480d001u, facts.MountFlags);
    }
}
