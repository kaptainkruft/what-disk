# WhatDisk

WhatDisk shows where the space on a disk has gone. It scans a volume or a folder (local or network) and presents the
results as a sortable tree with usage bars, a pie chart of the selected folder and a details pane, all updating live
while the scan runs.

It is a cross-platform .NET 10 / C# 14 application built with [Avalonia 12](https://avaloniaui.net/): a shared
application plus a **macOS** head and a **Windows** head, each using its platform's native APIs. A Linux head is
planned (see [Roadmap](#roadmap)).

## Features

- **One window, four areas.** A toolbar to choose what to scan, the results tree (about 80% of the width), and a right
  pane split into a pie chart of file types (top) and the details of the selected item (bottom). A status bar shows
  progress.
- **Choose what to scan.** Pick a volume from the **Volumes** menu (one at a time on macOS; tick several drives on
  Windows), type a path (`~` is expanded on macOS; `%VARIABLES%` and quoted paths from File Explorer's "Copy as path"
  are accepted on Windows), or use **Browse…** for the system folder dialog. Mounted network shares and mapped network
  drives appear in the volume list, and `\\server\share` paths can be typed on Windows. A path given on the command line
  is scanned on start-up.
- **Fast, multi-threaded scanning** with the most efficient API on each platform. On macOS that is
  `getattrlistbulk`, which returns names, types, sizes, allocation and identity for hundreds of entries per system call;
  on Windows it is `GetFileInformationByHandleEx`, which does the same in 64 KB batches.
- **Live results.** Every 200 ms the tree, chart, details and status bar are refreshed from the scan in progress. Each
  scanned root is expanded automatically, so its first-level children are visible from the start.
- **The tree.** Columns: Name (expandable, with Finder or File Explorer icons), % of Disk and % of Parent (both with left-to-right
  bars), Size, Files and Folders. Click a header to sort; drag a header edge to resize. Folders still being scanned are
  shown in italics, and folders that could not be read carry a warning icon.
- **Per-volume results.** When a scan spans volumes (for example `/` on macOS, which spans *Macintosh HD*, *Macintosh
  HD – Data*, *Preboot*, *VM* and any external disks, or a Windows folder with a drive mounted in it), each volume
  becomes its own top-level item, added as it is discovered. The place where it is mounted is shown as a placeholder that links to it, so nothing is counted twice.
- **Accurate sizes.** Sizes are the space actually allocated on disk (so compressed, sparse and cloud-only files are
  counted correctly), hard links are counted once on macOS (on Windows, as in File Explorer, each link counts), symbolic
  links and junctions are never followed, and virtual file systems
  (`/dev`, autofs, snapshots) are skipped.
- **Robust.** Unreadable or vanished folders are recorded and the scan carries on; the scan can be stopped at any time
  and the partial results stay on screen.
- **File types.** A donut chart of the five file types (by extension) taking the most space in the selected folder and
  everything beneath it, plus "other". With nothing selected it covers the whole scan. On macOS, packages such as
  `.app` bundles and Photos libraries count as one item of their own type, and types are described the way the Finder
  describes them ("PDF document"). Selecting a file highlights its type. The palette is colour-blind safe, with
  separate light and dark variants, and colours stay with a type while the scan changes the ranking.
- **Light and dark themes**, following the system setting.
- **Work with what you find.** Select several items, then open, preview, reveal, copy, rescan, move to the Trash or
  delete them from a context menu, the menu bar or the keyboard. Drag items onto another folder to move or copy them.
  See [Working with items](#working-with-items).

## Working with items

**Selecting.** Click a row to select it. Command-click (Ctrl-click on Windows) adds or removes a row, Shift-click
selects a range, Shift with the Up or Down arrow extends the selection, and ⌘A (Ctrl+A) selects every visible row. Right-clicking a row that is not selected
selects it first. When the selection contains a folder and items inside it, actions apply to the folder only. With
several items selected, the details pane shows their combined size and counts.

**Context menu and menu bar.** Right-click the selection, or press Shift+F10 (or the Menu key), for the context
menu. The same actions are in the **File**, **Edit** and **View** menus: in the macOS menu bar, or in a menu bar at the
top of the window on Windows, which also has **Help › About WhatDisk** and **File › Exit** (Alt+F opens the File
menu). The actions are enabled only while the tree has keyboard focus, so the copy and delete keys still work as usual
in the path box. The menus use each platform's words and shortcuts:

| macOS | Shortcut | Windows | Shortcut | What it does |
|---|---|---|---|---|
| Open | ⌘O or ⌘↓ | Open | Ctrl+O | Opens the items with their default applications |
| Open With | | Open With | | Lists the applications that can open the item, the default first |
| Show in Finder | ⌘R | Show in File Explorer | Shift+Alt+R | Opens a file manager window with the items selected |
| Quick Look | Space (⌘Y in the menu bar) | – | | Previews the items; press again to close the preview (macOS only) |
| Get Info | ⌘I | Properties | Alt+Enter | Opens the information window (one for several items on Windows) |
| Copy | ⌘C | Copy | Ctrl+C | Copies the items, ready to paste in the file manager |
| Copy Path | ⌥⌘C | Copy Path | Ctrl+Shift+C | Copies the full paths as text, one per line |
| Select All | ⌘A | Select All | Ctrl+A | Selects every visible row |
| Expand All Beneath | ⌥→ | Expand All Beneath | Alt+→ or numeric keypad * | Expands the selected folders and every folder inside them |
| Collapse All Beneath | ⌥← | Collapse All Beneath | Alt+← | Collapses the selected folders and every folder inside them |
| Rescan | ⇧⌘R | Rescan | F5 | Reads the selected folders again; a top-level item rescans everything |
| Move to Trash | ⌘⌫ | Move to Recycle Bin | Delete | Moves the items to the Trash or Recycle Bin |
| Delete Immediately… | ⌥⌘⌫ | Delete Permanently… | Shift+Delete | Deletes the items permanently |

The arrow keys work as before: Up and Down move, Right expands or moves to the first child, Left collapses or moves to
the parent. Return does nothing in the tree on either platform.

**Deleting.** Both kinds of delete ask first when the selection holds more than 10 MB or more than 10 files. Smaller
deletions happen straight away. There is no undo, but items moved to the Trash or Recycle Bin can be restored from it. On Windows, an item
that cannot go to the Recycle Bin (on a network share, or too large for it) is deleted only after Windows has warned
and you have agreed. Top-level
items, other volumes shown inside a folder and items on read-only volumes cannot be moved or deleted.

**Drag and drop.** Drag the selection onto a folder or volume row:

- Within one volume the items are moved. Onto another volume they are copied.
- Hold Option (⌥) to copy instead, or Command (⌘) to move instead; on Windows, Ctrl copies and Shift moves, as in File
  Explorer. The pointer shows which will happen.
- The folder under the pointer is highlighted. Hovering over a collapsed folder for a second opens it, and dragging
  near the top or bottom edge scrolls the tree.
- Dropping onto a file, onto the item itself or a folder inside it, or moving an item into the folder it is already in,
  is refused. Press Escape to cancel a drag.
- When a name is already taken, WhatDisk asks whether to replace the existing item, keep both (the new one is given a
  numbered name such as "report 2.pdf"), skip the item or stop. "Apply to all" answers for the rest of the operation.
- Progress shows in the status bar, and **Stop** cancels. The tree, chart and totals update in place without a
  rescan; copied folders are read into the tree at their new place.

Dragging works only within WhatDisk; items cannot be dragged to or from the Finder or File Explorer.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later; `global.json` rolls forward to the latest
  installed feature band).
- macOS 12 or later on Apple silicon or Intel to run the macOS head; Windows 10 or 11 on x64 or Arm64 to run the
  Windows head.
- To publish (both heads publish as Native AOT executables, which must be built on their own operating system): Xcode
  Command Line Tools on macOS; Visual Studio 2022 or its Build Tools with the "Desktop development with C++" workload
  on Windows.

## Build, run and test

```sh
# Build everything (warnings are treated as errors).
dotnet build

# Run the app, optionally scanning a path straight away.
dotnet run --project src/WhatDisk.MacOS
dotnet run --project src/WhatDisk.MacOS -- ~/Downloads
dotnet run --project src/WhatDisk.Windows                  # on Windows
dotnet run --project src/WhatDisk.Windows -- D:\Downloads

# Run all tests (xUnit v3 on Microsoft.Testing.Platform).
dotnet test

# Keep the headless UI test renders for review.
WHATDISK_SCREENSHOTS=/tmp/whatdisk-shots dotnet test --project tests/WhatDisk.Shared.Tests

# Publish a Native AOT app bundle (Apple silicon or Intel), on a Mac.
dotnet publish src/WhatDisk.MacOS -c Release -r osx-arm64
dotnet publish src/WhatDisk.MacOS -c Release -r osx-x64

# Publish a Native AOT executable (x64 or Arm64), on Windows.
dotnet publish src/WhatDisk.Windows -c Release -r win-x64
dotnet publish src/WhatDisk.Windows -c Release -r win-arm64
```

Publishing writes `WhatDisk.app` (about 34 MB, ad-hoc signed) to
`src/WhatDisk.MacOS/bin/Release/net10.0/<runtime>/publish/`, or `WhatDisk.exe` and the native libraries it loads
(Skia, HarfBuzz and ANGLE) to `src/WhatDisk.Windows/bin/Release/net10.0/<runtime>/publish/`. Add `-p:PublishAot=false`
for a conventional self-contained build instead (which can also be built for Windows on a Mac).

The command line is `WhatDisk [path] [--exit-when-done]`. With `--exit-when-done` the app scans the path, prints the
final status line and exits with code 0 when the scan completed, which is handy for scripted smoke tests:

```sh
dotnet run --project src/WhatDisk.MacOS -- /Applications --exit-when-done
```

On Windows the summary appears in the console the app was started from, or in the file or pipe its output is
redirected to. `cmd` does not wait for a windowed program, so use `start /wait WhatDisk.exe C:\Users --exit-when-done`
there to wait for the exit code.

## Versioning and releases

WhatDisk uses [semantic versioning](https://semver.org). **Git tags are the source of truth**: every tag looks like
`vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-LABEL` (for example `v0.1.3-alpha`). The project is in prerelease, so
versions are `0.x` with the `alpha` label.

Releases are built by the **Release pipeline** workflow (Actions tab › Release pipeline › Run workflow). Its options:

| Option | Meaning |
|---|---|
| Bump | `patch` (default), `minor` or `major`. The pipeline takes the highest version among the tags and increases that part. |
| Prerelease | The prerelease label, `alpha` by default. Leave it empty for a final release. |
| Create release | Publish a GitHub release with the packages attached. |

Every successful run tags the built commit, so each build has its own number and the next run continues from it.
The version and release time (UTC) are compiled into the application and shown in **WhatDisk › About WhatDisk**.
Local builds are versioned `0.0.0-dev` and show "Development build".

The pipeline is composed of reusable workflows, each running as a matrix over the platforms listed in
`.github/platforms.json`: macOS on Apple silicon and on Intel, and Windows on x64 and on Arm64. Each is built, smoke
tested and tested on its own kind of runner (`macos-latest`, `macos-15-intel`, `windows-latest` and
`windows-11-arm`). Every runner runs every test project; tests that need another operating system skip.

| Workflow | Does |
|---|---|
| `pipeline.yml` | Computes the version, then runs the four workflows below |
| `build.yml` | Builds each platform with `build/<scripts>/publish.sh`, then starts the app to scan a folder with `build/<scripts>/smoke-test.sh` |
| `test.yml` | Runs all tests on each platform |
| `package.yml` | Packages each build with `build/<scripts>/package.sh` (on the platform's `package-runner` when it names one) |
| `release.yml` | Tags the commit and, if requested, publishes the GitHub release |
| `verify.yml` | Run on its own: builds, smoke tests, tests and packages every platform as `0.0.0-ci` without tagging or releasing, to try a branch first |

On macOS the build is a Native AOT `WhatDisk.app`, packaged separately for each kind of Mac as
`WhatDisk-<version>-macos-apple-silicon.dmg` and `WhatDisk-<version>-macos-intel.dmg`. Each disk image shows the app
beside an Applications shortcut, so it installs by dragging.

On Windows the build is a Native AOT `WhatDisk.exe` with its native libraries, packaged for each architecture in two
forms: `WhatDisk-<version>-windows-x64.zip` (unzip anywhere and run `WhatDisk.exe`) and
`WhatDisk-<version>-windows-x64-setup.exe`, an [Inno Setup](https://jrsoftware.org/isinfo.php) installer that installs
for the current user without administrator rights, with a Start menu entry and an uninstaller (likewise `windows-arm64`).
Neither is code-signed yet, so Windows SmartScreen asks for confirmation the first time. Every package has a `.sha256`
checksum beside it.

The same steps run locally:

```sh
VERSION_PREFIX=0.1.3 VERSION_SUFFIX=alpha build/macos/publish.sh arm64 out/app
build/macos/package.sh out/app 0.1.3-alpha macos-apple-silicon out/package
.github/scripts/next-version.sh patch alpha    # what the next pipeline run would build
.github/scripts/test-next-version.sh           # tests for the version script

# On Windows, in Git Bash:
VERSION_PREFIX=0.1.3 VERSION_SUFFIX=alpha build/windows/publish.sh x64 out/app
build/windows/smoke-test.sh out/app
build/windows/package.sh out/app 0.1.3-alpha windows-x64 out/package
```

Adding a platform means adding `publish.sh`, `smoke-test.sh` and `package.sh` scripts under `build/<scripts>/` and an
entry in `.github/platforms.json` naming the scripts folder, architecture and runner (and, optionally, a different
`package-runner`). Every workflow step runs in bash, which Windows runners provide through Git Bash.

## Project layout

```text
what-disk/
├── WhatDisk.slnx                 Solution file (XML format)
├── global.json                   SDK version and the Microsoft.Testing.Platform test runner
├── Directory.Build.props         Settings for every project: net10.0, latest C#, nullable, analyzers,
│                                 warnings as errors, XML documentation
├── Directory.Packages.props      Central package versions
├── .editorconfig                 Code style and analyzer configuration
├── .github/
│   ├── platforms.json            The platforms the release pipeline builds (its matrix)
│   ├── scripts/                  next-version.sh (versions from tags) and its tests
│   └── workflows/                pipeline, build, test, package and release workflows
├── build/
│   ├── macos/                    publish.sh (app bundle for one architecture), smoke-test.sh, package.sh (disk image)
│   ├── windows/                  publish.sh (Native AOT executable for one architecture), smoke-test.sh,
│   │                             package.sh (zip and installer), WhatDisk.iss (Inno Setup script),
│   │                             pe-machine.sh (checks an executable's architecture)
│   └── smoke-test-common.sh      Runs the app with --exit-when-done and checks the result
├── src/
│   ├── WhatDisk.Shared/          The shared application: everything that is not platform specific
│   │   ├── App.axaml(.cs)        The Avalonia application and its theme
│   │   ├── Scanning/             Scan engine: the scan tree, multi-threaded session, work queue, planner,
│   │   │                         volume-boundary rules and the portable System.IO enumerator
│   │   ├── Volumes/              Volume and mount-table model, file-system classification, portable volume service
│   │   ├── Platform/             Contracts a platform head implements (icons, folder picker, file manager
│   │   │                         actions, capabilities and wording, shortcut schemes) and path helpers
│   │   ├── FileOperations/       Moving, copying and deleting items, with conflict handling and progress
│   │   ├── Charting/             File-type totals and the pie chart model: top types, "other", stable colours
│   │   ├── Formatting/           Byte sizes, percentages, counts, durations and timestamps
│   │   ├── ViewModels/           MVVM view models: main window (with the item actions), tree rows and their
│   │   │                         minimal-change reconciliation, chart, details, volume picker and dialogs
│   │   ├── Views/                Main window and menu bar, tree list (context menu, keys, drag and drop),
│   │   │                         chart pane, details pane, volume picker, question dialogs and About window
│   │   │   └── Controls/         Custom controls: tree row panel and column layout, percentage bar,
│   │   │                         pie chart, legend swatch, platform icon
│   │   ├── Icons/                Background icon cache and built-in vector icons
│   │   ├── Themes/               Light and dark palettes, styles
│   │   └── Hosting/              Start-up: dependency injection, app builder, command-line options and
│   │                             version information
│   ├── WhatDisk.MacOS/           The macOS head: entry point and macOS-only services
│   │   ├── Program.cs            Entry point: Avalonia's native Cocoa backend plus the macOS services
│   │   ├── MacFileSystemEnumerator.cs   Directory reading with getattrlistbulk
│   │   ├── AttributeBufferParser.cs     Decodes getattrlist buffers (pure code, tested on any OS)
│   │   ├── MacVolumeService.cs          Mounted volumes with names, physical devices and mount flags
│   │   ├── MacVolumeFacts.cs            Decodes and classifies volume attributes (pure code)
│   │   ├── MacIconProvider.cs           Finder icons through NSWorkspace
│   │   ├── MacFileTypeProvider.cs       Packages and file type descriptions (uniform type identifiers)
│   │   ├── MacShellService.cs           Trash, open, Open With, reveal, Get Info, Quick Look and the pasteboard
│   │   ├── MacPlatformServiceCollectionExtensions.cs   Registers the macOS services and conventions
│   │   └── Interop/              libc and Objective-C runtime bindings, errno mapping, constants
│   └── WhatDisk.Windows/         The Windows head: entry point and Windows-only services
│       ├── Program.cs            Entry point: Avalonia's Win32 backend plus the Windows services
│       ├── app.manifest          Windows 10/11, per-monitor DPI, long paths, modern common controls
│       ├── WindowsFileSystemEnumerator.cs   Directory reading with GetFileInformationByHandleEx
│       ├── DirectoryEntryParser.cs          Decodes directory listings and classifies reparse points (pure code)
│       ├── WindowsVolumeService.cs          Volumes with all their drive letters and mounted folders, network
│       │                                    and subst drives
│       ├── WindowsVolumeFacts.cs            Names, kinds and the volume list, as File Explorer shows them (pure code)
│       ├── WindowsIconProvider.cs           Shell icons (SHGetFileInfo and the system image lists)
│       ├── WindowsFileTypeProvider.cs       Type names registered with Windows ("Text Document")
│       ├── WindowsShellService.cs           Recycle Bin, Open With, File Explorer, Properties and the clipboard
│       ├── WindowsClipboard.cs, ShellItems.cs   Clipboard writing and shell item lists
│       ├── WindowsPaths.cs, IconPixels.cs, ClipboardFormats.cs   Paths, icon pixels and clipboard data (pure code)
│       ├── WindowsPlatformServiceCollectionExtensions.cs   Registers the Windows services and conventions
│       └── Interop/              kernel32, shell32, user32, gdi32, ole32 bindings; COM calls, STA threads,
│                                 Win32 error mapping
└── tests/
    ├── WhatDisk.Shared.Tests/    Scan engine, volumes, formatting, chart, view models, headless UI
    │                             rendering and documentation coverage
    ├── WhatDisk.MacOS.Tests/     Buffer parsing (any OS) and macOS integration tests against the real
    │                             file system, volumes, disk images and NSWorkspace
    └── WhatDisk.Windows.Tests/   Directory listing, path, volume, icon and clipboard helpers (any OS), and
                                  Windows integration tests against the real file system, volumes and shell
```

## Architecture

### Shared project and platform heads

Almost all code lives in `WhatDisk.Shared`. A platform head contains only its entry point and the services that need
the operating system:

| Contract | Purpose | macOS implementation | Windows implementation |
|---|---|---|---|
| `IFileSystemEnumerator` | Read one directory's entries | `MacFileSystemEnumerator` (`getattrlistbulk`) | `WindowsFileSystemEnumerator` (`GetFileInformationByHandleEx`) |
| `IVolumeService` | List volumes and capture the mount table | `MacVolumeService` (`DriveInfo` + `getattrlist`) | `WindowsVolumeService` (`FindFirstVolume`, `GetVolumePathNamesForVolumeName`) |
| `IIconProvider` | Platform icons for items and volumes | `MacIconProvider` (`NSWorkspace`) | `WindowsIconProvider` (`SHGetFileInfo`) |
| `IFileTypeProvider` | Which folders are single items (packages) and how types are described | `MacFileTypeProvider` (uniform type identifiers) | `WindowsFileTypeProvider` (registered type names) |
| `IShellService` | Trash, open, Open With, reveal, information window, preview, clipboard | `MacShellService` (`NSWorkspace`, `NSFileManager`, `NSPasteboard`) | `WindowsShellService` (`SHFileOperation`, `SHAssocEnumHandlers`, `SHOpenFolderAndSelectItems`, `SHObjectProperties`, the clipboard) |
| `PlatformCapabilities` | Conventions: volumes per scan, path comparison, byte units, the file manager's words, the shortcut scheme, whether there is an application menu | macOS values | Windows values |

The head registers these with `Microsoft.Extensions.DependencyInjection` before the shared defaults, which only fill in
what the head did not provide. It also chooses its own Avalonia backend (`UseAvaloniaNative().UseSkia().UseHarfBuzz()`
on macOS, `UseWin32().UseSkia().UseHarfBuzz()` on Windows), so each head ships only the native code it needs.

The context menu and menus are shared: one set of actions, named and bound to keys by `PlatformCapabilities`
(`ShortcutScheme.MacOS` or `ShortcutScheme.Windows`, "Finder" or "File Explorer", "Trash" or "Recycle Bin", "Get
Info" or "Properties"), and backed by the head's `IShellService`. Actions a platform lacks (Quick Look on Windows) are
hidden. The system's own context menu is not used on either platform.

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

### File operations

- Every action works on the topmost selected items, so a folder and a file inside it are never processed twice.
- Moves within a volume are renames. Other moves copy, then delete the original; a partly copied item is removed if
  the copy fails or is stopped. Symbolic links are copied as links and never followed.
- The scan tree is edited in place: a deleted or moved node is detached and its size and counts are subtracted from
  every ancestor; a moved node is attached to its new folder; a copied folder is scanned into its destination by a
  small scan session whose completion stops at that folder. Only the affected folders' chart totals are recomputed.
- The view model asks questions through `IDialogService`, so tests answer them without windows. File manager actions
  go through `IShellService`, which reports what the platform supports so unsupported actions are hidden.

### Windows specifics

- Directories are read with `GetFileInformationByHandleEx` and the `FileIdExtdDirectoryInfo` class, which gives each
  entry's size, allocated size, attributes, reparse tag and 128-bit file ID in 64 KB batches. File systems without it
  (FAT, some network servers) fall back to `FileIdBothDirectoryInfo` and then `FileFullDirectoryInfo`. Paths use the
  `\\?\` form, so folders deeper than 260 characters are read.
- Symbolic links and junctions are links and never followed (which also avoids the compatibility junctions in user
  profiles). A junction that mounts another volume is a mount point, so the volume becomes its own top-level item.
  OneDrive placeholders and deduplicated files are ordinary files counted at their allocated size, so online-only
  files take no space.
- Hard links cannot be detected in a directory listing without opening every file, so each link is counted, as in File
  Explorer. Folders such as `C:\Windows\WinSxS`, which share files with `System32`, therefore look larger than the
  space they free.
- Volumes come from `FindFirstVolume` with all their drive letters and mounted folders. Mapped network drives are named
  after their share (`media (\\nas) (Z:)`), and `subst` drives are partial views of the volume they point into.
- Icons and type names come from the shell (`SHGetFileInfo`, with the 48 and 256 pixel system image lists on high-DPI
  screens) on background threads. Shell calls that show windows run in a single-threaded apartment, and the few COM
  interfaces used (`IAssocHandler`, `IImageList`) are called through their virtual tables, which keeps the head free of
  runtime-generated COM wrappers and compatible with Native AOT.
- Move to Recycle Bin uses `SHFileOperation` with undo, so items can be restored. Copy puts a file list on the clipboard
  that File Explorer pastes.

### macOS specifics

- `getattrlistbulk` is called with `FSOPT_RETURN_REALDEV`, so the system and data volumes of the startup disk (which
  share a logical device number) can be told apart. Mount points and firmlinks are reported per entry.
- Volume names, physical device numbers and mount flags come from `getattrlist`. Volumes the Finder hides (Preboot,
  VM and the like) are left out of the volume list, and snapshots are treated as virtual.
- Icons come from `NSWorkspace` through the Objective-C runtime: per path for volumes, application bundles and
  special folders, per file type otherwise. They load on background threads and are cached.
- Move to Trash uses `NSFileManager`, so the Finder's Put Back works. Show in Finder, Open With and the pasteboard
  use `NSWorkspace` and `NSPasteboard`; Get Info asks the Finder through AppleScript, and Quick Look runs `qlmanage`.

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

`dotnet test` runs 354 tests across three projects (on macOS, 32 Windows-only tests skip; on Windows, the macOS-only ones do).

- **Scan engine:** an in-memory file system drives aggregation, allocated sizes, partial failures, symbolic links,
  hard links, cancellation, mount points, firmlinks, virtual and network volumes, and a parallel stress test.
- **View models:** tree reconciliation (including randomised tests), the chart model and the main window. Fake time
  checks the 200 ms refresh while a scan is still running.
- **Item actions:** deleting, moving, copying across volumes, name conflicts and rescanning run against real files in
  a temporary folder, with a recording shell service and scripted dialogs. The 10 MB and 10-file confirmation
  threshold and the drop rules are checked case by case.
- **Headless UI:** the main window is rendered with Skia in light and dark themes after a real scan. The pane
  proportions, header sorting, selection and the icon pipeline are checked. Real key presses, clicks and drag events
  check the arrow keys, shortcuts, focus tracking, the context menu, range selection and drag and drop.
- **Windows:** directory listings in all three layouts are built byte by byte and decoded, including every kind of
  reparse point, on any OS; so are paths, volume names, icon pixels and clipboard data. On Windows, the enumerator is
  compared with System.IO (including junctions, long paths and directories larger than one batch), and the volume
  service, Recycle Bin, Open With, clipboard, icons and type names are exercised for real. Shared view tests run with
  both platforms' shortcuts and words.
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

## Notes for Windows users

- WhatDisk reads what your account can read. Folders such as `System Volume Information` and other users' profiles are
  marked as unreadable; run WhatDisk as administrator to include them.
- To scan a network share, map it to a drive letter (it then appears in the **Volumes** list) or type its path, for
  example `\\nas\media`.
- The installer installs for the current user only and needs no administrator rights. The zip runs from anywhere.

## Roadmap

- **Linux head:** `/proc/self/mountinfo` volumes, `statx`-based enumeration and freedesktop icon themes. The shared
  contracts listed above are ready for it.
- **Windows:** an application icon, code signing for the executable and installer, optional hard-link detection (for
  example through the NTFS master file table, which also allows much faster scans when running as administrator), and
  dragging items to and from File Explorer.

## License

MIT; see [LICENSE](LICENSE).
