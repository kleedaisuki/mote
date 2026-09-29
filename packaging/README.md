# Experimental Native AOT packaging (not the release design)

> **This is not a compliant mote release path.** The product requirement is
> literally one binary file, without a `.app` bundle or sidecar DLLs/dylibs.
> These scripts preserve a temporary way to run GUI smoke tests while the
> single-binary UI/backend design is investigated. Do not publish their ZIPs
> or bundles as release artifacts, and do not treat successful packaging,
> code signing, or notarization as satisfaction of the one-binary requirement.

The intended product has statically linked format policies and one physical
binary file. The present Avalonia build uses native graphics/text libraries
and does **not** meet that requirement. This temporary packaging takes an
**already published**, RID-specific Native AOT directory;
it never builds, downloads plug-ins, or invents a workspace. Run it on the matching
OS so the executable and archive retain the right platform metadata.

## Unsigned development packages

From the repository root, after `dotnet publish`:

```powershell
dotnet publish src/Mote.Desktop/Mote.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true
pwsh -File packaging/Pack-Windows.ps1 `
  -PublishDirectory src/Mote.Desktop/bin/Release/net10.0/win-x64/publish `
  -OutputDirectory .cache/packages/win-x64 -RuntimeIdentifier win-x64 -Version 0.1.0
```

On macOS, use `osx-arm64` or `osx-x64` as appropriate:

```powershell
dotnet publish src/Mote.Desktop/Mote.Desktop.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true
pwsh -File packaging/Pack-MacOS.ps1 `
  -PublishDirectory src/Mote.Desktop/bin/Release/net10.0/osx-arm64/publish `
  -OutputDirectory .cache/packages/osx-arm64 -RuntimeIdentifier osx-arm64 `
  -Version 0.1.0 -BundleIdentifier org.mote.editor
