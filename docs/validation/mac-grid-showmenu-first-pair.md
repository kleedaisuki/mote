# Lean macOS Grid ShowMenu first pair

Date: 2026-10-01. Status: hosted C0/P0 raw replies observed; aggregate reports
rejected scalar JSON types. **Schema-only repair awaits hosted verification**.

## Question and scope

The original 41-check external product gate still reports AXShowMenu -25205,
while seven separate native controls report success. Those clients/preparations
differ, and the previous control also invokes the action twice during metadata
inspection without admitting a menu. The new C0/P0 experiment asks whether the
same fresh exact-PID native client reproduces that difference without the old
preparation or action warmup. It is not a product behavior fix.

The design and independent reviews are in
`../architecture/mac-grid-showmenu-discriminator.md`,
`../reviews/mac-grid-showmenu-discriminator-review.md`, and
`../reviews/mac-grid-showmenu-first-pair-implementation.md`.
Only C0 then P0 run in one invocation. Both targets/clients are new processes;
the compiled client hash is identical. Broad getter-selector tracing is deferred:
`dispatcher_observation_available=false`, `server_calls=[]` means unavailable,
not absence of framework getter traffic.

## Implementation contract

- `tests/MacGridShowMenuDiscriminator/{Client,Control}.m` implement the shared
  client and standalone runtime-B control. No Metadata/NSInvocation, direct
  action invocation, selector-permission warmup or 150-ms scheduled cancellation
  exists in this control.
- Client discovery is bounded generic Children BFS through the owned window,
  pruning Table/Row/Column descendants. Only fixed role/identifier classification
  is retained; no source values or menu contents are queried. Unsupported
  Children denotes a leaf; other discovery failures remain unavailable.
- The sole action result is atomically written **before** the post-reply audit.
  A retained Table receives one bounded rediscovery/CFEqual and parent/window
  audit; exhaustion produces unknown. No fallback action or input is permitted.
- Product observation is internal, disabled by default, gated by both Grid AX
  and `MOTE_NATIVE_GRID_SHOWMENU_DISCRIMINATOR=1`. The session is an existing
  absolute no-link repository `.cache` descendant. Ordinary launches have no
  timer, diagnostic file I/O, or extra registered diagnostic selectors.
- Action-entry/refusal facts are fixed preallocated fields, including unknown
  off-main owner fields. AX callbacks do not format JSON or perform I/O. Each
  action/lifetime row array is capped at 16 with separate total/overflow facts.
- Owner readiness checks actual visible attachment/frame/menu availability,
  without invoking the action or reserving the queue. Control frame availability
  refers to its initialized synthetic frame, not a product document frame.
- A fixed finish marker is emitted after the original reply and audit, except
  no-action unresolved cleanup (ordering flags null). Hung action emits none.
  Default/tracking-mode timers consume it, cancel only the owned menu, preserve
  its original did-close notification, and defer window closure to default mode.
  Pending admitted requests are cancelled without clearing the shown-menu
  relation. Timers and delayed cleanup callbacks are invalidated on disposal.
- Server `normal_shutdown` is an owned normal-close/stop fact, not independently
  proven process success. Driver actual owner exit, forced cleanup and unchanged
  fixture hash must be checked separately.

Ready/finish polling is at most 20 attempts separated by 150ms within the 12s
ceiling; the client has 55s overall, 1s per-element messaging timeout and 12,000
API admissions. Discovery caps are 256 nodes/depth12/Children128/action names16.
The single post-reply audit is additionally bounded by 3s/128 admissions. Outer
owned-process watchdog is 75s with at most 10s owner-exit wait. Neither cooperative
AX timeouts nor timer scheduling are hard native execution guarantees.

`Run.ps1` verifies the published product directory contains exactly one binary;
synthetic helpers/reports remain in repository `.cache`, not the payload. It
drains/discards target/client streams, validates fixed JSON keys/enums/types,
and preserves raw interrupted action snapshots separately from final reports.
Compiler-only failure logs are bounded and collected before any target launch.
The existing 41-check gate and seven-control diagnostic remain unchanged.

## Portable validation (Windows)

Commands run from `D:\Code\mote`, .NET SDK10.0.400:

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj --no-restore -v:q
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-build --no-restore `
  --filter 'FullyQualifiedName~MacGridPairObservationTests|FullyQualifiedName~MacGridMenuDiagnosticTests|FullyQualifiedName~MacGridMenuPresentationTests' `
  --logger 'trx;LogFileName=focused.trx' --results-directory .cache/tests/mac-grid-pair -v:q
