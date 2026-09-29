# macOS distribution of a strict standalone Mach-O

Status: release-path research, 2026-09-29. Scope is a **single Native AOT Mach-O file** with an `Info.plist` embedded in its `__TEXT,__info_plist` section, no `.app`, no product `.dylib`, no sidecar resource and no self-extraction. This is a technical and distribution contract, not a synonym for an application bundle. The related [feasibility probe](single-binary-feasibility.md) established native AppKit GUI startup and `open -a` acceptance on macOS 15 ARM and Intel; it did **not** exercise trusted downloaded distribution.

## Bottom line

Apple supports code-signing **nonbundled code** with a Developer ID Application identity, hardened runtime and secure timestamp. Its notary service accepts a ZIP containing signed standalone code and issues a ticket for nested binaries. The ticket is published online for Gatekeeper to fetch. **Apple's detailed notarization instructions explicitly state that tickets for standalone binaries cannot currently be stapled to those binaries.** Therefore the strict one-file product can have trusted *online* first launch after actual Developer ID signing/notarization, subject to Gatekeeper validation, but it cannot promise a fresh, quarantined, *offline* first launch with an attached ticket. Apple Developer Technical Support explains that a new offline Mac with no cached ticket blocks such a launch. [Apple distribution signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac/), [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow), [Apple DTS on stapling and offline launch](https://developer.apple.com/forums/thread/720093).

