## Testing

`dotnet test` runs 455 tests across three projects (on macOS, 35 Windows-only tests skip; on Windows, the macOS-only ones do).

- **Scan engine:** an in-memory file system drives aggregation, allocated sizes, partial failures, symbolic links,
  hard links, cancellation, mount points, firmlinks, virtual and network volumes, and a parallel stress test.
- **View models:** tree reconciliation (including randomised tests), the chart model and the main window. Fake time
  checks the 200 ms refresh while a scan is still running.
- **Item actions:** deleting, moving, copying across volumes, name conflicts and rescanning run against real files in
  a temporary folder, with a recording shell service and scripted dialogs. The 10 MB and 10-file confirmation
  threshold and the drop rules are checked case by case.
- **Duplicate finder:** real files in a temporary folder check each rule. Files up to 1 MiB that differ anywhere are
  ruled out by their hash. Larger files that differ inside a sample are ruled out, while those that differ only
  between samples stay in a sampled set until it is verified. The 1 MiB boundary and sample placement are checked, as
  are verification (confirming, trimming and splitting sets, with stable set numbers and rows kept in place), tiny,
  empty, cloud-only, unreadable, changed and missing files, hard links (with real links on macOS and Windows),
  progress and cancellation. Scoped searches are checked with their shortcuts: two selected folders find copies
  between them, one folder alone does not, and the whole scan is searched from anywhere in the window. The size-only Potential duplicate space estimate is checked on built trees (groups, the
  space beyond one copy, what is left out, scope and cache invalidation) and on a real scan (selection, the minimum
  length setting and deletes). Other tests cover the CSV quoting and the results tab, including a real
  right-click on a row to verify its set, and that Copy and Export give the same CSV.
- **Settings:** the settings file is checked for writing only differences from the defaults, putting platform
  settings in their subsection, and keeping other platforms' settings and unknown keys. Other tests cover comments,
  invalid values and invalid files, platform-dependent defaults, Reset, Restore Defaults and reloading after hand
  edits. The catalogue is checked for unique camel-case paths and valid defaults on both platforms. The Settings tab's
  pages, asterisks, editors and Reset are tested, along with each consumer: scan options, the delete threshold, size
  units applied at once, the duplicate options with automatic verification, the macOS volume list and the Windows
  Recycle Bin flags.
- **Tabs and in-window dialogs:** a real main window shows that results open in a new, selected, closeable tab while
  Scan cannot be closed, that tabs close in order (with the × and with Ctrl+W), that Settings opens in one tab, that
  closing a results tab stops its hashing, and that questions and the About box appear inside the window over
  disabled content, with Escape and the default button answering and the commands waiting meanwhile.
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