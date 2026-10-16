using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests icon loading through <c>NSWorkspace</c>. Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacIconProviderTests
{
    /// <summary>The PNG file signature.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The provider under test.</summary>
    private readonly MacIconProvider _provider = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MacIconProviderTests"/> class, skipping off macOS.
    /// </summary>
    public MacIconProviderTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// Folder, file type, volume and path icons load as PNG images.
    /// </summary>
    /// <param name="category">The icon category.</param>
    /// <param name="value">The category qualifier.</param>
    [Theory]
    [InlineData(IconKey.FolderCategory, null)]
    [InlineData(IconKey.FileCategory, ".pdf")]
    [InlineData(IconKey.FileCategory, ".unknownextension")]
    [InlineData(IconKey.FileCategory, "")]
    [InlineData(IconKey.VolumeCategory, "/")]
    [InlineData(IconKey.PathCategory, "/System/Applications/Calculator.app")]
    [InlineData(IconKey.LinkCategory, null)]
    public void LoadIcon_ReturnsPngImages(string category, string? value)
    {
        var icon = _provider.LoadIcon(new IconKey(category, value), 32);

        Assert.NotNull(icon);
        Assert.Equal(IconImageFormat.Encoded, icon.Format);
        Assert.True(icon.Data.AsSpan().StartsWith(PngSignature));
    }

    /// <summary>
    /// Icons can be loaded concurrently from background threads.
    /// </summary>
    /// <returns>A task that completes when every load has finished.</returns>
    [Fact]
    public async Task LoadIcon_IsSafeOnBackgroundThreads()
    {
        string[] extensions = [".txt", ".jpg", ".zip", ".mp4", ".swift", ".json", ".dmg", ".pages"];
        var icons = await Task.WhenAll(extensions.Select(extension =>
            Task.Run(() => _provider.LoadIcon(new IconKey(IconKey.FileCategory, extension), 32), TestContext.Current.CancellationToken)));

        Assert.All(icons, Assert.NotNull);
    }

    /// <summary>
    /// Application bundles get per-path icons; ordinary folders share the generic folder icon.
    /// </summary>
    [Fact]
    public void GetKey_UsesPathsForBundles()
    {
        var volume = VolumeInfo.CreateUnknown("/");
        var applications = new FakeRoot("/System/Applications", volume);

        Assert.Equal(IconKey.PathCategory, _provider.GetKey(applications.Child("Calculator.app")).Category);
        Assert.Equal(IconKey.FolderCategory, _provider.GetKey(applications.Child("Utilities-Folder")).Category);
    }

    /// <summary>
    /// Builds tiny scan trees for icon key tests.
    /// </summary>
    /// <param name="path">The root path.</param>
    /// <param name="volume">The volume.</param>
    private sealed class FakeRoot(string path, VolumeInfo volume)
    {
        /// <summary>The root of the tree.</summary>
        private readonly RootNode _root = CreateRoot(path, volume);

        /// <summary>
        /// Creates a child directory node.
        /// </summary>
        /// <param name="name">The child's name.</param>
        /// <returns>The node.</returns>
        public DirectoryNode Child(string name) => CreateDirectory(name, _root);

        /// <summary>
        /// Creates a root through the scanner, which owns node construction.
        /// </summary>
        /// <param name="rootPath">The path.</param>
        /// <param name="rootVolume">The volume.</param>
        /// <returns>The root.</returns>
        private static RootNode CreateRoot(string rootPath, VolumeInfo rootVolume)
        {
            _ = rootVolume;
            var fs = new SingleDirectoryEnumerator();
            using var session = new DiskScanner(fs, new PortableVolumeService(), TimeProvider.System).Start(new ScanRequest([rootPath], new ScanOptions { MaxDegreeOfParallelism = 1 }));
            session.Completion.Wait(TestContext.Current.CancellationToken);
            return session.Roots[0];
        }

        /// <summary>
        /// Creates a child directory under a root by scanning a fake directory that contains it.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="parent">The parent root (ignored; a fresh scan is used).</param>
        /// <returns>The directory node.</returns>
        private static DirectoryNode CreateDirectory(string name, RootNode parent)
        {
            var fs = new SingleDirectoryEnumerator(name);
            using var session = new DiskScanner(fs, new PortableVolumeService(), TimeProvider.System).Start(new ScanRequest([parent.RootPath], new ScanOptions { MaxDegreeOfParallelism = 1 }));
            session.Completion.Wait(TestContext.Current.CancellationToken);
            return (DirectoryNode)session.Roots[0].Children.Single();
        }
    }

    /// <summary>
    /// An enumerator whose root contains at most one empty subdirectory.
    /// </summary>
    /// <param name="childName">The name of the subdirectory, or <see langword="null"/> for none.</param>
    private sealed class SingleDirectoryEnumerator(string? childName = null) : IFileSystemEnumerator
    {
        /// <summary>The path of the first directory read, which is the root.</summary>
        private string? _root;

        /// <inheritdoc/>
        public string Name => "Single";

        /// <inheritdoc/>
        public FileIdentity GetIdentity(string path) => FileIdentity.Unknown;

        /// <inheritdoc/>
        public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken)
        {
            _root ??= path;
            if (childName is not null && path == _root)
            {
                entries.Add(new FileSystemEntryInfo(childName, EntryType.Directory, 0, 0, DateTime.MinValue));
            }

            return FileIdentity.Unknown;
        }
    }
}
