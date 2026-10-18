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

### Duplicate finder

Tools › Find Duplicates searches everything the last scan found. It runs as a file operation, so the status bar shows
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
Duplicates window says how many sets are only sampled and asks for them to be verified before anything is deleted.
**Verify All by Hash** hashes every sampled set, largest waste first, and a row's context menu has **Verify Set by
Hash** for its set alone. Hashing confirms a set (its rows switch to Hash and show the hash, keeping the selection),
drops the files that differ, or splits the set when it held two contents. The largest group keeps the set's number
and the others get new numbers and follow it, so numbers already noted stay valid. Stop (or closing the window) ends
hashing, and sets already hashed keep their results. Copy and Export wait until it finishes.

Up to four files are read at a time, enough to keep a solid-state disk busy without making a spinning disk seek
between too many files. Files are opened for shared reading, so files other programs have open are still compared.
A file that cannot be read, or whose length changed since the scan, is reported in the results window and left out.

Sets are numbered by the space they waste (length × (copies − 1)), largest first. The Duplicates window lists one row
per file with its set, full path, size, hash and comparison, and shades every other set. Double-clicking a row shows
the file in Finder or File Explorer. **Copy CSV** and **Export CSV…** produce the same RFC 4180 text: a header row
(`SetID,Full Path,Size,Hash,Comparison`), sizes in bytes, an empty hash for sampled files, quotes only where needed,
and CR LF line ends. The exported file starts with a UTF-8 byte order mark, so spreadsheet programs read non-ASCII
paths correctly. The save dialog is the system's own, and it starts in Documents (or the Desktop if that is
unavailable).

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