## Replacing Avalonia

An assessment, written on 18 October 2026, of what moving WhatDisk off Avalonia would take, now that only Windows
and macOS are supported. No decision has been made. The figures come from the code as it stood that day.

### Why it came up

Avalonia's integration with the macOS menu bar misbehaves on macOS 27, even with the menus set up exactly as
Avalonia's documentation describes (see [Menus](Architecture.md#menus)):

- Avalonia swaps `NSApp.mainMenu` itself whenever the window gains or loses key status. It shows the window's menus
  as the window becomes key, and only the application menu as it resigns (`AvnWindow.mm`).
- Clicking away to another app flashes the application-only menu before the other app's menus appear. Other apps
  don't do this.
- Clicking back into WhatDisk sometimes leaves only the application menu showing until the pointer moves over the
  menu bar. Switching with ⌘-Tab doesn't show the problem.
- With several windows, closing the key window left the menu bar with only the application menu, and a window's
  menus cannot be replaced once set (the attempt throws). WhatDisk now keeps to one window, with tabs.

These match open Avalonia issues filed on 23 September 2026 against Avalonia 12.1.2 on macOS 27. Each has a patch
attached, but none has a maintainer response or a merged fix:

- [#22285](https://github.com/AvaloniaUI/Avalonia/issues/22285): replacing NSApp's menu on every key-window change
  blanks the menu bar for about 500 ms, and can stop a menu opening after a shortcut opens a window. The reporter
  says it isn't seen on macOS 26.
- [#22286](https://github.com/AvaloniaUI/Avalonia/issues/22286): let the application menu carry the whole menu bar.
  `showAppMenuOnly` re-adds the application item at the end of the bar, so File takes its place.
- [#22287](https://github.com/AvaloniaUI/Avalonia/issues/22287): calling `NativeMenu.SetMenu` a second time on a
  window kills the process with "The menu being updated does not match".

Other projects hit the same problems. [excise #1584](https://github.com/marctjones/excise/issues/1584) had the same
crash and fixed it by keeping one menu object per window. [sharpee #485](https://github.com/ChicagoDave/sharpee/issues/485)
found its menus never appeared because they were attached to the window, not the application.

WhatDisk doesn't work around any of this. Workarounds were tried, then removed: secondary windows with menus of their
own, handing key status back before closing, and setting the main menu again through the Objective-C runtime to force
a redraw. They're Avalonia's to fix.

### Checking that the problem is Avalonia's

Run a minimal Avalonia app with nothing of WhatDisk in it on the same Mac. Use the same Avalonia version (12.1.4 with
`Avalonia.Desktop` and `Avalonia.Themes.Fluent`), `UsePlatformDetect()`, and one window. Declare menus as the
documentation shows: `NativeMenu.Menu` with an About item on the `Application`, and `NativeMenu.Menu` with File, Edit
and Tools menus and `Meta+` gestures on the `Window`, plus a `NativeMenuBar`. Then click between it and other apps,
and switch with ⌘-Tab:

- **The same flash and missing menus:** the problem is Avalonia's on macOS 27. Add the findings to #22285.
- **Correct behaviour there but not in WhatDisk:** something in WhatDisk still differs from the documented setup.

### What is tied to Avalonia

Line counts include documentation comments.

| Area | Lines | Fate on another framework |
|---|---|---|
| Scan engine, duplicate finder, settings, file operations, volumes, formatting, chart model | ~6,900 | Unchanged |
| macOS and Windows platform code (file system, volumes, shell, icons, clipboard) | ~5,000 | Mostly unchanged. Icons already come out as plain pixel data (`IconImage`); only `IconCache` turns them into Avalonia bitmaps. |
| View models (tree, chart, details, tabs, duplicates, settings) | ~5,100 | Mostly reused. They use CommunityToolkit.Mvvm, which is framework-neutral. About 10–15% needs touching to replace Avalonia types: `SelectionModel` (tree selection), `IAvaloniaReadOnlyList` (`TreeListModel`, `ObservableRangeList`), and `KeyGesture`/`KeyModifiers` (`ShortcutScheme`). |
| Views, custom controls (tree row panel and column layout, percentage bar, pie chart, legend swatch, platform icon), styles, app start-up | ~4,200 | Rewritten, once per UI codebase |
| Tests | ~10,900 | About 260 of the 341 test methods carry over. The 81 headless UI tests (`[AvaloniaFact]`/`[AvaloniaTheory]`, mostly the ~1,600 lines of view tests, plus view-model tests that need Avalonia's selection model) depend on Avalonia's headless platform. |
| Build and packaging | — | Reworked: the macOS app bundle, DMG and signing, and Windows packaging |

The most expensive part to rebuild on any framework is the custom UI. It's a virtualised multi-column tree with fill
bars, column resizing and sorting, drag and drop with spring-loaded folders and auto-scroll, per-platform shortcuts, a
shared context menu, the pie chart and legend, tabs, and in-window dialogs.

### Options for Windows and macOS

Effort is for one experienced .NET developer working full time, and includes reaching the current level of polish and
tests.

| Option | How it looks and works | macOS menu bar | Native AOT | Effort | Main risks |
|---|---|---|---|---|---|
| **A. Native per platform, shared C# core.** WinUI 3 (or WPF) on Windows; AppKit through .NET for macOS (`net10.0-macos`) on Mac. | Truly native on both | Real `NSMenu`, standard Apple behaviour | Yes on both with WinUI 3; WPF has none | 10–14 weeks | Every UI feature is built twice from then on. There are no headless UI tests, so those checks move to view-model tests and manual testing. |
| **B. Eto.Forms.** One C# UI over native controls: WPF on Windows, AppKit on macOS. | Native controls, plainer styling | Real `NSMenu` | Uncertain (it uses reflection) | 6–8 weeks | Small community, essentially one maintainer. Custom cells and drag and drop in its tree grid need proving early. |
| **C. .NET MAUI.** One UI: WinUI 3 on Windows, Mac Catalyst on macOS. | Native, but macOS uses the iPad-style Catalyst model | Native via `MenuBarItem` | Yes on macOS, partial on Windows | 6–9 weeks | No tree or data grid control, so build one or buy one (Syncfusion, Telerik). Catalyst can't call AppKit directly (Finder icons, Quick Look, Get Info). Desktop support is less mature. |
| **D. Swift on Mac, C# on Windows.** SwiftUI or AppKit on macOS calling the C# core compiled with Native AOT as a native library; WinUI 3 or WPF on Windows. | Best possible on Mac | Fully native | Yes | 12–16 weeks | Two languages, plus a C-style interface between Swift and .NET to design and maintain. |
| **E. Web UI in a native shell.** Electron with a .NET back end, or Tauri with a .NET sidecar process. | Web-styled | Native and reliable | Back end only | 8–12 weeks | Two technology stacks with messaging between them. Much larger downloads and memory use. The tree has to be a virtualised web grid; good tree grids are often commercial. |
| **F. Uno Platform** | WinUI-style APIs. macOS is drawn with Skia, as Avalonia's is. | Not a clear improvement | Partial | 5–8 weeks | On Mac it sits in the same non-native category as Avalonia, so it doesn't fix the problem. Not recommended for this reason. |

Notes on the options:

- **A:**
  - **On Windows,** WinUI 3 supports Native AOT with Windows App SDK 1.6 and later. WPF is more mature for custom
    drawing and trees, but can't be trimmed or compiled ahead of time.
  - **On macOS,** the app would use `NSOutlineView` with custom cell views, `NSMenu` and CoreGraphics. The
    hand-written Objective-C runtime calls in `WhatDisk.MacOS` could become typed AppKit calls.
- **B:** Eto's `TreeGridView` maps to `NSOutlineView` on macOS and to a WPF tree grid on Windows. Custom drawing uses
  `Drawable` and custom cells.
- **C:** `MenuBarItem` maps to the Catalyst main menu, which macOS shows as a native menu bar. Open With, Quick Look
  and Get Info rely on AppKit, which a Catalyst process can only reach through a separately loaded AppKit bundle.
- **D:** This gives the most Mac-like result. The core would be exported through `[UnmanagedCallersOnly]` entry points,
  and scan progress and results passed across as handles or serialised snapshots.

### Staying on Avalonia

| Option | Effort | Effect |
|---|---|---|
| Wait for Avalonia to fix #22285, #22286 and #22287 | None | The attached patches address exactly these problems, but nothing is merged yet. |
| Ship a patched Avalonia macOS native library built from those patches | 2–4 days, plus maintaining a fork of Avalonia's native code | Fixes the behaviour now; every Avalonia upgrade means rebasing the patches. |
| Turn native menus off on macOS (`MacOSPlatformOptions.DisableNativeMenus`) | Done: the `general.macos.useMenuBar` setting | The menus are drawn at the top of the window, as on Windows, with Quit WhatDisk (⌘Q), Settings and About in them. Avalonia sets nothing in the macOS menu bar, so none of its menu swapping happens. It doesn't look or behave like a Mac app: the system menu bar has no app menu items, and Hide (⌘H), Minimize (⌘M) and the Window menu aren't offered. |

To try the last option, turn off **Settings › General › Use the macOS menu bar** and restart, or add this to the
settings file:

```json
{ "general": { "macos": { "useMenuBar": false } } }
```

### Recommendation

- **If a Mac-native feel matters most:** option A with WinUI 3 and AppKit. It's the only path where the macOS menus
  and windows behave exactly like other Mac apps. The cost is roughly double the UI effort for every future feature.
- **If one UI codebase matters most:** option B, Eto.Forms, is the closest fit for Windows and macOS with native menus.
  Prototype the tree first, because that's where it's most likely to fall short.
- **Before either:** run the minimal repro. If it shows the same behaviour, the problem is entirely Avalonia's, and
  waiting for the upstream fix may cost less than 6–14 weeks of migration. Meanwhile the in-window menus are
  available as a setting.

### About the estimates

The weeks assume a human developer. With an AI assistant doing the porting, the coding goes much faster. Checking
native UI behaviour on real machines (menus, focus, drag and drop, keyboard) stays the bottleneck, and needs hands-on
time on both platforms.
