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