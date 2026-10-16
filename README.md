# WhatDisk

WhatDisk shows where the space on a disk has gone. It scans a volume or a folder (local or network) and presents the
results as a sortable tree with usage bars, a pie chart of the selected folder and a details pane, all updating live
while the scan runs.

It is a cross-platform .NET 10 / C# 14 application built with [Avalonia 12](https://avaloniaui.net/). This first
iteration contains the shared application and the **macOS** head; the Windows and Linux heads are planned for a later
iteration (see [Roadmap](#roadmap)).

## Features

- **One window, four areas.** A toolbar to choose what to scan, the results tree (about 80% of the width), and a right
  pane split into a pie chart of file types (top) and the details of the selected item (bottom). A status bar shows
  progress.
- **Choose what to scan.** Pick a volume from the **Volumes** menu (one at a time on macOS), type a path (`~` is
  expanded), or use **Browse…** for the system folder dialog. Mounted network shares appear in the volume list and can
  be scanned like any folder. A path given on the command line is scanned on start-up.
- **Fast, multi-threaded scanning** with the most efficient API on each platform. On macOS that is
  `getattrlistbulk`, which returns names, types, sizes, allocation and identity for hundreds of entries per system call.
- **Live results.** Every 200 ms the tree, chart, details and status bar are refreshed from the scan in progress. Each
  scanned root is expanded automatically, so its first-level children are visible from the start.
- **The tree.** Columns: Name (expandable, with Finder icons), % of Disk and % of Parent (both with left-to-right
  bars), Size, Files and Folders. Click a header to sort; drag a header edge to resize. Folders still being scanned are
  shown in italics, and folders that could not be read carry a warning icon.
- **Per-volume results.** When a scan spans volumes (for example `/` on macOS, which spans *Macintosh HD*, *Macintosh
  HD – Data*, *Preboot*, *VM* and any external disks), each volume becomes its own top-level item, added as it is
  discovered. The place where it is mounted is shown as a placeholder that links to it, so nothing is counted twice.
- **Accurate sizes.** Sizes are the space actually allocated on disk (so compressed, sparse and cloud-only files are
  counted correctly), hard links are counted once, symbolic links are never followed, and virtual file systems
  (`/dev`, autofs, snapshots) are skipped.
- **Robust.** Unreadable or vanished folders are recorded and the scan carries on; the scan can be stopped at any time
  and the partial results stay on screen.
- **File types.** A donut chart of the five file types (by extension) taking the most space in the selected folder and
  everything beneath it, plus "other". With nothing selected it covers the whole scan. On macOS, packages such as
  `.app` bundles and Photos libraries count as one item of their own type, and types are described the way the Finder
  describes them ("PDF document"). Selecting a file highlights its type. The palette is colour-blind safe, with
  separate light and dark variants, and colours stay with a type while the scan changes the ranking.
- **Light and dark themes**, following the system setting.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later; `global.json` rolls forward to the latest
  installed feature band).
- macOS 12 or later on Apple silicon or Intel to run the macOS head.
- Xcode Command Line Tools to publish (the macOS head publishes as a Native AOT executable).

## Build, run and test

```sh
# Build everything (warnings are treated as errors).
dotnet build

# Run the app, optionally scanning a path straight away.
dotnet run --project src/WhatDisk.MacOS
dotnet run --project src/WhatDisk.MacOS -- ~/Downloads

# Run all tests (xUnit v3 on Microsoft.Testing.Platform).
dotnet test

# Keep the headless UI test renders for review.
WHATDISK_SCREENSHOTS=/tmp/whatdisk-shots dotnet test --project tests/WhatDisk.Shared.Tests

# Publish a Native AOT app bundle (Apple silicon or Intel).
dotnet publish src/WhatDisk.MacOS -c Release -r osx-arm64
dotnet publish src/WhatDisk.MacOS -c Release -r osx-x64
```

Publishing writes `WhatDisk.app` (about 34 MB, ad-hoc signed) to
`src/WhatDisk.MacOS/bin/Release/net10.0/<runtime>/publish/`. Add `-p:PublishAot=false` for a conventional self-contained
build instead.

The command line is `WhatDisk [path] [--exit-when-done]`. With `--exit-when-done` the app scans the path, prints the
final status line and exits with code 0 when the scan completed, which is handy for scripted smoke tests:

```sh
dotnet run --project src/WhatDisk.MacOS -- /Applications --exit-when-done
```

## Project layout

```text
what-disk/
├── WhatDisk.slnx                 Solution file (XML format)
├── global.json                   SDK version and the Microsoft.Testing.Platform test runner
├── Directory.Build.props         Settings for every project: net10.0, latest C#, nullable, analyzers,
│                                 warnings as errors, XML documentation
├── Directory.Packages.props      Central package versions
├── .editorconfig                 Code style and analyzer configuration
├── src/
│   ├── WhatDisk.Shared/          The shared application: everything that is not platform specific
│   │   ├── App.axaml(.cs)        The Avalonia application and its theme
│   │   ├── Scanning/             Scan engine: the scan tree, multi-threaded session, work queue, planner,
│   │   │                         volume-boundary rules and the portable System.IO enumerator
│   │   ├── Volumes/              Volume and mount-table model, file-system classification, portable volume service
│   │   ├── Platform/             Contracts a platform head implements (icons, folder picker, capabilities)
│   │   │                         and path helpers
│   │   ├── Charting/             File-type totals and the pie chart model: top types, "other", stable colours
│   │   ├── Formatting/           Byte sizes, percentages, counts, durations and timestamps
│   │   ├── ViewModels/           MVVM view models: main window, tree rows and their minimal-change
│   │   │                         reconciliation, chart, details and volume picker
│   │   ├── Views/                Main window, tree list, chart pane, details pane and volume picker
│   │   │   └── Controls/         Custom controls: tree row panel and column layout, percentage bar,
│   │   │                         pie chart, legend swatch, platform icon
│   │   ├── Icons/                Background icon cache and built-in vector icons
│   │   ├── Themes/               Light and dark palettes, styles
│   │   └── Hosting/              Start-up: dependency injection, app builder, command-line options
│   └── WhatDisk.MacOS/           The macOS head: entry point and macOS-only services
│       ├── Program.cs            Entry point: Avalonia's native Cocoa backend plus the macOS services
│       ├── MacFileSystemEnumerator.cs   Directory reading with getattrlistbulk
│       ├── AttributeBufferParser.cs     Decodes getattrlist buffers (pure code, tested on any OS)
│       ├── MacVolumeService.cs          Mounted volumes with names, physical devices and mount flags
│       ├── MacVolumeFacts.cs            Decodes and classifies volume attributes (pure code)
│       ├── MacIconProvider.cs           Finder icons through NSWorkspace
│       ├── MacFileTypeProvider.cs       Packages and file type descriptions (uniform type identifiers)
│       ├── MacPlatformServiceCollectionExtensions.cs   Registers the macOS services
│       └── Interop/              libc and Objective-C runtime bindings, errno mapping, constants
└── tests/
    ├── WhatDisk.Shared.Tests/    Scan engine, volumes, formatting, chart, view models, headless UI
    │                             rendering and documentation coverage
    └── WhatDisk.MacOS.Tests/     Buffer parsing (any OS) and macOS integration tests against the real
                                  file system, volumes, disk images and NSWorkspace
```

## Architecture

### Shared project and platform heads

Almost all code lives in `WhatDisk.Shared`. A platform head contains only its entry point and the services that need
the operating system:

| Contract | Purpose | macOS implementation |
|---|---|---|
| `IFileSystemEnumerator` | Read one directory's entries | `MacFileSystemEnumerator` (`getattrlistbulk`) |
| `IVolumeService` | List volumes and capture the mount table | `MacVolumeService` (`DriveInfo` + `getattrlist`) |
| `IIconProvider` | Platform icons for items and volumes | `MacIconProvider` (`NSWorkspace`) |
| `IFileTypeProvider` | Which folders are single items (packages) and how types are described | `MacFileTypeProvider` (uniform type identifiers) |
| `PlatformCapabilities` | Conventions: one or several volumes per scan, path comparison, byte units | macOS values |

The head registers these with `Microsoft.Extensions.DependencyInjection` before the shared defaults, which only fill in
what the head did not provide. It also chooses its own Avalonia backend (`UseAvaloniaNative().UseSkia().UseHarfBuzz()`
on macOS), so each head ships only the native code it needs.

### Scan engine

- A `ScanSession` runs a fixed set of dedicated worker threads (the processor count, clamped to 4–16; 32 for network
  locations, where latency dominates) that read directories from a shared last-in-first-out queue. Depth-first order
  keeps the queue small and lets subtrees finish early.
- Each directory is read exactly once. Its children are published as an immutable array; file totals are added to the
  directory and every ancestor with `Interlocked` operations. The user interface can read any node at any time without
  locks.
- A per-directory pending counter marks a folder complete when it and everything beneath it has been read.
- A failure to read one directory is classified (access denied, not found, network, I/O) and recorded on that
  directory; the scan continues. Cancelling stops the workers and leaves the partial tree intact.
- Volumes: the planner creates a top-level root for every requested path and for every volume mounted beneath one.
  While scanning, mount points, firmlinks and device changes are detected and resolved against the mount table: the
  directory is either traversed, skipped (virtual file systems, network shares by default, data already counted
  elsewhere) or scanned as a newly discovered root.

### Live user interface

- While a scan runs, a `PeriodicTimer` (driven by `TimeProvider`, so tests control time) refreshes every pane every
  200 ms on the UI thread.
- The tree is a flattened, virtualised list. Each refresh captures the visible nodes' values, re-sorts siblings only
  where a value changed, and applies the difference to the bound list with as few range operations as possible (it
  keeps the longest run of rows that are already in order). Only the rows that actually moved are touched, and the
  selection survives reordering.
- The tree list is built from a `ListBox`, a custom row panel and a column layout shared with the header. Avalonia's
  `TreeDataGrid` requires a commercial licence from version 12, so it is not used.
- The pie chart totals file sizes by type on a background thread; totals of large completed folders are cached, so
  charting stays cheap on huge trees. It follows a validated data-visualisation palette: at most five named slices plus
  "other", colours that stay with their type, slices drawn in colour-slot order so neighbouring colours are always a
  tested pair, 2 px gaps between slices, a legend, tooltips and separate light and dark colours.

### macOS specifics

- `getattrlistbulk` is called with `FSOPT_RETURN_REALDEV`, so the system and data volumes of the startup disk (which
  share a logical device number) can be told apart. Mount points and firmlinks are reported per entry.
- Volume names, physical device numbers and mount flags come from `getattrlist`. Volumes the Finder hides (Preboot,
  VM and the like) are left out of the volume list, and snapshots are treated as virtual.
- Icons come from `NSWorkspace` through the Objective-C runtime: per path for volumes, application bundles and
  special folders, per file type otherwise. They load on background threads and are cached.

## Performance

Measured on an Apple M4 Pro (12 cores, macOS 27), file-system caches warm. Both runs use the same scan engine; only
the directory enumerator differs.

| Folder | Files | `getattrlistbulk` | System.IO enumeration |
|---|---|---|---|
| `/System/Library` | 305,000 | 5.1 s | 12.3 s |
| `/Applications` | 424,000 | 0.9 s | 6.9 s |

The native enumerator is also more accurate: for `/System/Library` it reports 11.4 GB allocated, while
System.IO enumeration reports 29 GB because it cannot see allocation or recognise hard links.

## Modern C# and .NET

- C# 14 extension members (`FileSystemNodeExtensions`), the `field` keyword (`ScanOptions`) and null-conditional
  assignment.
- C# 13 partial properties with the CommunityToolkit.Mvvm source generators, and `System.Threading.Lock`.
- Primary constructors, collection expressions, list patterns and `readonly record struct`s.
- `[LibraryImport]` source-generated interop, `TimeProvider`, `FrozenDictionary` and `FrozenSet`.
- Native AOT publishing, compiled XAML bindings, central package management, the `.slnx` solution format, xUnit v3
  on Microsoft.Testing.Platform.

## Testing

`dotnet test` runs 215 tests across two projects.

- **Scan engine:** an in-memory file system drives aggregation, allocated sizes, partial failures, symbolic links,
  hard links, cancellation, mount points, firmlinks, virtual and network volumes, and a parallel stress test.
- **View models:** tree reconciliation (including randomised tests), the chart model and the main window. Fake time
  checks the 200 ms refresh while a scan is still running.
- **Headless UI:** the main window is rendered with Skia in light and dark themes after a real scan. The pane
  proportions, header sorting, selection and the icon pipeline are checked.
- **macOS:** the native enumerator is compared with System.IO on real directories, including hard links, firmlinks
  and mount points at `/`. Other tests cover volume discovery and NSWorkspace icons on background threads. One test
  attaches a real disk image inside a scanned folder.
- **Documentation coverage:** every type, member, field and enum value must have an XML documentation comment. A
  Roslyn-based test enforces this for private members too.

Tests that need macOS skip on other systems; the parsing tests run everywhere.

## Notes for macOS users

- macOS protects some folders (Desktop, Documents, Mail, Photos and others). For complete results, grant WhatDisk
  (or, when running with `dotnet run`, your terminal) **Full Disk Access** in System Settings › Privacy & Security.
  Folders that remain unreadable are marked and counted in the status bar.
- Network shares must be mounted first, for example in the Finder with **Go › Connect to Server**. They then appear
  in the **Volumes** menu. Shares mounted inside a folder you scan are listed but not scanned unless chosen directly.

## Roadmap

- **Windows head:** several drives per scan, UNC paths, `GetFileInformationByHandleEx` enumeration (allocated sizes in
  bulk) and shell icons.
- **Linux head:** `/proc/self/mountinfo` volumes, `statx`-based enumeration and freedesktop icon themes.
- The shared contracts listed above are ready for both.

## License

MIT; see [LICENSE](LICENSE).
