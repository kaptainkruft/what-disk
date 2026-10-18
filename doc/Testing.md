## Testing

`dotnet test` runs 405 tests across three projects (on macOS, 34 Windows-only tests skip; on Windows, the macOS-only ones do).

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
  progress and cancellation. Other tests cover the CSV quoting and the Duplicates window, including a real
  right-click on a row to verify its set, and that Copy and Export give the same CSV.
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