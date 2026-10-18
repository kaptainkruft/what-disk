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

### One window, with tabs

Everything the app shows lives in its one window. The main window has a tab strip under the menu bar:

- **Scan** is always first and cannot be closed. It holds the toolbar for choosing what to scan, the tree, the chart,
  the Potential duplicate space pane and the details.
- **Each duplicate search** adds a tab with its results ("Duplicates in Photos"; the tooltip has the full scope) and
  shows it. Closing the tab stops any hashing it is doing.
- **Settings** opens in a tab too, one at most; opening it again shows the existing tab.
- Close Tab (⌘W on macOS, Ctrl+W on Windows, and the × on each tab) closes any tab but Scan and shows the one before.

The tabs are view models (`TabViewModel`: the Scan tab, `DuplicateResultsViewModel` and `SettingsViewModel`) in the
main view model's `Tabs` collection, so tests drive them without a window. Every tab's page stays in place while
hidden, so it keeps its scroll position, column widths and selection. Leaving the Scan tab makes the tree inactive, so
the item commands and their shortcuts do not act on a tree that cannot be seen; coming back gives the tree focus again.
A status bar along the bottom of the window shows scan and operation progress whichever tab is shown.

Questions (delete confirmations, name conflicts, Restore Defaults) and the About box appear inside the window too, as
a card over the dimmed, disabled content, rather than in windows of their own. Escape answers with the cancel button
and Return with the default one. While one is showing, the commands wait (so shortcuts in the macOS menu bar cannot
act behind it), and focus returns where it was afterwards.

The app keeps to one window because Avalonia's support for several windows' menus on macOS is unreliable on macOS
27: when a window closes while it is key, the menu bar is left with only the application menu, and a window's menus
cannot be replaced once set. The only windows besides the main one are the system's own panels: the folder picker and
the save dialog.

### Menus

The menus are set up exactly as Avalonia's documentation describes, and nothing works around Avalonia's behaviour:

- The application menu (About and Settings…; Avalonia adds Hide, Show All and Quit) is `NativeMenu.Menu` on the
  `Application` in `App.axaml`.
- The window's menus (File, Edit, View, Tools and, on Windows, Help) are `NativeMenu.Menu` on the `Window` in
  `MainWindow.axaml`, bound to the view model's commands. A `NativeMenuBar` draws them in the window where there is
  no system menu bar. They are declared once and never changed: headers and visibility depend only on the platform
  (macOS drops the access key underscores itself), and enabled states follow the commands.
- Nothing is rebuilt while the app runs. In particular, Open With is only in the context menu (an Avalonia popup), not
  in the menu bar: a submenu filled from `NeedsUpdate` would be rebuilt whenever macOS checked key equivalents, on
  almost every ⌘-key press, and Avalonia then sends the whole window menu to the menu bar again.
- The app sets no `MacOSPlatformOptions`, except `DisableNativeMenus` when **Settings › General › Use the macOS
  menu bar** (`general.macos.useMenuBar`) is turned off. That option is read at start-up. With it, Avalonia sets
  nothing in the macOS menu bar and the window draws its menus, as on Windows, adding Quit WhatDisk (⌘Q), Settings
  and About.

