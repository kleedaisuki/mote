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
5. Replace the versioned page's preparation status with actual published state
   only after downloads/checksums exist. Update the README release status and
   move the changelog's Unreleased entry to the dated version.
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

## Publication records

- [v0.1.0 preparation page](v0.1.0.md)
- [User manual](../user/manual.md)
- [Root changelog](../../CHANGELOG.md)