```

| Platform | Output in the chosen directory | Launchable path |
| --- | --- | --- |
| Windows | `mote-<version>-<rid>-unsigned/`, matching `.zip` | `.../Mote.Desktop.exe` |
| macOS | `mote.app`, `mote-<version>-<rid>-unsigned.zip`, matching `-manifest.json` | `mote.app/Contents/MacOS/Mote.Desktop` |

The Windows portable directory contains the embedded Windows app manifest and all
runtime companions. Its `package-manifest.json` lists relative paths, sizes and
SHA-256 hashes, excluding debugger symbols and the manifest itself. The macOS
manifest sits beside the bundle. The `.app` contains `Info.plist`, the original
Native AOT executable in `Contents/MacOS`, and native `.dylib` files in
`Contents/Frameworks`, with app-host-relative symlinks in `Contents/MacOS` for
loader compatibility. `ditto` preserves symlinks and executable permissions in
the zip. Both packers reject an existing target instead of silently overwriting
a previous artifact. Use a fresh output directory for each build/RID.

The macOS bundle advertises editing for plain text, Markdown, JSON, YAML,
TOML and CSV through Launch Services. It uses `LSHandlerRank=Alternate` and does
**not** seize the system's default handlers. Avalonia's file-activation event
routes Finder-opened documents to the editor; a double-click test on each format
is still part of release verification. The default `org.mote.editor` identifier
is a development placeholder: a public release must choose and keep an identifier
controlled by the publisher. macOS 14 is the declared minimum because it is the
oldest macOS in the current .NET 10 support matrix. Reassess this when the SDK or
platform policy changes. A branded icon is not yet included; unsigned CI bundles
will show the system's generic app icon.

## Observed single-binary gap (Windows x64, 2026-09-29)

On this checkout, `dotnet publish ... -r win-x64 --self-contained true
-p:PublishAot=true -o .cache/packaging-publish/win-x64` succeeded and the
resulting native executable returned `mote runtime ok` for `--check-runtime`.
The publish directory still had four loadable binaries (debug symbols are not
counted):

| File | Bytes | Role / confidence |
| --- | ---: | --- |
| `Mote.Desktop.exe` | 26,926,080 | Native AOT app executable |
| `av_libglesv2.dll` | 5,394,096 | Avalonia ANGLE asset; whether this specific backend is needed in every GUI path is unverified |
| `libHarfBuzzSharp.dll` | 1,816,088 | Native shaping asset emitted by NuGet |
| `libSkiaSharp.dll` | 11,628,896 | Native drawing asset emitted by NuGet |

An otherwise identical publish with `-p:PublishSingleFile=true
-p:IncludeNativeLibrariesForSelfExtract=true` into
`.cache/packaging-probe-single/win-x64` **still emitted all three DLLs**;
the executable was 26,955,776 bytes. Thus these switches do not solve the
literal one-binary target for this Native AOT/Avalonia configuration. The
headless check proves only that the executable itself starts; it does not
establish which sidecars a real GUI session loads. Merely placing DLLs into
a ZIP or extracting them to a temporary directory at startup would not meet
the stronger operational one-binary requirement.

The installed NuGet assets for the three Windows native dependencies contain
`.dll` files, not static `.lib` archives. [.NET Native AOT native interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
documents a possible *mechanism* for static linking: build static archives,
declare `NativeLibrary`, and bind matching calls with `DirectPInvoke`.
That is not a drop-in packaging switch. It would require source builds and
interop changes for the graphics/shaping stack, then Windows/macOS GUI,
performance, licensing, and update-path validation. This evidence does **not**
prove static linking is impossible; it shows it is currently unimplemented
and materially larger than a packaging task. A different UI/backend design
may be the lower-risk path to the strict goal.

## Diagnostic validation (not a release gate)

The unsigned outputs are **CI/developer diagnostics, not trusted end-user
releases and not one-binary compliant**. Run `--check-runtime` on the executable and a real packaged GUI
open/edit/save smoke on the target OS. On macOS also run
`plutil -lint mote.app/Contents/Info.plist`, inspect the executable and native
libraries with `file`/`otool`, verify every `Contents/MacOS/*.dylib` symlink,
and launch with `open -a mote.app --args <file>` plus Finder double-click. On
Windows unpack the zip on a clean machine/VM, confirm all companion DLLs remain
beside the executable, then run the same GUI workflow. `--check-runtime` alone
does not initialize Avalonia and cannot certify the package.

**The next steps describe platform trust for a hypothetical bundle-based
product, not mote's approved release architecture.** If a bundle-based
product were separately authorized, distributing it outside the Mac App Store
would require signing every Mach-O library and
the executable with a **Developer ID Application** identity, hardened runtime
and secure timestamp; sign the enclosing bundle **last**. Do not use `codesign
--deep` as a substitute for signing nested code deliberately. Verify with
`codesign --verify --strict --verbose=2`, submit a `ditto` zip using
`xcrun notarytool submit ... --wait`, then staple and assess the app with
`xcrun stapler staple` and `spctl --assess`. Native AOT itself does not require
JIT entitlements; request any additional entitlement only after a real runtime
need is demonstrated. Recreate the zip and SHA-256 manifest **after** signing
and stapling, since those operations change bytes. Gatekeeper acceptance and
a real GUI smoke would still be necessary; neither notarization nor those
tests would make a bundle one binary.

Likewise, a hypothetical Windows portable release would sign the executable and each native DLL
with a trusted Authenticode identity and RFC 3161/SHA-256 timestamp, verify the
signatures, then generate a fresh inventory and zip. Unsigned packages may be
blocked by Smart App Control or prompt in SmartScreen. Signing is deliberately
not run by ordinary PR CI because repository secrets and publisher identity are
not available to untrusted changes. No `signed` or `notarized` label is emitted
by these packers, and signing multiple files would not satisfy mote's goal.

## Why this layout

- [Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos)
  documents the `.app` structure, `CFBundleExecutable`, native backend and
  post-publish packaging.
- [Avalonia activatable lifetime](https://docs.avaloniaui.net/docs/services/activatable-lifetime)
  documents macOS file activation and the `CFBundleDocumentTypes` requirement.
- [Apple document types](https://developer.apple.com/documentation/bundleresources/information-property-list/cfbundledocumenttypes)
  and [imported UTIs](https://developer.apple.com/documentation/uniformtypeidentifiers/defining-file-and-data-types-for-your-app)
  define Finder associations.
- [.NET Native AOT for macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
  distinguishes Native AOT's entitlement needs from JIT applications.
- [Apple distribution signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac)
  and [notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
  define the public release trust chain.
- [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
  and [Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
  explain Windows portable signing and trust constraints.
