# Installing mote

See the [GitHub releases page](https://github.com/kleedaisuki/mote/releases)
for published downloads. The [v0.1.0 page](../releases/v0.1.0.md) is a preparation
record until its artifacts and checksums are published. Do not mistake CI
artifacts, experimental profiles, or the Avalonia prototype for a supported
release download.

## Choose a build

| Computer | Runtime identifier |
| --- | --- |
| Intel/AMD Windows | `win-x64` |
| Arm Windows | `win-arm64` |
| Intel Mac | `osx-x64` |
| Apple silicon Mac | `osx-arm64` |

Use the package matching your machine. Do not rely on emulation as qualification
of an untested architecture. The first release targets currently supported
Windows 11 releases and macOS 15 or newer. This is the declared product support
policy, not a claim that every OS version has been exercised: the versioned
release page records the actual hosted qualification environments.

Native AOT packages include the required application runtime. You need neither
a .NET installation nor an account. The application still uses OS libraries.
Keep all files in a release package together; an application bundle or companion
library is part of the installed application, not user configuration.

## Verify a download

Compare the archive SHA-256 with the published checksum file **before** launch.
This verifies integrity against that release record, not publisher identity.
Unsigned packages can trigger Windows SmartScreen or macOS Gatekeeper warnings.
Only proceed if you trust the repository and the downloaded version.

```powershell
Get-FileHash .\downloaded-package.zip -Algorithm SHA256
```

```sh
shasum -a 256 mote-0.1.0-osx-arm64.tar.gz
```

## Windows

Extract the entire ZIP to a directory you control. The archive contains a
`mote-0.1.0-win-x64/` or `mote-0.1.0-win-arm64/` directory. Open that directory
and run `mote.exe`, or pass one file from PowerShell:

```powershell
.\mote.exe "C:\Users\Ada\Documents\settings.toml"
```

Keep the package directory intact. Updating means extracting the new version to
a separate directory, closing the old application, and using the new directory.
Do not mix companion libraries from different releases. Windows security prompts
are not evidence that an unsigned executable has been verified by its publisher.

## macOS

Extract the matching `.tar.gz` archive. It contains the complete `mote.app`
bundle, including its executable and documentation. Move `mote.app` to
Applications or another directory you control and launch it there. A
command-line invocation has this form:

```sh
/Applications/mote.app/Contents/MacOS/mote ~/Documents/notes.md
```

**No Apple Developer ID signature or notarization is supplied.** A download may
be blocked even though a hosted test launched the same binary. On supported
macOS versions, after attempting to open a trusted download, review the
application-specific prompt in System Settings > Privacy & Security and use
Open Anyway only if offered and you explicitly accept the risk. Consult
[Apple's guidance](https://support.apple.com/en-us/102445) if the prompt differs.
Do not disable Gatekeeper globally or remove quarantine recursively from an
unverified directory. An online first launch cannot compensate for missing
publisher signing/notarization.

## Settings, updates, and removal

Default settings are at `~/.mote/config.toml`; no configuration is required to
start. Follow [Configuration](configuration.md) for path overrides.

There is no documented automatic updater. Preserve your settings, close mote,
and install a complete new package. To remove mote, delete its application
package. Your documents and `~/.mote` remain; inspect the latter before deleting
it. Recovery content may reside beside a failed Save target, not inside the
application package; see [Save and recovery](manual.md#save-and-recovery).