This is a **known product limitation**, not evidence that a bare Mach-O cannot run a GUI. A stapled `.app`, `.dmg` or `.pkg` could change the trust experience, but treating any of those as the delivered application would violate the user's strict single-binary shape. A ZIP used temporarily to *submit* the binary to Apple's notary service does not alter the final installed runtime shape; whether a ZIP or raw Mach-O is acceptable as customer transport is a separate product decision. A ZIP itself cannot be stapled either. [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

## Keep six different claims separate

| Stage | What it establishes | What it does **not** establish |
| --- | --- | --- |
| Native AOT publish and dependency inspection | One Mach-O exists and its load commands require only OS libraries/frameworks. | Signing, notarization, Gatekeeper trust, Finder file association. |
| Embedded `Info.plist` | Apple APIs can read metadata from `__TEXT,__info_plist` of an unbundled Mach-O; our probe's `open -a` and `codesign` recognized the binary/identifier on macOS 15 ARM and Intel. | Automatic document-type registration, branded Finder icon, quarantine behavior. [Apple single-file signing guide](https://developer.apple.com/library/archive/documentation/Security/Conceptual/CodeSigningGuide/Procedures/Procedures.html), [Core Foundation API](https://developer.apple.com/documentation/corefoundation/cfbundlecopyinfodictionaryforurl%28_%3A%29), [probe run](https://github.com/kleedaisuki/mote/actions/runs/36539669129). |
| Developer ID code signature | Publisher identity and tamper detection; hardened runtime and secure timestamp satisfy notary prerequisites. | Notary acceptance or Gatekeeper approval. An ad-hoc CI signature has **no** Developer ID identity. [Apple signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac/), [Apple notarization prerequisites](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution). |
| Notary service `Accepted` | Apple scanned the submitted signed code and created ticket(s), with a code-directory hash (`cdhash`) per architecture; ticket is online. | Offline first launch, stapling to a standalone binary, or automatic passage of every Gatekeeper rule. [Apple notary workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow), [Apple DTS ticket model](https://developer.apple.com/forums/thread/720093), [Apple DTS: notarization ≠ Gatekeeper](https://developer.apple.com/forums/thread/814949). |
| Gatekeeper assessment on a fresh quarantined download | Closest test of the user's actual first-launch path. | Finder document association, IME/accessibility correctness, or all future OS versions. `codesign`, `spctl`, and `syspolicy_check` are useful diagnostics but not a substitute for this run. [Apple DTS testing procedure](https://developer.apple.com/forums/thread/130560). |
| Launch Services/Finder document claim | Embedded `CFBundleDocumentTypes` plus successful registration and an actual open-document event could make mote a candidate editor. | Being the system default handler, receiving and opening files correctly, or a custom Finder icon. The positive CI `open -a` only proved application launch. [Apple `LSRegisterURL`](https://developer.apple.com/documentation/coreservices/1446350-lsregisterurl), [Apple document types](https://developer.apple.com/documentation/bundleresources/information-property-list/cfbundledocumenttypes). |

The high-level Apple notarization overview uses broad language about attaching a ticket “to your executable”; for **standalone binaries**, the more specific [“Staple the ticket to your distribution” section](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow) expressly says this is not currently possible. Use the specific statement for release design. The online ticket and a signed binary are still real, but a signed CI smoke must never be called a trusted public release.

## Practical release procedure (requires publisher credentials)

All example build/test artifacts stay under repository-root `.cache` or `.temp`. Run the signing/notary commands on macOS with the publisher-controlled **Developer ID Application** certificate and a configured `notarytool` keychain profile. Do not put passwords or private keys in the repository or PR workflows. Replace the placeholder bundle ID with a stable identifier controlled by the publisher. The embedded `CFBundleIdentifier` and `codesign -i` identifier should match. Apple specifically directs nonbundled code to use `-i`, main executables to use `-o runtime`, Developer ID signatures to use `--timestamp`, and warns not to run `codesign` with `sudo`. [Apple distribution-signing instructions](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac/).

### 1. Produce and inspect the final code *before* signing

```bash
set -euo pipefail
bin="$(pwd)/.cache/release/osx-arm64/mote"

# Native AOT publish command is project-specific; its output must be this lone file.
file "$bin"
otool -L "$bin"               # Only Apple /usr/lib or /System/Library dependencies.
otool -s __TEXT __info_plist "$bin"  # Metadata must be embedded, not a sidecar.
find "$(dirname "$bin")" -maxdepth 1 -type f -print
```

The embedded plist should contain at least a stable `CFBundleIdentifier` and `CFBundleName` for single-file code signing; for Launch Services identity include `CFBundleExecutable`, version/name fields and carefully scoped `CFBundleDocumentTypes` claims. Apple documents an Info.plist section for single-file tools and recognizes it in `CFBundleCopyInfoDictionaryForURL`. [Apple single-file signing guide](https://developer.apple.com/library/archive/documentation/Security/Conceptual/CodeSigningGuide/Procedures/Procedures.html), [Core Foundation API](https://developer.apple.com/documentation/corefoundation/cfbundlecopyinfodictionaryforurl%28_%3A%29). If making a universal Mach-O with `lipo`, combine the two AOT slices **before** signing and confirm both slices embed consistent metadata; .NET documents universal Native AOT merging with `lipo`. [Microsoft macOS Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

### 2. Sign the binary once, with real Developer ID credentials

```bash
security find-identity -p codesigning -v
codesign --force --sign 'Developer ID Application: PUBLISHER (TEAMID)' \
  --timestamp --options runtime \
  --identifier 'org.mote.editor' "$bin"
codesign --verify --strict --verbose=4 "$bin"
codesign --display --verbose=4 "$bin" 2>&1 | \
  grep -E 'Identifier=|Authority=|TeamIdentifier=|Timestamp=|Runtime Version='
```

Use no JIT or library-validation exception entitlement unless a real tested need appears. Native AOT needs no JIT entitlement, and this one-binary design loads only OS frameworks. Apple recommends narrowly scoped hardened-runtime exceptions; exceptions such as disabling library validation enlarge the attack surface and can complicate trust. If entitlements become necessary, sign with a reviewed `--entitlements` plist and test again. [Apple Hardened Runtime](https://developer.apple.com/documentation/security/hardened-runtime), [.NET Native AOT macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos). `codesign` must be the **last mutating step on the Mach-O** before notarization: lipo, `install_name_tool`, binary patching or metadata-section changes afterward invalidate the signed bytes. [Apple resolving notarization issues](https://developer.apple.com/documentation/security/resolving-common-notarization-issues).

### 3. Submit an archive containing only the signed binary

```bash
zip="$(pwd)/.cache/release/mote-osx-arm64-notary.zip"
ditto -c -k --keepParent "$bin" "$zip"
xcrun notarytool submit "$zip" --keychain-profile 'mote-notary' --wait
# Save the submission ID printed by the preceding command.
xcrun notarytool log '<submission-id>' --keychain-profile 'mote-notary' \
  "$(pwd)/.cache/release/notary-log.json"
```

Gate on `Accepted`, inspect the log for **warnings as well as errors**, and verify its `ticketContents` includes the mote binary's architecture and `cdhash`. Apple's notary service accepts ZIP archives and creates tickets for nested code items. The notary profile can be set up using `notarytool store-credentials`; never embed its secret in a script. [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow). Keep the exact signed executable bytes that were in the accepted ZIP; do not re-sign or mutate afterward. **Do not run `stapler` on the executable or claim “stapled”:** Apple says this is unsupported for a standalone binary. The submission ZIP is not the installed product; ZIP files also cannot be stapled. [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

### 4. Check trust on fresh target Macs, not merely the CI runner

```bash
# On a fresh VM/Mac after downloading through Safari to a repository .temp folder:
bin="$(pwd)/.temp/gatekeeper/mote"
xattr -p com.apple.quarantine "$bin"  # Confirm the real download is quarantined.
codesign --verify --strict --verbose=4 "$bin"
codesign -vvvv -R='notarized' --check-notarization "$bin"
spctl --assess --type exec --verbose=4 "$bin"  # Supplemental, not decisive.
open -a "$bin"                         # Actual quarantined launch, with network online.
```

Use a **fresh** machine or restored VM snapshot for each first-launch test because Gatekeeper caches ticket decisions. Download in the manner customers will use, preferably Safari (which sets quarantine); a `curl`/`scp` copy or unquarantined CI artifact can produce a false pass. Do **not** strip quarantine as a workaround. Exercise macOS ARM and Intel (or both universal slices), and the minimum supported OS plus a current release. Check the visible Gatekeeper dialog, accepted launch, and an actual editable document. Apple DTS recommends this real workflow over static CLI checks and notes that even a valid notarization ticket can fail other Gatekeeper checks. For unbundled/“other code,” Apple DTS recommends `codesign -vvvv -R="notarized" --check-notarization` as an auxiliary test; `spctl` is less faithful than an actual quarantined launch. [Apple DTS testing procedure](https://developer.apple.com/forums/thread/130560), [Apple DTS trusted-execution troubleshooting](https://developer.apple.com/forums/thread/707357).

### 5. Separately test Finder/document integration

`open -a /absolute/path/to/mote` succeeded in the unsigned/ad-hoc CI probe, but that tests **application launch**, not a Finder document association. Apple's `LSRegisterURL` accepts a file URL to an *app file or bundle* and registers its document claims; `CFBundleDocumentTypes` describes the claimed content types/role. This provides an authoritative mechanism to try with an embedded plist, but its effectiveness for mote's six actual formats is not yet measured. [Apple `LSRegisterURL`](https://developer.apple.com/documentation/coreservices/1446350-lsregisterurl), [Apple document-type keys](https://developer.apple.com/documentation/bundleresources/information-property-list/cfbundledocumenttypes).

On a clean user account, explicitly register the binary or perform the supported Launch Services discovery path, then verify **Open With** lists mote as an editor for `.md`, `.toml`, `.json`, `.yaml`/`.yml`, `.csv`, and `.txt` without seizing default handlers. Test `open -a "$bin" "$doc"` (without `--args`) and a Finder double-click after the user chooses mote as handler. Instrument the app's file-open Apple event and assert it opens the **actual document bytes**, including a second document sent to an already running process. Passing `--args path` only tests a CLI argument and is not equivalent. Record app/Dock identity and Finder icon behavior; Apple's conventional custom-icon keys refer to resources in an app bundle, so a branded Finder icon from only embedded bytes is **unverified**, not a promised feature. [Apple icon resource guidance](https://developer.apple.com/library/archive/documentation/General/Reference/InfoPlistKeyReference/Articles/CoreFoundationKeys.html), [AppKit runtime Dock icon](https://developer.apple.com/documentation/appkit/nsapplication/applicationiconimage).

## Hard limit: no stapled ticket on a lone executable

Apple's specific wording is decisive: the notary service **creates** tickets for standalone binaries but `stapler` cannot **attach** them to those binaries. Gatekeeper can fetch a ticket online and cache it. On a fresh Mac that has never seen this binary, with a quarantined download and no network, it cannot find a local or stapled ticket; Apple DTS states the trusted execution system blocks first launch. Online first launch may incur a slight lookup delay. [Apple custom notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow), [Apple DTS ticket-cache/offline explanation](https://developer.apple.com/forums/thread/720093).

| User scenario | Supported expectation under strict one-binary form |
| --- | --- |
| Fresh Mac, quarantined, online, Developer ID-signed and notarized | **Plausible release path**; must pass real first-launch Gatekeeper test. Ticket may be fetched online. |
| Same Mac after successful online first launch, later offline | May work from local ticket cache; do not market as guaranteed on every machine or after cache reset. |
| Fresh Mac, quarantined, first launch offline | **Incompatible with guaranteed trusted launch** without a stapled carrier or prior cache. Product documentation must say an internet connection can be required for first launch. |
| Unquarantined unsigned/ad-hoc CI binary | Useful for GUI feasibility only; **not** a public release or valid Gatekeeper validation. |

If the product promise is expanded to “fully trusted first launch offline on a pristine Mac”, the current Apple mechanism conflicts with the **lone executable** requirement. Do not silently answer this by shipping an `.app`, `.dmg`, `.pkg`, an extraction stub, or a companion ticket file; any change of delivery contract needs an explicit product decision.

## What remains unverified without publisher credentials

No publisher Developer ID certificate or notary profile was supplied to the isolated CI probe. Consequently no authentic Developer ID signature, timestamp, notary submission, online ticket lookup, Gatekeeper acceptance, quarantine launch, or offline behavior was tested. The ad-hoc signed CI probe only demonstrated that metadata stayed readable and `open -a` accepted the binary. The next release-blocking investigation is **one publisher-controlled signing/notarization dry run followed by fresh-VM quarantine tests**. Preserve the binary hash, notary submission ID/log, OS version/architecture, delivery method, quarantine attribute, Gatekeeper result and actual document-open result in `.cache/release-evidence/`; never store credentials there. This is the smallest experiment that can change the answer from “technically credible” to “release-proven.”