pwsh -NoProfile -File tests/MacGridShowMenuDiscriminator/Test-Guards.ps1
```

Build: zero warnings/errors. Focused tests: **24/24**, including five new
off-main/unknown/overflow/path/ABI facts tests. TRX:
`.cache/tests/mac-grid-pair/focused.trx`. Driver guards: **27/27**; fixture is
263,760 UTF-16 units, 1,100 records, ragged record2, with the same hash/oracles as
the original external fixture. Workflow YAML parses successfully.

These checks do not compile Objective-C, load AppKit, validate menu tracking,
establish either original AX reply, or measure observer overhead. Both Mac RIDs
must execute the non-gating first-pair step and have their raw reports audited.
`completed-failure-observed` is valid evidence collection of a failing original
reply, never product `passed`. No VoiceOver, physical input, IME, arbitrary-file,
release or causal-fix claim is made before those observations.

## First hosted execution: CI 36815303415 / 6750cd9

[Run 36815303415](https://github.com/kleedaisuki/mote/actions/runs/36815303415)
executes the new first-pair step on both Mac RIDs. The containing non-gating
steps appear successful, but the **raw owner reports are `probe-error` on both**:
`error_code=native-typecheck-failed`, `native_typecheck=false`, `compiled=false`,
`sessions=[]`. No C0/P0 target/client was launched, and no action return,
identity, admission, lifecycle or normal-exit evidence exists for this pair.
The source hashes are unchanged and strict product one-file inventory passed.
Completed raw job logs independently print `probe-error`, the pair-unresolved
wrapper error and actual step exit1 on both jobs (x64 job110218955959;
ARM job110218955927). Thus the green containing jobs do not establish even
experimental completion.

| Prelaunch fact | osx-x64 | osx-arm64 |
| --- | --- | --- |
| Host architecture | X64 | Arm64 |
| SDK toolchain in compiler log | Xcode16.4 | Xcode26.6 |
| Compiler failure | Control helper `Marker` collides with SDK typedef | Same |
| Diagnostics | 2 errors | 2 errors |
| Product binary SHA256 | `4AADB1127CB48C872772C88E385ED3111120C940D21575B99833BEEE22CA1204` | `8C870C20B7F8A5F1284A89481F874ECB91351D6DC49BFAFA82AA125D20316C94` |

Both bounded compiler logs locate `Control.m:120` and call site177; SDK
`CarbonCore.framework/Headers/AIFF.h:133` declares `typedef struct Marker Marker`.
This is a demonstrated **diagnostic helper compilation defect**, not a product
AX or Native AOT callback failure. Client compilation occurs earlier in the
driver, but the aggregate compiled flag does not certify a usable pair. Raw
artifacts and compiler logs are preserved under
`.cache/ci-36815303415-grid-pair/{x64,arm64}/`.

Both fixture reports preserve 263,760 units and SHA256
`8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2`.
Source pins match the reviewed LF snapshots: Client
`FAD0E752418E6CFE2529469E23B12C9DDEC4BE58369534BA6D9E120B56068C77`,
Control `BC78F9EA2331738D36E6FCB6EEF152FDCFBACFC5925A7D8CFDA5CE4E26978B3D`,
driver `EC842CF260BD91034B5D37DF79E4161D4C1A0B24AF204840B131E67F3B22C872`.

The unchanged original external product probe **does execute independently**
in this run: both raw reports have41 checks,40 true and only
`context-menu-accessible=false`, original AX=-25205, actual Swift exit1.
Both observe editor normal exit0, no forced cleanup, unchanged fixture and
completed guarded downstream navigation. These separate normal-exit facts
must not be transferred to the unlaunched C0/P0 pair. Reference reports are
under the same audit root in `reference-{x64,arm64}/`.

Bounded harness repair `60f7bd4` renames only the helper declaration/call to
`PairFinishMarker`; no action behavior, schema, budget, cleanup or product
implementation changes. Updated Control hashes are LF
`0BCE7C4678221B16140BF424E33761E17471E6C171331CC657D985B053BEE693` and
CRLF `82330DADF9F9978ADD71375846C7511542A6C43F57E1515ADA4F2F7610B75A80`.
Workflow pins are refreshed in `904037d`; portable guards27/27, YAML parsing and
all four LF/CRLF pin pairs were revalidated. A repeat compile
and real C0/P0 execution are required; **the original causal question remains
unanswered**, so no product bridge/getter/lifetime repair is justified here.

## Corrected-control execution: CI 36816778414 / d6b355b

[Run 36816778414](https://github.com/kleedaisuki/mote/actions/runs/36816778414)
compiles both native helpers and runs all four sessions. Both AOT jobs finish
green (x64 job110223459425, ARM job110223459488), but raw owner reports are
**`unresolved`**, each session is `probe-error/report-schema-invalid`, and
completed job logs show the actual first-pair step **exit1**. This is no longer
the preceding prelaunch failure: native_typecheck/compiled/source_unchanged and
strict_one_file_inventory are all true, with real client/server raw reports.

| Raw session fact | x64 C0 | x64 P0 | ARM C0 | ARM P0 |
| --- | --- | --- | --- | --- |
| Client status / trusted / ready | reply-observed / true / true | same | same | same |
| Names error/count/advertised | 0 / 1 / true | same | same | same |
| Action attempts / begin / end | 1 / true / true | same | same | same |
| Original AX reply | **0** | **-25205** | **0** | **-25205** |
| Total entries / admitted requests / dispatch attempts | 1 / 1 / 1 | same | same | same |
| Native opens / closes / detaches | 1 / 1 / 1 | 1 / 1 / 1 | **0 / 0 / 1** | 1 / 1 / 1 |
| Client / owner actual exit | 0 / 0 | same | same | same |
| Forced cleanup / unchanged fixture | false / true | same | same | same |
| Ready marker polls | 1 | 4 | 2 | 5 |
| Prelude API admissions / role reads | 85 / 8 | 257 / 28 | 85 / 8 | 257 / 28 |
| Post-reply API admissions | 128 | 128 | 107 | 128 |
| Post-reply audit exhausted | false | **true** | false | **true** |

Every server action row records main-thread/owner lookup/current-root/attached/
frame/baseline/generation-equality/epoch-equality/queued/returned **true**,
installing/exception **false**. All entry/lifetime overflow and off-main counts
are zero. Product lifetime rows are queue, dispatch, open, close, detach;
the first four retain current/attached/baseline equality, detach clears them.
C0 ARM instead has queue, dispatch, detach only. Server ready/finish/normal-close
flags are true. Driver independently observes exit0 for every owner/client.
The raw client cleanup flags both say true after reply/audit; aggregate
`finish_after_reply=false` is the driver's **unassigned default after parser
failure**, not evidence that cleanup preceded the reply.

### Scalar schema failure, isolated reproducibly

Client `owned_target` is JSON integer **1** in all four reports. The control's
four positive CFEqual fields (retained/current, parent/Group, window, top-level)
are also integer1, not JSON Booleans. Product equality fields remain null because
the audit never reaches them. Native `@(found!=nil)` boxes a comparison's
integer type, and `@(CFEqual(...))` boxes the CF Boolean's numeric type;
Foundation serialization does not convert them into canonical JSON Booleans.
The strict parser correctly refuses those wire types. No parser coercion is
introduced to erase this defect.

Audit reproducer `.cache/ci-36816778414-grid-pair/schema-repro.ps1` loads the
actual driver validator without launching anything: **4/4 raw clients fail**;
only a fresh in-memory copy with those specific scalar conversions passes the
same parser; **4/4 raw servers already pass**. Original files are untouched.
This local diagnostic identifies the schema mismatch, not substitute acceptance
or repaired hosted evidence. Raw artifact/log roots are
`.cache/ci-36816778414-grid-pair/{x64,arm64}/` and `job-{x64,arm64}.log`.

Schema-only `597317a` introduces `PairBoolean(BOOL)` returning explicit
`@YES/@NO`, used solely at the owned-target and CFEqual conversion sites.
Regression `0159a6b` keeps numeric1 rejected at all five fields, tests valid
false/null and source conversion contracts: **37/37 guards**, independently
reproduced by the reviewer (`c204e41`). Source-contract tests are not Foundation
execution. Pin update `6138be8` changes only the Grid Client/guard hash lines;
YAML and all four LF/CRLF source pin pairs verify. No action/discovery/audit/
cleanup/budget/product behavior changes occur in this repair.

### Why P0 exhausts its unchanged 128-call identity ceiling

Both product post-reply call ledgers are exactly:
49 PID checks +48 timeout installations +13 Role reads +13 child-count reads
+5 child-array copies =128 admissions. **All are spent in the one generic
re-discovery**, whose prelude equivalent visits28 nodes and costs257 admissions
including the eventual names/action. The post-reply finder gets through13 role
reads and stops after its last admitted PID check; it never establishes a
unique current Table or reaches the later Parent/Window/TopLevel checks.
Therefore every product external equality/parent/cycle/reciprocal relation is
unknown, not false or evidence of stable identity. This is deterministic budget
pressure in the observed graph, not an AX error/timeout during a parent read.

C0 ARM completes the audit in107 admissions; x64 uses exactly128 but completes
its last required call, so `audit_exhausted=false` is consistent. C0 retained
Table/Group/window/top-level comparisons are positive numeric1, reciprocal
children has exactly one occurrence, parent reaches the window without cycle.
Those control facts cannot be transferred to the product graph.

The cap remains128. No additional finder, wider budget, missing-read-as-success
or pre-action graph warmup is added. An explicit `audit_exhausted=true` plus
null graph facts is the current record. If external parent relationships become
the discriminating question, a separate bounded audit can put the direct local
Parent/Window/TopLevel/reciprocal reads **before** generic rediscovery while
retaining the same cap and unknown retained/current equality when the finder
does not finish. That changes only post-reply observation order and must be
reviewed/run as a separate experiment, not silently folded into scalar repair.

### Lifecycle and next experiment decision

ARM C0 has action admission and dispatch attempt but no menu-open/close callback.
Its code can refuse presentation after finish consumption, but the ledger does
not record finish-at-dispatch or whether native popup was invoked. It therefore
does **not identify preemption** rather than a native presentation that produced
no open notification. C0 AX0 means successful original action triggering,
not proven complete popup lifecycle. This first sample does not establish
matched lifecycle completion across both RIDs.

The agreed next hosted invocation is **scalar-only**, isolating the report
repair and preserving the zero-open observation. No guessed delay or adaptive
cleanup is introduced now. A future bounded completion discriminator could
have each existing opt-in owner timer write one fixed `menu-opened` marker after
the already observed opens counter becomes positive (never I/O in AX/menu
callbacks); after persisting the reply/audit the same client could wait within
an explicitly shared <=1s/overall remaining deadline before finish. No menu
action, getter, retry, activation or input would be added. Timeout would remain
`open-unobserved`, not a pass. This is feasible but adds a marker contract and
makes cleanup timing depend on the observed event, so it is deferred rather
than confounded with the scalar repair. The independent reviewer also records
a fixed250ms quiet-window alternative; it does not guarantee menu opening and
is not adopted as a guessed causal correction.

**Supported insight:** the same compiled client on each RID obtains C0=0 and
P0=-25205 under minimal preparation, with one admitted/current main-thread
callback. Thus the old Swift client and original selection-setter preparation
are not necessary for the **observed raw product failure**. Aggregate schema
acceptance, product external identity auditing, matched lifecycle completion
and reversed-order confirmation remain missing. No Native AOT bridge cause,
selector-history absence, repaired product AX, VoiceOver, input or release
acceptance follows from these samples.

### Hashes and unchanged original gate

Both sessions on a RID share the single compiled client hash, but different RIDs
naturally have different native executables:

| SHA256 | osx-x64 | osx-arm64 |
| --- | --- | --- |
| Shared client | `563E42285CA2964E7B05630CEC58AAF7A833F39C6707A96C2D337F0EAD03D8C2` | `5575B48EE6F1AC1963E2D6A6FF08BB1D78A9CE280F15E702F7A9128162CD8C2D` |
| Control | `369773793D40F2C5269F62D9B90F02605182BC147FA0104E9054F5BCFEC40B2B` | `64FF73113DF69A30A67181BB5C45BC181B82C91C5636DB5514D9C3E8AD74306D` |
| Product | `FB9BD6A1B9EF38F963006A8E011390323896E417AFD671198F505B6E5344302E` | `D601360EFE5CE028E57E778A73E4CB4603C7A6687C1721A938D85BA77E09F8B0` |

Executed source hashes are unchanged from the corrected first-pair pins:
Client `FAD0E752...B56068C77`, Control `0BCE7C46...53BEE693`, driver
`EC842CF2...F3B22C872`. Every owner reports the original fixture hash
`8BACC6F9...C98DD8C2`,263760 units and unchanged bytes; full source/fixture
hashes are preserved in the raw owner reports and earlier sections.

The separately executed original41-check probe on **the same product binaries**
is failed/Swift exit1 on both RIDs, with40 true and only
`context-menu-accessible=false`, original AX=-25205. It completes guarded
downstream navigation, observes normal editor exit0/no forced cleanup and
unchanged source; its six-phase native trace has exactly one admitted request,
open and close. Raw reference reports are in `reference-{x64,arm64}/`.
That unchanged independent failure prevents relabeling the instrumented first
pair or schema repair as product acceptance.

## External grounding

Apple documents that a timer can be registered in multiple modes and that
`invalidate` removes it from all installed modes:
[NSRunLoop addTimer:forMode:](https://developer.apple.com/documentation/foundation/runloop/add(_:formode:)-392ag?language=objc).
The [Threading Programming Guide run-loop chapter](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Multithreading/RunLoopManagement/RunLoopManagement.html)
distinguishes default and event-tracking modes. The product resolves exported
framework mode constants instead of guessing NSString wire values. These
documented mechanisms justify the cleanup design, not an unexecuted guarantee
that hosted AX transport returns before a tracking loop starts. The design's
failure-preserving reduction rationale remains the prior IEEE TSE2002 evidence;
no speculative product bridge or getter change is introduced.
