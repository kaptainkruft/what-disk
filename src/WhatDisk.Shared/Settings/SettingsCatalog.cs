using WhatDisk.Formatting;

namespace WhatDisk.Settings;

/// <summary>
/// A section of the settings file, shown as one page of the settings window.
/// </summary>
/// <param name="Key">The section's key in the settings file, such as <c>scanning</c>.</param>
/// <param name="Title">The page's title.</param>
/// <param name="Description">What the page covers.</param>
/// <param name="Settings">The section's settings, in display order, including those of every platform.</param>
public sealed record SettingSection(string Key, string Title, string Description, IReadOnlyList<SettingDefinition> Settings);

/// <summary>
/// Every setting, by section. The sections are the top-level keys of the settings file and the pages of the settings
/// window, in the same order; settings for one platform sit in their section beside the others and are stored in the
/// section's platform subsection.
/// </summary>
public static class SettingsCatalog
{
    /// <summary>The key of the General section.</summary>
    private const string GeneralKey = "general";

    /// <summary>The key of the Scanning section.</summary>
    private const string ScanningKey = "scanning";

    /// <summary>The key of the Duplicates section.</summary>
    private const string DuplicatesKey = "duplicates";

    /// <summary>The key of the Deleting section.</summary>
    private const string DeletingKey = "deleting";

    /// <summary>
    /// Gets the sections, in display order.
    /// </summary>
    public static IReadOnlyList<SettingSection> Sections { get; } =
    [
        new(GeneralKey, "General", "Appearance, how sizes are shown, and where the menus are.", [General.Theme, General.SizeUnits, General.UseMenuBar]),
        new(
            ScanningKey,
            "Scanning",
            "How folders are read, and what the Volumes menu lists.",
            [Scanning.Threads, Scanning.NetworkThreads, Scanning.IncludeNetworkVolumes, Scanning.RefreshMilliseconds, Scanning.ShowHiddenVolumes]),
        new(
            DuplicatesKey,
            "Duplicates",
            "How Tools › Find Duplicates compares files, and what the Potential duplicate space pane counts.",
            [Duplicates.HashUpToMiB, Duplicates.SampleCount, Duplicates.SampleSizeKiB, Duplicates.Readers, Duplicates.VerifyAutomatically, Duplicates.EstimateOverMiB]),
        new(
            DeletingKey,
            "Deleting",
            "When deleting or moving items to the Trash or Recycle Bin asks first.",
            [Deleting.ConfirmAboveFiles, Deleting.ConfirmAboveMegabytes, Deleting.WarnWhenNotRecyclable]),
    ];

    /// <summary>
    /// Gets every setting, in display order.
    /// </summary>
    public static IEnumerable<SettingDefinition> All => Sections.SelectMany(static section => section.Settings);

    /// <summary>
    /// General settings.
    /// </summary>
    public static class General
    {
        /// <summary>The theme: <c>system</c>, <c>light</c> or <c>dark</c>.</summary>
        public static readonly ChoiceSetting Theme = new(
            GeneralKey,
            "theme",
            "Appearance",
            "Light or dark, or follow the system's appearance.",
            [new("system", "Match the system"), new("light", "Light"), new("dark", "Dark")],
            static _ => "system");

        /// <summary>How sizes are counted: <c>decimal</c> (1 KB = 1,000 bytes) or <c>binary</c> (1 KB = 1,024 bytes).</summary>
        public static readonly ChoiceSetting SizeUnits = new(
            GeneralKey,
            "sizeUnits",
            "Size units",
            "How sizes are counted. The default follows the system: decimal in the Finder, binary in File Explorer.",
            [new("decimal", "Decimal (1 KB = 1,000 bytes)"), new("binary", "Binary (1 KB = 1,024 bytes)")],
            static platform => platform.ByteUnits == ByteUnitConvention.PowersOf1024 ? "binary" : "decimal");

        /// <summary>
        /// Whether the menus are in the macOS menu bar (the default) or drawn at the top of the window, as Avalonia's
        /// <c>MacOSPlatformOptions.DisableNativeMenus</c> option does. Read at start-up.
        /// </summary>
        public static readonly BoolSetting UseMenuBar = new(
            GeneralKey,
            "useMenuBar",
            "Use the macOS menu bar",
            "Show the menus in the menu bar at the top of the screen, as Mac apps do. When off, they are drawn at the top of the window instead, with Quit, Settings and About in them. Takes effect the next time WhatDisk starts.",
            true,
            SettingPlatform.MacOS);
    }

    /// <summary>
    /// Scanning settings.
    /// </summary>
    public static class Scanning
    {
        /// <summary>The number of folders read at once on local disks; zero chooses automatically.</summary>
        public static readonly IntSetting Threads = new(
            ScanningKey,
            "threads",
            "Folders read at once",
            "On local disks. Automatic uses the number of processor cores, between 4 and 16.",
            0,
            0,
            64)
        {
            Unit = "threads",
            ZeroMeans = "Automatic",
        };

