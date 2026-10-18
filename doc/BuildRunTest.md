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

# Publish a Native AOT executable (x64), on Windows.
dotnet publish src/WhatDisk.Windows -c Release -r win-x64
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