Avalonia still swaps `NSApp.mainMenu` itself whenever the window gains or loses key status: it shows the window's
menus as the window becomes key and only the application menu as it resigns (`AvnWindow.mm`). On macOS 27 this shows
as a brief flash of the application-only menu when clicking away to another app, and sometimes a menu bar that is not
redrawn until the pointer moves over it when clicking back. These are Avalonia's
([#22285](https://github.com/AvaloniaUI/Avalonia/issues/22285); see also
[#22286](https://github.com/AvaloniaUI/Avalonia/issues/22286) and
[#22287](https://github.com/AvaloniaUI/Avalonia/issues/22287)) and are left alone here until Avalonia fixes them.
What moving off Avalonia would take is assessed in [Replacing Avalonia](AvaloniaReplacement.md).

### Why Avalonia

The app needed a desktop UI written in .NET 10 and C#, with one shared project and thin platform heads for macOS,
Windows and Linux, a custom-drawn tree with bars, a pie chart, Native AOT builds, and UI tests that run anywhere.

- **Avalonia** runs on all three platforms from one XAML codebase, draws its own controls with Skia (so the tree's
  rows, bars and chart look and behave the same everywhere), supports Native AOT with compiled bindings, lets each
  head choose its own backend (`UseAvaloniaNative` on macOS, `UseWin32` on Windows), and has a headless platform that
  renders real windows in tests on any OS. It also offers native menu bar export on macOS.
- **.NET MAUI** has no Linux desktop support, and runs on macOS through Mac Catalyst, an iPad-style app model.
- **Uno Platform** covers all three through its Skia desktop target and would have worked. It follows WinUI's APIs
  and project structure, which suit a Windows-first app more than this one.
- **WPF, WinForms and WinUI** run on Windows only. **Eto.Forms** wraps each platform's native widgets, which suits
  standard forms but makes a custom tree and chart harder to keep consistent. **Electron or Tauri** with a .NET back
  end would mean writing the interface in a web stack and shipping a browser engine.

The costs, found along the way: controls are drawn by Avalonia, so they look like Avalonia's Fluent theme rather than
each system's own; `TreeDataGrid` needs a commercial licence from version 12, so the tree is built on a `ListBox`;
and the macOS menu bar integration is thin and buggy on macOS 27, which led to the single-window design above.

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

- The **Potential duplicate space** pane, between the chart and the details, covers the same folder as the chart and
  is computed in the same background pass. `PotentialDuplicateAggregator` groups the regular files over 1 MiB (the
  `duplicates.estimateOverMiB` setting) by length. For each length shared by two or more files, the space they take
  on disk beyond the smallest copy counts as potentially wasted. Extra hard links, cloud-only files and smaller files
  are left out, and no file is read, so it is a dirty, size-only list that Find Duplicates later confirms by content.
  Like the file-type totals, the groups of large completed folders are cached and forgotten when their contents
  change, so the estimate follows the scan as it runs and stays cheap afterwards. The pane shows the total, a bar of its share of the
  folder's size (in the chart's first colour, with the percentage inside), and an info button that explains what was
  counted.

### File operations

- Every action works on the topmost selected items, so a folder and a file inside it are never processed twice.
- Moves within a volume are renames. Other moves copy, then delete the original; a partly copied item is removed if
  the copy fails or is stopped. Symbolic links are copied as links and never followed.
- The scan tree is edited in place: a deleted or moved node is detached and its size and counts are subtracted from
  every ancestor; a moved node is attached to its new folder; a copied folder is scanned into its destination by a
  small scan session whose completion stops at that folder. Only the affected folders' chart totals are recomputed.
- The view model asks questions through `IDialogService`, so tests answer them without a window. File manager actions
  go through `IShellService`, which reports what the platform supports so unsupported actions are hidden.

### Duplicate finder

A duplicate search takes a scope: the folders and files whose files it compares, as one set. **Tools › Find
Duplicates in Scanned Files** (⇧⌘D on macOS, Ctrl+Shift+D on Windows) uses the scan's top-level items. **Find
Duplicates in Selection** (⌘D, Ctrl+D; also in the context menu) uses the selected items, keeping only the topmost of
nested ones. It is enabled when the selection could hold duplicates: a folder, or more than one item. Selecting two
folders therefore finds copies between them as well as within each; the finder itself only sees one list of files.
The results open in a new tab titled with the scope. A search runs as a file operation, so the status bar shows
its progress and Stop cancels it. While it runs, the volume picker, path box, Browse and Scan are disabled, because a
new scan would replace the tree being searched. It reads as little as it can:

1. **Length.** The scanned regular files are grouped by length from the scan tree alone. Only lengths shared by two or
   more files go further, which usually leaves a small part of the scan. Empty files, cloud-only files (`Offline`,
   which reading would download) and hard links the scan already recognised are left out. Before any file is read,
   its identity (device and file number, from the head's enumerator) is checked, so hard links to the same data are
   read once. This matters on Windows, where the scan cannot detect hard links.
2. **Files up to 1 MiB are hashed.** They are quick to read, so each is read in full and hashed with XXH128
   (`System.IO.Hashing`), which runs at memory speed and makes an accidental collision vanishingly unlikely (about 1
   in 10^38 per pair). Files with the same length and hash form a set marked **Hash**.
3. **Larger files are sampled.** Sixty-four 4 KiB samples are read from each, spread evenly from the start to the last
   whole sample at the end (256 KiB per file). Files whose samples are all equal form a set marked **Sampled**, and
   the search stops there, so the results appear without reading every large file in full.

Sampling is much cheaper, but it is not proof. Measured on real folders, about 0.3% of the files that 64 × 4 KiB
samples matched were not identical: mostly signed binaries that differ only in their code signature. So the
results tab says how many sets are only sampled and asks for them to be verified before anything is deleted.
**Verify All by Hash** hashes every sampled set, largest waste first, and a row's context menu has **Verify Set by
Hash** for its set alone. Hashing confirms a set (its rows switch to Hash and show the hash, keeping the selection),
drops the files that differ, or splits the set when it held two contents. The largest group keeps the set's number
and the others get new numbers and follow it, so numbers already noted stay valid. Stop (or closing the tab) ends
hashing, and sets already hashed keep their results. Copy and Export wait until it finishes.

Up to four files are read at a time, enough to keep a solid-state disk busy without making a spinning disk seek
between too many files. Files are opened for shared reading, so files other programs have open are still compared.
A file that cannot be read, or whose length changed since the scan, is reported in the results tab and left out.

Sets are numbered by the space they waste (length × (copies − 1)), largest first. The results tab lists one row
per file with its set, full path, size, hash and comparison, and shades every other set. Double-clicking a row shows
the file in Finder or File Explorer. **Copy CSV** and **Export CSV…** produce the same RFC 4180 text: a header row
(`SetID,Full Path,Size,Hash,Comparison`), sizes in bytes, an empty hash for sampled files, quotes only where needed,
and CR LF line ends. The exported file starts with a UTF-8 byte order mark, so spreadsheet programs read non-ASCII
paths correctly. The save dialog is the system's own, and it starts in Documents (or the Desktop if that is
unavailable).

### Settings

Settings live in one JSON file with the same format on every platform: `~/Library/Application Support/WhatDisk/settings.json`
on macOS, `%APPDATA%\WhatDisk\settings.json` on Windows. Like Visual Studio Code's, it holds only what differs from
the defaults, so a default changed in a later version reaches everyone who has not chosen otherwise:

```jsonc
{
  "general": { "theme": "dark" },
  "scanning": { "threads": 8, "macos": { "showHiddenVolumes": true } },
  "duplicates": { "hashUpToMiB": 4, "readers": 1 },
  "deleting": { "confirmAboveFiles": 50, "windows": { "warnWhenNotRecyclable": false } }
}
```

- **Catalogue.** `SettingsCatalog` defines every setting once: its section and key, label and description, type
  (on/off, a number with a range and unit, or a choice), default, and platform. The sections are the file's top-level
  keys and the Settings tab's pages, in the same order. A default can follow the platform: sizes are decimal on
  macOS and binary on Windows, so choosing the platform's own units writes nothing.
- **Platform settings** sit in their section's `macos` or `windows` subsection of the file. In the Settings tab they appear
  in their section with the others, marked with an asterisk, and only on their platform. A file shared between
  platforms keeps the other platform's settings untouched.
- **Reading and writing.** `AppSettings` reads the file with comments and trailing commas allowed, as people edit it
  by hand. A value of the wrong kind counts as the default, a number out of range is brought into range, and keys
  this version does not know are kept. An unreadable file leaves every setting at its default and is kept as
  `settings.invalid.json` before it is first overwritten. Each change is saved at once, written to a temporary file
  and renamed over the settings file, and setting a value back to its default removes it (and any section left empty).
  The JSON is handled with `System.Text.Json.Nodes`, which needs no reflection, so the Native AOT builds read it too.
- **Applying.** Theme and size units apply at once: the app switches its theme variant, and the tree, chart, details
  and volume list re-format every size. Other settings are read when next needed: scan threads and the network rule
  at the next scan, the duplicate options at the next search, confirmation thresholds at the next delete, and the
  macOS volume list and Windows Recycle Bin flags by the platform services.
- **Tab.** Settings is in the application menu on macOS (⌘,) and in Tools on Windows (Ctrl+,), and opens in a tab.
  It has the sections down the left, and for each setting its label, description, path in the file, an editor, and
  Reset when it differs from the default (marked by an accent bar). Changes apply at once, so there is no OK or
  Cancel. **Open Settings File** opens the JSON in the default editor; showing the tab again, or coming back to the
  window while it is shown, re-reads it. **Restore Defaults…** asks, then resets this platform's settings.

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
- .NET's advisory file locks (`flock`, its Unix emulation of Windows file sharing) are turned off with the
  `System.IO.DisableFileLocking` runtime option, as macOS apps ignore them. Otherwise files another program holds an
  exclusive lock on, such as the SQLite databases of Chromium-based apps, could not be read by the duplicate search or
  copied.
- Move to Trash uses `NSFileManager`, so the Finder's Put Back works. Show in Finder, Open With and the pasteboard
  use `NSWorkspace` and `NSPasteboard`; Get Info asks the Finder through AppleScript, and Quick Look runs `qlmanage`.