        /// <summary>The number of folders read at once on network locations.</summary>
        public static readonly IntSetting NetworkThreads = new(
            ScanningKey,
            "networkThreads",
            "Folders read at once on network locations",
            "Network shares answer slowly, so reading more folders at once keeps them busy.",
            32,
            1,
            128)
        {
            Unit = "threads",
        };

        /// <summary>Whether network shares mounted inside a scanned folder are scanned too.</summary>
        public static readonly BoolSetting IncludeNetworkVolumes = new(
            ScanningKey,
            "includeNetworkVolumes",
            "Scan network shares inside scanned folders",
            "When a scanned folder contains a mounted network share, scan the share too, instead of listing it unscanned.",
            false);

        /// <summary>How often the panes update while a scan runs, in milliseconds.</summary>
        public static readonly IntSetting RefreshMilliseconds = new(
            ScanningKey,
            "refreshMilliseconds",
            "Update interval while scanning",
            "How often the tree, chart and details update while a scan runs.",
            200,
            50,
            5000)
        {
            Unit = "ms",
        };

        /// <summary>Whether the Volumes menu lists volumes the Finder hides (macOS).</summary>
        public static readonly BoolSetting ShowHiddenVolumes = new(
            ScanningKey,
            "showHiddenVolumes",
            "Show hidden volumes",
            "List the volumes the Finder hides, such as Preboot, Recovery and VM, in the Volumes menu.",
            false,
            SettingPlatform.MacOS);
    }

    /// <summary>
    /// Duplicate search settings.
    /// </summary>
    public static class Duplicates
    {
        /// <summary>The largest file hashed whole during the search, in MiB; zero samples every file.</summary>
        public static readonly IntSetting HashUpToMiB = new(
            DuplicatesKey,
            "hashUpToMiB",
            "Hash files up to",
            "Files up to this size are confirmed by hashing them whole. Larger files are compared by samples and can be verified by hash afterwards. 0 samples every file.",
            1,
            0,
            1024)
        {
            Unit = "MiB",
        };

        /// <summary>The number of samples compared for each larger file.</summary>
        public static readonly IntSetting SampleCount = new(
            DuplicatesKey,
            "sampleCount",
            "Samples per file",
            "How many samples of each larger file are compared, spread evenly from its start to its end.",
            64,
            2,
            1024)
        {
            Unit = "samples",
        };

        /// <summary>The length of each sample, in KiB.</summary>
        public static readonly IntSetting SampleSizeKiB = new(
            DuplicatesKey,
            "sampleSizeKiB",
            "Sample size",
            "The length of each sample.",
            4,
            1,
            1024)
        {
            Unit = "KiB",
        };

        /// <summary>The number of files read at once; zero chooses automatically.</summary>
        public static readonly IntSetting Readers = new(
            DuplicatesKey,
            "readers",
            "Files read at once",
            "Automatic reads up to 4 files at once. Use 1 for spinning hard disks, which slow down when reading several files at once.",
            0,
            0,
            32)
        {
            Unit = "files",
            ZeroMeans = "Automatic",
        };

        /// <summary>Whether sets matched only by samples are verified by hash as soon as the results appear.</summary>
        public static readonly BoolSetting VerifyAutomatically = new(
            DuplicatesKey,
            "verifyAutomatically",
            "Verify sampled sets automatically",
            "As soon as the results appear, hash the sets that only matched on samples, as Verify All by Hash does.",
            false);

        /// <summary>The length, in MiB, files must exceed to count in the Potential duplicate space pane.</summary>
        public static readonly IntSetting EstimateOverMiB = new(
            DuplicatesKey,
            "estimateOverMiB",
            "Estimate duplicate space among files over",
            "The Potential duplicate space pane counts files larger than this that share their size with another file, from the scan alone.",
            1,
            0,
            1024)
        {
            Unit = "MiB",
        };
    }

    /// <summary>
    /// Deleting settings.
    /// </summary>
    public static class Deleting
    {
        /// <summary>Deleting more files than this asks first.</summary>
        public static readonly IntSetting ConfirmAboveFiles = new(
            DeletingKey,
            "confirmAboveFiles",
            "Ask before deleting more than",
            "Deleting more files than this, or more data than the size below, asks first, whether to the Trash or Recycle Bin or permanently. 0 always asks.",
            10,
            0,
            1_000_000)
        {
            Unit = "files",
        };

        /// <summary>Deleting more data than this many megabytes (in the size units chosen) asks first.</summary>
        public static readonly IntSetting ConfirmAboveMegabytes = new(
            DeletingKey,
            "confirmAboveMegabytes",
            "Ask before deleting more than",
            "Megabytes in the size units chosen in General. 0 always asks.",
            10,
            0,
            1_000_000)
        {
            Unit = "MB",
        };

        /// <summary>Whether Windows warns before permanently deleting items that cannot go in the Recycle Bin.</summary>
        public static readonly BoolSetting WarnWhenNotRecyclable = new(
            DeletingKey,
            "warnWhenNotRecyclable",
            "Warn before permanently deleting what the Recycle Bin can't take",
            "Items too large for the Recycle Bin, or on drives without one, are deleted permanently. When this is off, that happens without asking.",
            true,
            SettingPlatform.Windows);
    }
}
