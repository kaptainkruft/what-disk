namespace WhatDisk.MacOS.Interop;

/// <summary>
/// Constants from <c>sys/attr.h</c>, <c>sys/stat.h</c>, <c>sys/vnode.h</c> and <c>sys/mount.h</c>.
/// </summary>
internal static class AttributeConstants
{
    /// <summary><c>ATTR_BIT_MAP_COUNT</c>: the number of attribute groups.</summary>
    public const ushort BitmapCount = 5;

    /// <summary><c>ATTR_CMN_NAME</c>: the entry's name.</summary>
    public const uint CommonName = 0x00000001;

    /// <summary><c>ATTR_CMN_DEVID</c>: the device the object is on.</summary>
    public const uint CommonDeviceId = 0x00000002;

    /// <summary><c>ATTR_CMN_OBJTYPE</c>: the object type (<c>enum vtype</c>).</summary>
    public const uint CommonObjectType = 0x00000008;

    /// <summary><c>ATTR_CMN_MODTIME</c>: the modification time.</summary>
    public const uint CommonModificationTime = 0x00000400;

    /// <summary><c>ATTR_CMN_FLAGS</c>: the BSD file flags.</summary>
    public const uint CommonFlags = 0x00040000;

    /// <summary><c>ATTR_CMN_FILEID</c>: the 64-bit file number.</summary>
    public const uint CommonFileId = 0x02000000;

    /// <summary><c>ATTR_CMN_ERROR</c>: a per-entry error code.</summary>
    public const uint CommonError = 0x20000000;

    /// <summary><c>ATTR_CMN_RETURNED_ATTRS</c>: which attributes were returned.</summary>
    public const uint CommonReturnedAttributes = 0x80000000;

    /// <summary><c>ATTR_VOL_INFO</c>: required whenever volume attributes are requested.</summary>
    public const uint VolumeInfo = 0x80000000;

    /// <summary><c>ATTR_VOL_NAME</c>: the volume name.</summary>
    public const uint VolumeName = 0x00002000;

    /// <summary><c>ATTR_VOL_MOUNTFLAGS</c>: the mount flags.</summary>
    public const uint VolumeMountFlags = 0x00004000;

    /// <summary><c>ATTR_DIR_MOUNTSTATUS</c>: whether something is mounted on the directory.</summary>
    public const uint DirectoryMountStatus = 0x00000004;

    /// <summary><c>ATTR_FILE_LINKCOUNT</c>: the number of hard links.</summary>
    public const uint FileLinkCount = 0x00000001;

    /// <summary><c>ATTR_FILE_TOTALSIZE</c>: the logical size of all forks.</summary>
    public const uint FileTotalSize = 0x00000002;

    /// <summary><c>ATTR_FILE_ALLOCSIZE</c>: the bytes allocated for all forks.</summary>
    public const uint FileAllocatedSize = 0x00000004;

    /// <summary><c>DIR_MNTSTATUS_MNTPOINT</c>: a file system is mounted on the directory.</summary>
    public const uint MountStatusMountPoint = 0x00000001;

    /// <summary><c>DIR_MNTSTATUS_TRIGGER</c>: the directory is an automount trigger.</summary>
    public const uint MountStatusTrigger = 0x00000002;

    /// <summary><c>FSOPT_NOFOLLOW</c>: do not follow a symbolic link at the end of the path.</summary>
    public const uint OptionNoFollow = 0x00000001;

    /// <summary><c>FSOPT_RETURN_REALDEV</c>: report physical rather than volume-group device numbers.</summary>
    public const uint OptionReturnRealDevice = 0x00000200;

    /// <summary><c>VREG</c>: a regular file.</summary>
    public const uint TypeRegular = 1;

    /// <summary><c>VDIR</c>: a directory.</summary>
    public const uint TypeDirectory = 2;

    /// <summary><c>VLNK</c>: a symbolic link.</summary>
    public const uint TypeSymbolicLink = 5;

    /// <summary><c>UF_IMMUTABLE</c>: the file may not be changed.</summary>
    public const uint FlagImmutable = 0x00000002;

    /// <summary><c>UF_COMPRESSED</c>: the file is compressed.</summary>
    public const uint FlagCompressed = 0x00000020;

    /// <summary><c>UF_HIDDEN</c>: the file is hidden from the Finder.</summary>
    public const uint FlagHidden = 0x00008000;

    /// <summary><c>SF_FIRMLINK</c>: the directory is a firmlink.</summary>
    public const uint FlagFirmlink = 0x00800000;

    /// <summary><c>SF_DATALESS</c>: the file's data is not stored locally.</summary>
    public const uint FlagDataless = 0x40000000;

    /// <summary><c>MNT_RDONLY</c>: mounted read-only.</summary>
    public const uint MountReadOnly = 0x00000001;

    /// <summary><c>MNT_REMOVABLE</c>: the media is removable.</summary>
    public const uint MountRemovable = 0x00000200;

    /// <summary><c>MNT_LOCAL</c>: the file system is stored locally.</summary>
    public const uint MountLocal = 0x00001000;

    /// <summary><c>MNT_ROOTFS</c>: the root file system.</summary>
    public const uint MountRootFileSystem = 0x00004000;

    /// <summary><c>MNT_DONTBROWSE</c>: not a path to user data; hidden by the Finder.</summary>
    public const uint MountDontBrowse = 0x00100000;

    /// <summary><c>MNT_SNAPSHOT</c>: the mount is a snapshot.</summary>
    public const uint MountSnapshot = 0x40000000;
}
