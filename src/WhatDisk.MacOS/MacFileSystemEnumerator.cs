using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS;

/// <summary>
/// Reads directories with <c>getattrlistbulk</c>, which returns the name, type, sizes (including the allocated size),
/// identity and mount status of many entries per system call, instead of one <c>stat</c> call per entry.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacFileSystemEnumerator : IFileSystemEnumerator
{
    /// <summary>The size of the attribute buffer used per call; large enough for several hundred entries.</summary>
    private const int BufferSize = 256 * 1024;

    /// <summary>The options used for every attribute call: physical device numbers.</summary>
    private const uint Options = AttributeConstants.OptionReturnRealDevice;

    /// <inheritdoc/>
    public string Name => "getattrlistbulk";

    /// <summary>
    /// Creates the enumerator, falling back to the portable one if the system calls are unavailable.
    /// </summary>
    /// <returns>The enumerator to use.</returns>
    public static IFileSystemEnumerator CreateOrFallback()
    {
        try
        {
            var enumerator = new MacFileSystemEnumerator();
            _ = enumerator.GetIdentity("/");
            return enumerator;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning("getattrlistbulk is unavailable ({0}); using the portable enumerator.", exception.Message);
            return new ManagedFileSystemEnumerator();
        }
    }

    /// <inheritdoc/>
    public FileIdentity GetIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var request = AttributeBufferParser.IdentityRequest;
        var buffer = stackalloc byte[64];
        return LibC.GetAttrList(path, &request, buffer, 64, Options | AttributeConstants.OptionNoFollow) == 0
            ? AttributeBufferParser.ParseIdentity(new ReadOnlySpan<byte>(buffer, 64))
            : FileIdentity.Unknown;
    }

    /// <inheritdoc/>
    public string GetCanonicalPath(string path) => PathUtilities.ResolveLinks(path);

    /// <inheritdoc/>
    public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var fd = LibC.Open(path, LibC.ORdOnly | LibC.ODirectory | LibC.OCloExec);
        if (fd < 0)
        {
            throw PosixErrors.ToException(Marshal.GetLastPInvokeError(), path);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var identity = GetIdentity(fd);
            var request = AttributeBufferParser.BulkRequest;
            fixed (byte* pointer = buffer)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = LibC.GetAttrListBulk(fd, &request, pointer, (nuint)buffer.Length, Options);
                    if (count == 0)
                    {
                        break;
                    }

                    if (count < 0)
                    {
                        var errno = Marshal.GetLastPInvokeError();
                        if (errno == PosixErrors.Interrupted)
                        {
                            continue;
                        }

                        throw PosixErrors.ToException(errno, path);
                    }

                    AttributeBufferParser.ParseEntries(new ReadOnlySpan<byte>(pointer, buffer.Length), count, entries);
                }
            }

            return identity;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _ = LibC.Close(fd);
        }
    }

    /// <summary>
    /// Gets the identity of an open directory, which is the directory actually reached (for a firmlink, the target).
    /// </summary>
    /// <param name="fd">The directory's descriptor.</param>
    /// <returns>The identity, or <see cref="FileIdentity.Unknown"/>.</returns>
    private static FileIdentity GetIdentity(int fd)
    {
        var request = AttributeBufferParser.IdentityRequest;
        var buffer = stackalloc byte[64];
        return LibC.FGetAttrList(fd, &request, buffer, 64, Options) == 0
            ? AttributeBufferParser.ParseIdentity(new ReadOnlySpan<byte>(buffer, 64))
            : FileIdentity.Unknown;
    }
}
