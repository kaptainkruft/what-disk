namespace WhatDisk.Scanning;

/// <summary>
/// Navigation helpers for scan tree nodes, written as C# 14 extension members.
/// </summary>
public static class FileSystemNodeExtensions
{
    /// <summary>
    /// Extension members for <see cref="FileSystemNode"/>.
    /// </summary>
    /// <param name="node">The node being extended.</param>
    extension(FileSystemNode node)
    {
        /// <summary>
        /// Gets the node followed by each of its ancestors, ending with its root.
        /// </summary>
        public IEnumerable<FileSystemNode> SelfAndAncestors
        {
            get
            {
                for (FileSystemNode? current = node; current is not null; current = current.Parent)
                {
                    yield return current;
                }
            }
        }

        /// <summary>
        /// Gets the top-level node of the tree that contains the node.
        /// </summary>
        public FileSystemNode TopLevel => node.SelfAndAncestors.Last();
    }
}
