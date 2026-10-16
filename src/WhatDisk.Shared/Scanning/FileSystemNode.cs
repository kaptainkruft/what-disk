using System.Globalization;
using System.Text;
using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A node of the scan tree. Nodes are created by scan workers and read concurrently by the user interface; all
/// members are safe to read from any thread.
/// </summary>
public abstract class FileSystemNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemNode"/> class.
    /// </summary>
    /// <param name="name">The node's file name, or display name for a root.</param>
    /// <param name="parent">The containing directory, or <see langword="null"/> for a root.</param>
    private protected FileSystemNode(string name, DirectoryNode? parent)
    {
        Name = name;
        Parent = parent;
    }

    /// <summary>
    /// Gets the node's file name (for a root, its display name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the directory that contains this node, or <see langword="null"/> for a root.
    /// </summary>
    public DirectoryNode? Parent { get; }

    /// <summary>
    /// Gets the kind of node.
    /// </summary>
    public abstract NodeKind Kind { get; }

    /// <summary>
    /// Gets the number of bytes this node counts towards disk usage (the allocated size where the platform reports
    /// it), including everything beneath it.
    /// </summary>
    public abstract long Size { get; }

    /// <summary>
    /// Gets the logical (apparent) size in bytes of this node and everything beneath it.
    /// </summary>
    public abstract long LogicalSize { get; }

    /// <summary>
    /// Gets the number of files at or beneath this node.
    /// </summary>
    public abstract long FileCount { get; }

    /// <summary>
    /// Gets the number of directories beneath this node, excluding the node itself.
    /// </summary>
    public abstract long DirectoryCount { get; }

    /// <summary>
    /// Gets the volume this node is stored on.
    /// </summary>
    public abstract VolumeInfo Volume { get; }

    /// <summary>
    /// Gets when the node was last modified, in UTC, when known.
    /// </summary>
    public abstract DateTime? LastWriteTimeUtc { get; }

    /// <summary>
    /// Gets a value indicating whether the node can contain other nodes.
    /// </summary>
    public virtual bool IsContainer => false;

    /// <summary>
    /// Gets the absolute path of the node, rebuilt from the chain of parents.
    /// </summary>
    public string FullPath
    {
        get
        {
            var segments = new Stack<string>();
            FileSystemNode current = this;
            while (current.Parent is { } parent)
            {
                segments.Push(current.Name);
                current = parent;
            }

            var builder = new StringBuilder(current.TopLevelPath);
            foreach (var segment in segments)
            {
                if (builder.Length > 0 && builder[^1] != Path.DirectorySeparatorChar)
                {
                    builder.Append(Path.DirectorySeparatorChar);
                }

                builder.Append(segment);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Gets the path that a top-most node contributes to <see cref="FullPath"/>. Roots return the path that was
    /// scanned; any other parentless node returns its name.
    /// </summary>
    private protected virtual string TopLevelPath => Name;

    /// <summary>
    /// Gets the number of ancestors between this node and its root.
    /// </summary>
    public int Depth
    {
        get
        {
            var depth = 0;
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind} {Name} ({Size:N0} bytes)");
}
