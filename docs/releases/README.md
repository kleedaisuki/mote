# Maintaining release documentation

This directory is the versioned product-release record, not a mirror of every
engineering probe. The root README is the product landing page; `docs/user/`
contains the practical manual. Each published release has its own page here.

## Before publishing a tag

1. Fix version, source revision, release-default profile and asset names. Require
   published artifacts to match that revision; do not silently rebuild a tag.
2. Run qualification against **extracted release packages**, not only build-tree
   executables: launch, ordinary editing/history, Save and a fresh-process reopen,
   required formats/settings and appropriate native workflows.
3. Record actual runner OS/architecture and workflow URLs. Separate supported
   OS requirements from tested environments and emulation. Keep unknown/failed
   non-gating evidence visible.
4. Inventory each complete package and calculate archive SHA-256. Keep matching
   notices, GPL license, source tag and build provenance. Check that relative
   manual links work in packaged documentation.
5. Keep immutable packaged documentation valid both before and after publication:
   describe the version and routes, and defer availability to the GitHub release.
   Do not ship a permanent candidate/pending banner. Confirm public availability
   on the repository landing page only after downloads/checksums exist; a later
   documentation-only status update does not rebuild or replace tagged assets.
   Keep a fresh Unreleased section alongside the dated version entry.
6. Use the same user-facing summary, assets, limitations and installation links
   in the GitHub release body. Never claim signing/notarization or universal
   IME/accessibility/performance without its own evidence.

## After publication

- Do not overwrite an archive under the same tag/filename with different bytes.
  Issue a new patch version when binaries change.
- Correct documentation transparently; distinguish a documentation correction
  from changed product behavior and keep affected version scope.
- Maintain a fresh Unreleased section and record user-visible fixes/features,
  breaking changes, migration requirements and known issues.
- Check links to downloads, checksums, manual and corresponding source. Keep
  architecture and research records discoverable separately from the concise
  product entry point.
- Do not package private traces, recovery content, development settings or
  repository `.cache`/`.temp` artifacts.

## Runtime security servicing

Before each publication, check Microsoft's current .NET servicing release and
security advisories; a reproducible SDK pin is not permission to ship an obsolete
security patch. The [.NET 10.0.12 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.12/10.0.12.md)
(2026-09-08) include SDK 10.0.401 and security fixes with links to the official CVE
advisories. Microsoft's [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
requires supported installations to remain current with servicing updates.

Native AOT includes runtime code in the application executable. Installing a
new .NET runtime on a user's computer does not service that compiled application.
Update the exact SDK/runtime pin, workflows, notices and corresponding license
inventory together, then rebuild and requalify extracted packages for the same
four native RIDs. Verify the actual selected SDK and resulting package provenance,
not only the SDK requested from setup tooling. Review CVE relevance without
claiming every framework advisory necessarily affects mote's executed paths.

If a version is already published, distribute the serviced build under a new
patch version/tag with new manifests and checksums. Never replace immutable
archives or retarget the previous tag. If publication has not occurred, hold it
until the serviced candidate passes; a previous toolchain's successful run cannot
qualify rebuilt bytes. Users install the complete new mote package; they still
do not need a separate .NET installation.

## Publication records

- [v0.1.0 release reference](v0.1.0.md): published 2026-10-02 Asia/Singapore;
  [public release](https://github.com/kleedaisuki/mote/releases/tag/v0.1.0),
  source `ed96fe4f29278e633e10421c89e2ce1f5b0536ae`,
  [qualified workflow 36938529836](https://github.com/kleedaisuki/mote/actions/runs/36938529836).
  All 11 public assets independently downloaded without authentication and matched
  the qualified bytes. Repository post-publication documentation is additive;
  tagged packages/source remain unchanged.
- [User manual](../user/manual.md)
- [Root changelog](../../CHANGELOG.md)
