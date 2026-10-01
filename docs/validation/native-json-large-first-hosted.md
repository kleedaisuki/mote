# Ordinary Native AOT JSON pilot: first four-RID target evidence

Date: 2026-10-01. Target run:
[CI 36799464145](https://github.com/kleedaisuki/mote/actions/runs/36799464145),
source `d7b24730b17b51d49dc0f384fff5dc2e27c99f98`. This audits the original
`cf6ce09` pilot, before the subsequent Save observer/failure metadata correction.
It is **not a four-RID acceptance pass**, despite green Native AOT jobs.

## Actual outcomes, not masked step conclusions

All four native-architecture Python setup steps succeeded. All four portable
test batches passed **9/9**. Both Mac clients compiled successfully. Every actual
JSON pilot printed `status: incomplete` and its raw step ended **exit 1**;
`continue-on-error` made the API step/job conclusions green. The uploaded JSON,
raw step log and exact sample outcomes take precedence over that color.

| RID | 1 MiB control | 100 MiB workload | First failing observable phase |
| --- | --- | --- | --- |
| win-x64 | Complete v0 → one edit → Complete v1 → exact Save → normal exit → GUI reopen/normal exit/terminal traces: pass | Source bind, Complete v0/v1 and unchanged disk before Save: pass; Save oracle not reached | `save-exact-bytes`, TimeoutError after bounded 60 s |
| win-arm64 | Same complete sequence: pass, independently audited native ARM64 ctypes execution | Same pre-Save sequence: pass; Save oracle not reached | `save-exact-bytes`, TimeoutError after bounded 60 s |
| osx-x64 | Failed before source acknowledgement, zero attempted edits | Same | `launch-to-source-bound`, RuntimeError |
| osx-arm64 | Same | Same | `launch-to-source-bound`, RuntimeError |

The original and final immutable fixture hashes agree in **all eight** cases.
The two Windows 100 MiB working copies still have the original hash; both
processes were forcibly killed. Their trace files are **zero bytes**, not proof
that a Save event never occurred. No normal terminal session, exact saved result,
GUI reopen, child timing or Save-phase attribution can be recovered for those
samples. For Mac, the original wrapper discarded a client's non-`observed`
metadata, retaining only RuntimeError. Its numeric guard is therefore unknown:
do not infer failed product binding, denied trust or a tree defect from this
artifact. No Mac native edit, Save, clean close or trace acceptance was reached.

## Provenance

Each report has one real executable in its publish directory, the requested
native observer architecture, the source commit above, and identical binary
hashes before/after the probe. The actual executables were not uploaded; these
are the strict inventory/hash observations produced on each target, not a local
reinspection of downloaded executable bodies.

| RID | Executable bytes | Executable SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `2df493b5e3d7402b636c2ca540e4377f00b48a78426ab88b0dcbab7ad16460d9` |
| win-arm64 | 7,244,288 | `520ca50236c180db45316853a0b3e465546d377ed0ba499ad31ce1b0957bad08` |
| osx-x64 | 16,842,440 | `ef98590aa12322ef0ca1f8d000fe27308beb59c77a29174c1c9c60ce6ded9305` |
| osx-arm64 | 16,505,608 | `0aa07f3fea66377e28582462b11611cd561b47e565a434bff5d909b364403f96` |

Tool hashes were checked against **Git blobs from the target commit**, including
the Windows checkout's CRLF transformation, rather than against a changing
current worktree. All match:

| Tool | Mac/LF SHA-256 | Windows/CRLF SHA-256 |
| --- | --- | --- |
| `probe.py` | `9d83f00dda698ecd07a7449b001927c61c536afa64ab5bac1066196e331c01fc` | `d6d9f37d4adcd742672dbaaf51fa22542830e15c04115ac708127e316fb86a25` |
| Reused `acceptance.py` | `b2d45fe1eac0f094bf997be8ea3777921e7019d09f958c03f7a6be6e1ed7f541` | `483a1977e439380e5809cf97334c81da86d00ee9c6484db9f489d7ce427525d5` |

Both actual Swift source hashes match committed LF bytes:
`bd4f387ecf3c8009de9243cc14f77354330d601a52986a33b92573bba26fd72f`.
Native Python 3.14 setup succeeded; the Mac reports record **3.14.7**. Runner
images: win-x64 `20260922.246.2`, win-arm64 `20260920.164.1`, osx-x64
`20260824.0482.1`, osx-arm64 `20260907.0351.1`. Different hardware/image instances
make these observations unsuitable for an OS/architecture speed ranking.

Source SHA-256 pairs (the expected saved value changes only byte 9):

| Size | Original SHA-256 | Expected saved SHA-256 |
| --- | --- | --- |
| 1 MiB | `9004bc8156e480461b02205536ee607414186c776078a8f35b20716c7e668355` | `7fb64708951aa7dc1337a43c841aa7a5e0f3807f53875da4e1ea965719248b3a` |
| 100 MiB | `11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db` | `f11fa45a8ff38a7fcf4c8cb925ed3775294cb98169e2aa75077b303d22459d09` |

## Timing scope and independent raw-trace audit

Windows 1 MiB original and reopen raw traces were independently re-audited with
the tightened successful-action/version predicate: both native architectures
pass, including exactly one terminal session, no drops, open v0, pre-edit v0,
accepted commit/presentation/draw v1 and a successful unversioned engine Save.
The Save result and reopen remain independently byte exact. Reopen has no edit
or Save actions. No native workflow was rerun for this artifact audit.

| Endpoint (ms) | win-x64 1 MiB | win-arm64 1 MiB |
| --- | ---: | ---: |
| Parent launch → bounded source acknowledgement | 162.0263 | 154.2519 |
| Child startup → editable (after configuration) | 94.615 | 81.548 |
| Child open → editable v0 | 18.086 | 13.410 |
| Child open → source draw callback return v0 | 26.925 | 20.580 |
| Parent edit dispatch → bounded source acknowledgement | 13.3568 | 13.9150 |
| Child edit → source draw callback return v1 | 9.426 | 10.393 |
| Child engine Save | 17.014 | 34.454 |

Windows 100 MiB parent launch-to-source acknowledgement was **618.3253 ms x64**
and **457.8447 ms ARM64**; parent edit acknowledgement **12.6922 / 13.4274 ms**.
These are bounded synthetic native-message observations and include 50 ms polling
and observer overhead; their failed terminal traces cannot justify child startup,
edit/draw or Save durations. All samples are traced, just-written, not disk-cold,
with one observation per size. No physical input, compositor presentation,
foreground user responsiveness, tracing-off baseline or p95 is established.

**CPU time and RSS/working-set were not sampled by this pilot.** No CPU/RSS,
allocation or memory-pressure claim follows from these artifacts. The parser's
managed benchmark evidence remains separate.

## Discriminating the Windows observer, not blaming the product

The failed poll repeatedly opened and hashed the 100 MiB working target while
Save could still be atomically replacing it. A tiny local owned-leaf Win32
discriminator, without editor/GUI input, tested the relevant sharing contract:

| Otherwise identical `ReplaceFileW` call with target reader held open | Result |
| --- | --- |
| Ordinary Python `Path.open('rb')` | Failed, **Win32 error 32**, original target retained |
| Explicit `CreateFileW` reader sharing READ/WRITE/**DELETE** | Succeeded, exact changed target |

This **proves the harness can obstruct atomic replacement**. It does not prove
the precise failed phase of the two hosted samples: their unflushed traces and
unobserved modal give no such evidence. CPython's
[`FileIO` source](https://github.com/python/cpython/blob/main/Modules/_io/fileio.c)
uses `_wopen`; the independent
[Windows sharing contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
requires existing handles to share delete access for replacement/deletion.
Procedure: `.temp/native-json-share-discriminator/discriminate.py`; exact result:
`.cache/native-json-large/windows-share-discriminator.json`. Only two owned leaf
fixtures were created and removed; no editor or user file was touched.

The correction removes the interaction rather than building a special reader:
after one ordinary Save request, poll only the owned native **dirty→clean title
transition**, with no target file handle until clean acknowledgement, then require
the independent full saved hash and GUI reopen. Native clean title alone is not
acceptance. The original 60-second deadline remains; no Save retry, product
recovery weakening, custom DELETE-sharing reader or blind timeout increase.
On failure retain owned-dialog count/enabled/dirty metadata and make at most one
owned normal-close request, bounded five seconds. Unknown dialogs remain untouched;
if clean exit cannot happen, forced cleanup is still failure and may leave an
unflushed trace. Mac failure metadata gains a fixed guard enum and bounded
numeric/Boolean whitelist without arbitrary AX values.

One authorized local **100 MiB stale-binary harness discriminator** with that
UI-only polling passed exact Save, normal close/terminal trace and fresh GUI
reopen in the complete pilot. Binary:
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`,
7,068,160 bytes, predating current JSON pages. Artifact:
`.cache/native-json-large/local-stale-large-title-only-pilot.json`; scratch
`b3fa376a259b4d06b00d309e61935428`. Saved hash is the exact declared 100 MiB
`f11fa...` oracle; immutable original remained `11c596...`; both normal exits and
terminal audits passed. This supports the observer correction, **not current-source
JSON-page acceptance or performance**. There was one launch workflow, no repeated
stress or timing-threshold tuning. Updated portable tests **12/12** pass;
[independent targeted recheck](../reviews/native-json-large-acceptance-review.md)
found no remaining substantive issue. Updated Mac metadata compilation and
current-source four-RID outcomes require a later hosted run.

## Retained artifacts and next evidence

Artifacts are under `.cache/ci-36799464145-native-json-{x64,arm,macx64,macarm}/`:
each contains its exact report and any uploaded traces; the corresponding job
log is also retained there. Content-free recomputed identity/causal summary:
`.cache/native-json-large/ci-36799464145-audit.json`.

Next run must show whether the UI-only Save observer resolves both Windows
100 MiB failures, retain exact byte/reopen/terminal evidence, and expose the first
Mac failure guard without permission/global-input workarounds. Only then choose
the next correction. No green AOT job is substituted for those observations.
