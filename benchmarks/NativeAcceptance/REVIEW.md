# Native acceptance artifact review

## Scope and outcome

Reviewed `acceptance.py` and `test_acceptance.py` on 2026-10-01. This review covers the artifact-only corpus generator, trace reader, causal integrity audit, descriptive distributions, and output confinement. No GUI driver, target probe, clipboard, user-home operation, or production change was executed. No files were staged or committed.

**Final disposition: no remaining substantive findings in the reviewed artifact-only scope.** The initial two P2 findings below were repaired by the tool owner and independently rechecked after source freeze. Preserve them as resolved history, not open issues. They did not demonstrate a defect in the native trace producer.

## Resolved initial findings

### P2 — Missing draw identity and cyclic causal graphs receive an integrity pass

Location: `acceptance.py`, `audit()`, parent traversal and draw-version comparison (approximately lines 200–214).

The `parent_span_id is None` early continuation bypasses the operation-specific draw-parent requirement. A successful `document.edit_to_draw_submission` with no parent therefore passes even when exact expected record counts are supplied. Separately, `.get("version")` compares two missing versions as equal, accepting a draw that cannot be associated with a document revision. Checking immediate parent existence is also insufficient to reject cycles: making the presentation parent point back to its draw produces a presentation/draw cycle, and the audit still passes.

Reproduced with three synthetic records: successful `mote.session` span 1; `document.edit_to_presentation` span 2 parent 1 version 1; `document.edit_to_draw_submission` span 3 parent 2 version 1. Independently mutate (a) draw parent to null, (b) remove both version attributes, or (c) presentation parent to span 3. Each returns `pass` with `expected={"document.edit_to_draw_submission": 1}`.

Impact: a report presented as a causal-integrity certificate can certify disconnected or impossible endpoint attribution. Such durations should not be accepted as revision-correlated native callback observations.

Remedy: require a parent and an explicit nonnegative revision for draw endpoints, require the intended parent operation and explicit matching parent revision, and reject cycles across the complete identity graph. Do not require serialization order: real records are intentionally emitted out of order. Add separate negative controls for null draw parent, absent versions, self-parent and multi-record cycle. Confidence: high, directly reproduced.

### P2 — A loss marker without its count is interpreted as zero loss

Location: `acceptance.py`, `load_records()` numeric attributes and `audit()` dropped-record aggregation (approximately lines 157–160 and 215–217).

The reader permits `telemetry.dropped` with `attributes={}`; the audit defaults missing `count` to zero. Appending that marker to otherwise valid evidence still returns `pass` and `dropped_records: 0`. The production `JsonlTraceSink.DropRecord()` always emits the positive loss count; omission is malformed evidence, not evidence of no drops.

Impact: malformed or incomplete loss accounting is silently certified as uncensored data.

Remedy: require a strictly positive integer `count` for `telemetry.dropped`, or at minimum mark missing/invalid count as incomplete rather than using zero. Add reader/audit negative controls for omitted, zero, Boolean and negative counts. Confidence: high, directly reproduced.

## Initial observations and resolution

- Initial CSV observation resolved: the final row now has four fields, and the grammar test checks the field count of every row.
- Quantile documentation observation resolved: the docstring now explicitly distinguishes median p50 from nearest-rank p95.
- Outputs do not export source paths or arbitrary trace attribute strings. The tool never opens a user document or runs a process. Path validation rejects traversal and existing symlink/junction ancestors, and exclusive output creation protects existing report files. No demonstrated privacy/path escape was found in this bounded review. This does not claim resistance against concurrent hostile filesystem mutation.
- Reported durations are per supplied sample and per operation; cancelled outcomes are kept separate. Small samples remain descriptive, and the endpoint explicitly disclaims physical presentation. No target binary provenance, workload execution truth, AOT behavior, OS input latency, or performance SLA is certified by this tool.

## Verification evidence

Command: `PYTHONDONTWRITEBYTECODE=1 python -m unittest discover -s benchmarks/NativeAcceptance -v` (PowerShell environment assignment used). **6/6 passed**. All writes from those tests remained under `.temp/native-acceptance-tests/`; cleanup removed only exact test files.

Independently read all four retained JSONL files under `.cache/ci-36754713708-mac-draw-{arm,x64}`. Each has one successful terminal session, two edit-to-presentation records and two edit-to-draw-submission records: one cancelled revision 0 and one successful revision 1. All four pass with exact counts supplied. Matching parent identity and revision are present; out-of-order serialization is genuine. This is compatibility evidence for those traces, not a new macOS probe or a startup/physical-paint measurement.

## Re-review status

Independent re-review after source freeze: **6/6 tests passed**, including expanded negative controls. Each original false-pass reproduction now returns `incomplete-or-invalid` with the appropriate issue: `missing-causal-parent`, `draw-parent-version-unavailable`, `causal-cycle`, or `invalid-dropped-record-counter`. The four retained Mac traces still pass the exact-count audit. Source now requires one successful root session, rejects absent required fields, checks all non-session roots, detects cycles with a linear nonrecursive chain walk, and rejects omitted/zero loss counters. Boolean/negative numeric attributes fail in the reader.

Reviewed raw-file SHA256:

- `acceptance.py`: `b2d45fe1eac0f094bf997be8ea3777921e7019d09f958c03f7a6be6e1ed7f541`
- `test_acceptance.py`: `1902b5484ed1a017c258446d61dfca3dc91040414a8db2d1eb8da2d154c41a77`

Binary SHA remains optional by design, with explicit `unavailable` or `caller-supplied-not-verified` labeling. Missing coverage expectations remain explicit rather than certifying workload completeness. These limitations are appropriate for retained artifact diagnosis and do not imply target execution or measured acceptance. No further necessary correction was found in the frozen changes. GUI drivers, target runtime, adversarial concurrent filesystem races, and representative corpus coverage are outside this review.



### Bounded Python 3.11 path-compatibility delta

Statically reviewed the additional `stat` import and existing-ancestor `lstat().st_file_attributes & FILE_ATTRIBUTE_REPARSE_POINT` guard in `artifact_path()`. This supplements the optional `os.path.isjunction` call for Python 3.11 and rejects existing Windows reparse-point ancestors rather than depending on a newer Python API. On platforms without Windows file attributes the attribute lookup is zero, preserving ordinary path behavior. No trace-audit or corpus-generation code changed in this delta. No new substantive concern found. The tool owner reports the delta test rerun remained 6/6; this reviewer did not repeat the completed suite. The raw acceptance.py hash above was independently checked and updated; the test file hash is unchanged.
