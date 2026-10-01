# mote release packaging

The approved delivery contract permits an application package with multiple
files while retaining **Native AOT**. Users do not install .NET. The release
product is `src/Mote.Native`, not the experimental `Mote.Desktop` prototype.

## Release outputs

| RID | Download | Executable after extraction |
| --- | --- | --- |
| win-x64 | `mote-0.1.0-win-x64.zip` | `mote-0.1.0-win-x64/mote.exe` |
| win-arm64 | `mote-0.1.0-win-arm64.zip` | `mote-0.1.0-win-arm64/mote.exe` |
| osx-x64 | `mote-0.1.0-osx-x64.tar.gz` | `mote.app/Contents/MacOS/mote` |
| osx-arm64 | `mote-0.1.0-osx-arm64.tar.gz` | `mote.app/Contents/MacOS/mote` |

Each package contains the executable, all required publish companions (if any),
user documentation, release notes, GPL license, third-party notices and full
upstream license texts. `package-manifest.json` records source commit, version,
RID, actual SDK, workflow run and every payload file's length/SHA-256. No symbols,
CoreCLR runtime configuration, plugin download or workspace is introduced.
Mutable settings, caches and opt-in traces remain under `~/.mote` or user overrides.

The final `release-assets` set contains four archives, four external package
manifests, the matching Git source archive, `release-index.json` and `SHA256SUMS`.
Checksums are integrity evidence, **not publisher authentication**. These are
not Developer ID signed/notarized macOS applications or Authenticode signed
Windows applications. See the included installation guide for trust limitations;
never disable system-wide security to run mote. macOS minimum declared version
is 15.0; hosted qualification records its actual OS version, not all versions.

## Qualification and publication

`.github/workflows/release.yml` is reusable via `workflow_call` and manually
invocable via `workflow_dispatch`. It requires a full immutable lowercase Git
SHA and canonical stable version. The source project version must agree. The
workflow pins SDK 10.0.400/runtime 10.0.11 to match the distributed notice texts;
a toolchain upgrade must deliberately update the runtime notice inventory.

The gate order is source identity, complete solution tests on Windows/macOS,
packer/oracle negative controls, native-architecture AOT publish on four runners,
archive extraction and exact inventory, actual packaged CLI/runtime/GUI startup,
six-format edits/Save/fresh-process reopen, seven embedded codec checks, then
post-run byte verification. Evidence is retained on failure. Download candidates
are uploaded only after that RID passes; the complete release-assets set is
assembled only if **all four** pass. The workflow has read-only repository
permissions and never creates tags/releases or overwrites existing release assets.

Root publication protocol: inspect a successful run of the intended exact commit,
download its `release-assets`, independently verify `SHA256SUMS` and source/run
identity, confirm a new version/tag does not already exist, create the tag for
that commit and a draft GitHub release, upload that exact set plus the qualified
release notes, then publish. An existing release/version is an error, not an
invitation to use `--clobber`. Public release notes must distinguish actual
qualification from real keyboard/Pinyin/screen-reader/physical paint work not
covered by hosted probes.

## Local packer use

Run from a **clean checkout** of the immutable source after the appropriate
Native AOT publish. Outputs must be descendants of repository `.cache` or `.temp`.

```powershell
$sha = (git rev-parse HEAD).Trim()
dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o .cache/release-publish/win-x64
python -B packaging/release_package.py build --rid win-x64 --version 0.1.0 --source $sha --sdk 10.0.400 --publish .cache/release-publish/win-x64 --output .cache/local-release/win-x64
python -B packaging/release_package.py unpack --archive .cache/local-release/win-x64/mote-0.1.0-win-x64.zip --output .cache/local-release-extracted/win-x64
python -B packaging/release_package.py verify --rid win-x64 --version 0.1.0 --source $sha --package .cache/local-release-extracted/win-x64/mote-0.1.0-win-x64
```

Local packing does **not** qualify a release. GUI/encoding probes in the release
workflow are hosted-only by design. No local GUI is launched by the packer/tests.

```powershell
python -B -m unittest discover -s tests -p test_release_packaging.py -v
```

See [release packaging validation](../docs/validation/release-packaging.md).

## Historical diagnostic packers

`Pack-Windows.ps1`, `Pack-MacOS.ps1` and `Pack-Common.ps1` remain unchanged as
historical Avalonia Desktop diagnostic tooling. Their old NONCOMPLIANT labels
refer to the former strict-binary contract and their non-product prototype.
They are **not** the current Native release path; do not upload those Desktop
archives as the qualified native product or infer that Avalonia was migrated.

## References

- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): self-contained native builds and platform toolchains.
- [GitHub workflow dispatch](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch): manual workflows must first exist on the default branch.
- [GitHub reusable workflows](https://docs.github.com/en/actions/how-tos/sharing-automations/reuse-workflows): qualification can also run as ordinary CI without a manual dispatch round trip.
