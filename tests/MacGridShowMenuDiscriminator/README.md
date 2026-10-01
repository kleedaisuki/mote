# Native shared client and runtime-B control

These two standalone Objective-C programs are the lean C0/P0 components of
`docs/architecture/mac-grid-showmenu-discriminator.md`. They do not replace the
unchanged original product accessibility gate. They are diagnostic-only and
have **not been compiled or executed on macOS during Windows implementation**.

## Build and invocation

Run from the repository root on a macOS host. The integration driver owns fresh
session creation, private product configuration, fixture hashes, exact child
PIDs, process exit checks and the 75-second outer watchdog.

```sh
clang -fobjc-arc -framework Foundation -framework ApplicationServices \
  tests/MacGridShowMenuDiscriminator/Client.m -o .cache/grid-pair-client
clang -fobjc-arc -framework Cocoa \
  tests/MacGridShowMenuDiscriminator/Control.m -o .cache/grid-pair-control
.cache/grid-pair-control "$PWD/.cache/mac-grid-showmenu-discriminator/run/osx-arm64/C0"
.cache/grid-pair-client "$CONTROL_PID" "$PWD/.cache/mac-grid-showmenu-discriminator/run/osx-arm64/C0"
```

Both paths must already exist and resolve beneath the current repository's
`.cache/`; links in the cache or session path are rejected. Each session must
be new: never reuse `ready`, `finish`, or AX handles. `ready` contains exactly
`mote-grid-pair-ready-v1\n`; `finish` exactly `mote-grid-pair-finish-v1\n`.
Marker reads use `O_NOFOLLOW`, regular-file and exact-size checks, and bounded
buffers. No marker content becomes an arbitrary command.

The client writes `client.json` atomically before its sole action, immediately
after the original reply, then after its single post-reply audit. Early files
have only `schema`, `status`, `actions`; an interrupted snapshot is not a final
report. It writes finish only after that final report. When no action was
attempted, finish is safe owner cleanup and both `cleanup` ordering facts are
null; such a session is unresolved, never a completed comparison. A hung
attempt emits no finish: watchdog cleanup remains forced/unknown.

## Observation limits and schema

Client schema is `mote-grid-action-client-v1`; final fields are `status`
(`reply-observed` or `unresolved`), `trusted`, `ready`, `owned_target`,
`discovery` (`attempts`, `nodes`, `admissions`), `actions`, `identity`,
`client_calls`, and `cleanup`. `actions` retains `attempts`, nullable numeric
`names_error`, nullable bounded `names_count`, nullable `advertised`, nullable
numeric `original_error`, and `begin_recorded`/`end_recorded`. Successful
observation of -25205 is still a failed original action reply, not product pass.
`identity` is empty without a reply; otherwise its Boolean equality/graph facts
are nullable. Audit exhaustion preserves unknown rather than false. Its
`audit_admissions` is at most 128 and `audit_exhausted` records bound exhaustion.
Calls contain only sequence, phase (`prelude`/`post-reply`), fixed operation ID
and numeric AX error. No target/client PID is serialized.

The client does not read Enabled, Parent, selection, Rows, text, or shown menu
before the action. Discovery traverses bounded Children generically (including
SplitGroup and ScrollArea), prunes Table/Row/Column, and classifies only the fixed
`mote.csv.table` identifier. Unsupported Children attributes denote normal
leaves; other failed count/read results make discovery unavailable. The
expected Group is the Table's immediate traversal parent only when that
parent has AXGroup role; no pre-action Parent read is added. Ownership and
installed timeout calls count toward
all budgets. There is one finder before the action and one after it; graph
lengths may differ between targets. The 55-second overall deadline, 1-second
per-element timeout, 12,000 API admissions, 256 nodes/depth 12, 128 children,
16 action names and post-reply 3-second/128-admission audit are ceilings.
Cooperative AX deadlines do not replace the outer process watchdog.

Control schema is `mote-grid-action-server-v1`. Fields match the product
observer: `ready_published`, `finish_consumed`, `normal_shutdown`,
`dispatcher_observation_available` (false), `callback_entries`,
`off_main_entries`, `entry_overflow`, `lifetime_overflow`, `action_entries`
(cap 16), `lifetime` (cap 16), `requests`, `dispatches`, `opens`, `closes`,
`detaches`, and `server_calls` (empty/unavailable, not observed absence).
Action facts cover main-thread/owner/current root/attached/installing/frame/
ready baseline/attachment equality/epoch equality/queue/return/exception.
Off-main entries refuse without reading mutable owner state; their unavailable
fields are null. Main entry order is preserved; off-main facts are appended
only at export, so cross-thread temporal order is not inferred. Lifetime phases
are queue/dispatch/open/close/detach with equality/current/attached flags.
Counter overflow precludes complete absence inference.

The empty control's `frame_present` means its initialized synthetic frame and
owned window. Its stable attachment/epoch baseline is target-specific, not a
claim of a document frame lifecycle equivalent to the product. Detach clears
the Group's current Table and attachment, making current-root
and attachment equality false; frame availability and epoch equality are
also false once detached. There is no
metadata action warmup, direct action invocation, NSInvocation, legacy action
bridge, selector recorder, 150-ms cancellation, activation or global input.
A default/tracking-mode timer consumes finish, cancels only the owned menu,
then closes normally. `normal_shutdown` records owner close/stop; the driver
must independently observe a zero process exit. Instrumentation and timer
scheduling can perturb transport; control completion alone is not acceptance.

## Scalar Boolean schema repair

The shared client explicitly converts owned-target and CFEqual identity results
to canonical JSON Boolean objects. Boxing a C relational result or the Core
Foundation Boolean typedef can otherwise serialize numeric `1` rather than
`true`, which the strict report parser correctly rejects. This change repairs
the observation schema only; it does not change discovery, action, identity
auditing, bounds or cleanup, and it is not product acceptance. A new hosted
execution is required to validate the emitted raw reports.
