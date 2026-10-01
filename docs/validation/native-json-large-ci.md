# Independent ordinary Native AOT JSON pilot CI audit

## Identity and verdict

2026-10-01: independently audited [run 36799464145](https://github.com/kleedaisuki/mote/actions/runs/36799464145), source
`d7b24730b17b51d49dc0f384fff5dc2e27c99f98`. This is the first retained
four-native-RID execution of `benchmarks/NativeJsonLargeAcceptance/probe.py`.
The expectations come from its documented ordinary-product, exact-byte,
version/causal/normal-exit contract, not the green job color.

**The pilot is incomplete on all four RIDs.** Windows 1 MiB cases genuinely
pass the whole scoped contract; Windows 100 MiB cases fail at the Save byte
oracle. Both Mac sizes fail before source-binding acknowledgement. No 100 MiB
end-to-end acceptance, physical-paint observation, real IME result or tail SLA
was established. Do not silently promote successful earlier phases into a
successful workload.

| RID | 1 MiB | 100 MiB | Pilot report | Actual nested exit |
| --- | --- | --- | --- | --- |
| win-x64 | pass / complete, normal close and fresh GUI reopen | failed / save-exact-bytes / TimeoutError | incomplete | 1 |
| win-arm64 | pass / complete, normal close and fresh GUI reopen | failed / save-exact-bytes / TimeoutError | incomplete | 1 |
| osx-x64 | failed / launch-to-source-bound / RuntimeError | same | incomplete | 1 |
| osx-arm64 | failed / launch-to-source-bound / RuntimeError | same | incomplete | 1 |

All four portable protocol/artifact test executions passed **9/9**. Those tests
do not execute native OS input and cannot override these pilot failures. Each
non-gating nested step prints its `incomplete` summary, throws `Native JSON
source pilot incomplete (exit 1)` and ends with `Process completed with exit
code 1`. All four Single-binary AOT jobs nevertheless conclude success because
the nested step uses `continue-on-error`. The overall run is separately failed
by `Test / macos-latest`: `Native_recovery_export_then_explicit_save_keeps_versions_and_identity_distinct`
timed out awaiting a fake-shell controller state, `Plain text · Complete · v2`,
`shell-errors=2`, at `NativeSaveRecoveryTests.cs:88`. That managed-test failure
is **not** the Mac JSON pilot's source-binding error and is not used to diagnose it.

## Published payload and observer provenance

Python **3.14.7**, native x64/arm64 observer architecture matches each RID.
Both Mac Swift clients actually compiled successfully, with source SHA-256
`bd4f387ecf3c8009de9243cc14f77354330d601a52986a33b92573bba26fd72f`.
Driver and artifact-auditor hashes were independently recomputed from the run's
Git blobs, accounting for Windows CRLF versus Mac LF checkout. They match the
reports; line-ending differences are not different executable logic.

| RID | Published executable bytes | Pre/post pilot executable SHA-256 |
| --- | ---: | --- |
| win-x64 | 7,105,024 | `2df493b5e3d7402b636c2ca540e4377f00b48a78426ab88b0dcbab7ad16460d9` |
| win-arm64 | 7,244,288 | `520ca50236c180db45316853a0b3e465546d377ed0ba499ad31ce1b0957bad08` |
| osx-x64 | 16,842,440 | `ef98590aa12322ef0ca1f8d000fe27308beb59c77a29174c1c9c60ce6ded9305` |
| osx-arm64 | 16,505,608 | `0aa07f3fea66377e28582462b11611cd561b47e565a434bff5d909b364403f96` |

The pre/post hash agrees per report. The pilot inventory check requires exactly
one regular executable in the publish directory before launching. Separate
`native-inventory-*` artifacts for ARM Windows and both Macs corroborate one
payload, exact byte size, zero other payloads and zero bundled native libraries.
Windows x64's publish-inventory JSON is present in its raw job log with the
same single payload/byte count, but no separate x64 inventory artifact is listed.
No published executable was uploaded in this run; the audit therefore verifies
reported identity and corroborating inventories/logs, **not an independent local
rehash or execution of the downloaded product binary**.

## Exact file and trace evidence

Independent bounded-batch reconstruction of the documented ASCII JSON array
agrees with both declared input and single `r`→`X` saved hashes on every RID:

| Bytes | Original SHA-256 | One-byte edit SHA-256 |
| ---: | --- | --- |
| 1,048,576 | `9004bc8156e480461b02205536ee607414186c776078a8f35b20716c7e668355` | `7fb64708951aa7dc1337a43c841aa7a5e0f3807f53875da4e1ea965719248b3a` |
| 104,857,600 | `11c596afa32f508d22cf7704eb458200fd66c8ec05af3d9f4a384aeb0570c5db` | `f11fa45a8ff38a7fcf4c8cb925ed3775294cb98169e2aa75077b303d22459d09` |

Both Windows 1 MiB reports witness Complete/zero diagnostics at v0 and v1,
one edit attempt, disk unchanged before Save, saved/final working hash equal to
the edit oracle, immutable original hash unchanged, and two normal exits. Raw
edit-session traces contain 26 records each. Independently checked successful
single terminal roots, session/span uniqueness, no drops, exact endpoint counts,
v0 startup/open, pre-edit v0 action and v1 commit/presentation/draw. Open draw's
parent is open-to-editable; edit draw/action's parent is edit-to-presentation.
Fresh reopen traces contain no edit/commit/Save/draw-edit operation. Retained
file SHA-256 matches the report's trace hashes; causal and endpoint integrity
are pass. There is no observed false-positive trace acceptance in these cases.

The unversioned engine `document.open`, `document.save` and `save.completed`
records **do not themselves certify a revision**. Accepted native status and
independently checked exact saved bytes are separate witnesses; versions are
not fabricated for absent attributes. This preserves the prior successful-action
and conflicting-version rejection contract rather than relaxing it for CI.

Both Windows 100 MiB reports reached bound source, Complete v0, one acknowledged
edit, Complete v1 and unchanged working disk before Save. After the 60-second
Save oracle deadline the final working hash still equals the original, not the
edit oracle. Both processes were forcibly cleaned up; no normal exit or GUI
reopen is certified. Each retained 100 MiB trace is **0 bytes**. Buffered trace
loss prevents identifying whether Save dispatch, replacement, another failure
or a modal prevented completion. Do not infer a replacement HResult, modal
purpose, no Save attempt, or reliable original preservation from that absence.

Both Mac sizes have zero edit attempts, forced cleanup, unchanged immutable
and working hashes, and **no uploaded trace files**. `mac_client_compiled=true`
proves compilation, not source readiness. The reports classify RuntimeError,
not PermissionError/blocked, but do not preserve the bounded client's first
failed attribute or capability result. They cannot distinguish tree/ownership/
source-contract failure from other runtime observation failures. There is **no
evidence here establishing a TCC denial**, source focus, successful Quartz edit,
semantic certification or normal terminal session. No permission prompt or
TCC-state workaround was attempted.

## Timing endpoints, not latency claims

These are individual trace-on observations on just-written, non-evicted files.
The two fresh process sizes are not a latency distribution. Parent clocks include
observer overhead and 50 ms polling; child clocks begin after configuration and
must not be subtracted from parent clocks. Native draw callback return is not
compositor presentation, whole-window paint, physical key acknowledgement or
photons. Failed cases have no successful full-workflow latency.

| Windows 1 MiB endpoint (ms) | x64 | ARM64 |
| --- | ---: | ---: |
| Parent launch → bound source | 162.0263 | 154.2519 |
| Child startup → editable | 94.615 | 81.548 |
| Child open → editable | 18.086 | 13.410 |
| Child open → source draw return | 26.925 | 20.580 |
| Parent edit dispatch → source acknowledgement | 13.3568 | 13.9150 |
| Child edit → source draw return | 9.426 | 10.393 |
| Child edit → semantic presentation | 155.872 | 130.789 |
| Child Save | 17.014 | 34.454 |

The 100 MiB Windows parent source acknowledgement is 618.3253 ms (x64) /
457.8447 ms (ARM64), edit acknowledgement 12.6922 / 13.4274 ms. These earlier
phase observations cannot be promoted to full-workflow pass, cross-architecture
performance superiority or p95 acceptance. No child trace endpoints survive
for those samples, and no Mac source-binding timing was achieved.

## Reproduction and discriminating next investigation

Artifacts were downloaded only under repository `.cache/ci-36799464145-json-<RID>`.
Raw complete logs: `.cache/ci-36799464145-json-all.log`. Independent assertions
and structured output: `.cache/ci-36799464145-json-audit.py` and
`.cache/ci-36799464145-json-independent-summary.json`. No product/harness was
modified or native process rerun for this audit.

```powershell
gh run view 36799464145 --log > .cache/ci-36799464145-json-all.log
gh run download 36799464145 --name native-json-large-win-x64 --dir .cache/ci-36799464145-json-win-x64
# Repeat the artifact command for win-arm64, osx-x64 and osx-arm64.
python .cache/ci-36799464145-json-audit.py
```

The independent provenance/corpus/retained-trace assertions pass for all eight
samples; that means the reported failures and scoped successes are corroborated,
**not that all samples pass the product workflow**.

The next useful Windows probe is bounded read-only owned-window/modal metadata
at the first Save failure plus safe normal terminal evidence, without another
Save attempt or broad retry. The next useful Mac change is retaining the first
bounded client failure/capability classification without source text, foreign
metadata or changing TCC. These distinguish causes that the present reports
collapse; repeating the same opaque pilot would not resolve them.
