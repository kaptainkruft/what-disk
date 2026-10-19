# WhatDisk

WhatDisk shows where the space on a disk has gone. It scans a volume or a folder (local or network) and presents the results as a sortable tree with usage bars, a pie chart of the selected folder and a details pane, all updating live while the scan runs.

While a scan runs, a narrow **Potential duplicate space** pane under the chart estimates, from sizes alone, how much space files over 1 MiB that share their size with another file take beyond one copy each, for the folder the chart shows. The **Duplicates** tab, which is always there, lists those files that share a size as the scan finds them (smaller files too, on request). After the scan, its **Sampled Scan** (also **Tools › Find Duplicates in Scanned Files**, ⇧⌘D, Ctrl+Shift+D) compares their contents: files up to 1 MiB are confirmed by hash, and larger files are matched quickly by samples. **Full Hash Scan** hashes every set not yet confirmed by hash. **Find Duplicates in Selection** (⌘D, Ctrl+D, also in the context menu) compares the selected folders and files as one set, so two suspect folders can be compared, and opens its results in a tab of their own. Results are listed by set, ready to copy or export as CSV. Double-click a file to show it in Finder or File Explorer. The tabs run along the bottom of the window, above the status bar.

Settings (**WhatDisk › Settings…**, ⌘, on macOS; **Tools › Settings…**, Ctrl+, on Windows) covers appearance, size units, scanning, the duplicate search, delete confirmations and, on Windows, whether the context menu offers File Explorer's full menu (**Show More Options**, or Shift+right-click). They are saved as JSON that holds only what differs from the defaults, in `~/Library/Application Support/WhatDisk/settings.json` on macOS and `%APPDATA%\WhatDisk\settings.json` on Windows.

It is a MacOS and Windows cross-platform .NET 10 / C# 14 application built with [Avalonia 12](https://avaloniaui.net/).

## License

MIT; see [LICENSE](LICENSE).

## Requirements

macOS 12 or later on Apple silicon or Intel to run the macOS version; Windows 10 or 11 on x64 to run the Windows version.

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
│   │   ├── Duplicates/           The duplicate finder (length, then XXH128 hashes up to 1 MiB and samples above),
│   │   │                         set verification by hash, and the CSV
│   │   ├── Settings/             The settings catalogue (sections, keys, defaults, ranges) and the JSON settings file
│   │   ├── Charting/             File-type totals and the pie chart model: top types, "other", stable colours
│   │   ├── Formatting/           Byte sizes, percentages, counts, durations and timestamps
│   │   ├── ViewModels/           MVVM view models: main window (with its tabs, the item actions and Find Duplicates), tree
│   │   │                         rows and their minimal-change reconciliation, chart, details, volume picker,
│   │   │                         duplicate results and dialogs
│   │   ├── Views/                Main window (menu bar, tab strip, in-window dialogs), tree list (context menu,
│   │   │                         keys, drag and drop), chart, details and potential duplicates panes, volume
│   │   │                         picker, duplicate results and settings tabs, questions and About box
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
