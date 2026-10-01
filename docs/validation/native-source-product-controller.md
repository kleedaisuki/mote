# Native source product controller validation

## Scope and independently derived contract

The explicit `EditorPresentationProfile.NativeSource` product candidate must use one complete source replica, not a bounded text page. The engine remains authoritative for text, history, file identity and saving. Import certification is required before editing; ordinary edits advance actual engine history once; engine-originated changes use guarded native ranges instead of full imports. Native callbacks from old document/version/nonce identities must not mutate current text. A failure cannot roll back a committed engine transaction or silently discard unadmitted native text.

`tests/Mote.Tests/NativeSourceControllerTests.cs` exercises the actual production `NativeEditorController` with an independent portable `INativeEditorShell` / `INativeSourceShell` buffer. The fake maintains its own string, validates replacement preconditions, returns exact readback, and queues actual controller completions. It does not execute GUI, global input, system clipboard, or native text controls. Reflection reads canonical Document and binding fields solely to inspect actual history/version/direction and global interest; it does not mutate production state.

## Evidence

Environment: Windows, repository `D:\Code\mote`, managed Release `net10.0`. The parent integration owner coordinated one shared build; this validator did not start competing builds or repeat passed tests.

- `.temp/native-source-product-build-5.log`: build succeeded, zero warnings/errors, 4.23 seconds.
- `.temp/native-source-product-test-2/native-source-product-2.trx`: 78 selected cases passed, zero failed/skipped. Console duration 344 ms covers the combined selection, not just controller cases.
- Independent XML enumeration identifies **20 controller cases, all Passed**, including all three explicit recovery cases below. No controller case was inferred from the aggregate count.
- Earlier `.temp/native-source-product-test-1/native-source-product-1.trx` contained 17 controller cases, all Passed. That earlier run does not qualify the later recovery API or tests. Initial estimated counts in coordination messages were corrected by actual TRX enumeration.
- Earlier shared compilation problems included a missing `Mote.Formats` test import and in-flight platform signature updates. These were harness/integration setup failures, not accepted behavioral runs.

## Exact coordinated execution

The integration owner executed from repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~NativeSourceControllerTests|FullyQualifiedName~WindowsProductSourceTests|FullyQualifiedName~MacProductSourceTests|FullyQualifiedName~NativeSourceTelemetryTests|FullyQualifiedName~NativePresentationProfileTests' --logger 'trx;LogFileName=native-source-product-2.trx' --results-directory .temp/native-source-product-test-2
```

Console output was redirected to `.temp/native-source-product-test-2.log`. Platform Copy/modal-guard refinements after this run are not qualified by this artifact. No completed controller tests were rerun solely for documentation.

## Tested behavior

| Workflow | Independent expected outcome | Observed |
| --- | --- | --- |
| 90,000-character source plus CRLF and emoji; offscreen emoji substitution | Complete import, one engine version advance/history entry, old echo ignored | Passed |
| Undo twice then Redo | One guarded replacement per actual history change; no full reimport or phantom second undo | Passed |
| No-change candidate | Selection updates without adding a text version/history entry | Passed |
| Backward versus ordered-only native selection | Actual endpoint determines backward direction; absent endpoint remains explicitly unknown | Both passed |
| Global viewport beyond 90,000; stale viewport sequence; copy/select-all/Go To Line | Absolute positions, stale interest ignored, exact substring/full text copy; reveal target global | Passed |
| New followed by retired callback | Fresh installation nonce; current empty canonical text unchanged | Passed |
| Embedded NUL import | Disable uncertifiable replica; exact canonical file contents retained | Passed |
| Failed acknowledgement after commit | Keep committed canonical text and actual dirty history; disable replica | Passed |
| Failed native range publication during Undo | Keep actual engine Undo result; do not pretend stale buffer is certified | Passed |
| Save settlement versus veto | Admit settled pending text before Save; veto creates no file; exact UTF-8 bytes and fresh Document reopen | Both passed |
| Dirty Open cancelled | Preserve nonce, dirty source and Undo; no file picker invocation | Passed |
| Malformed UTF-16 candidate | No canonical mutation/version advance; classify unadmitted native text | Passed |
| Provisional composition versus synchronous settlement barrier | Marked candidate cannot commit; settled final candidate inside generic composing barrier commits once | Both passed |
| Canonical-retained versus unadmitted failure Save | Committed source can be saved/reopened; unresolved native-only text vetoes implicit Save | Both passed |
| Cancel explicit native-only recovery | Keep pending native buffer, canonical source/history and disabled state | Passed |
| Confirm explicit recovery | Certified full canonical import with new nonce; retain dirty canonical text/version/history | Passed |
| Recovery import fails, then second Save without consent | Keep unadmitted classification and veto both commands; no file and no silent discard | Passed |

## Consequential finding

Inspection while adding the failed-recovery case found a barrier downgrade: recovery's failed import could call the default `CanonicalRetained` failure, despite the old unadmitted buffer remaining. That would allow a later Save to bypass consent. The production owner added failure provenance and a recovery scope so failed recovery remains `UnadmittedNativeText` until complete import certification succeeds. The final test requires both the retained classification and a second vetoed Save. This finding was identified by inspection, not reproduced by executing the unfixed binary; the final run verifies the corrected invariant.

The fake's settlement decision models the platform contract deliberately. These tests prove controller consumption of that verdict, not actual Windows/AppKit modal prompting, IME settlement or control import fidelity.

## Qualified hashes (SHA-256)

| Artifact | Hash |
| --- | --- |
| Tests source | `1E7573669D2F86A9647D44CCD828C3B923CDA82A74E6A468F7E8DE34AA56AB70` |
| Controller source partial | `6E7BE9AE8BFFE47168B2207315AFFE698E408BA27A85CCC7C3EB2B5E5F5B8BF5` |
| Binding source | `2BD50614B47361B8AAF2C9944AF7E5AC2D6B533A44C59C033D6D91D3EA108674` |
| Source contracts | `C54888397999097C98C61ED228F1C1AD990DDF0525D1AD5373D3F44CC9FE4657` |
| Actual `mote.dll` | `06DA3AC98CECA67F1D3AEAE6E157884121C27DA821C8D33CFB4A5CBAC6CD4AE4` |
| Actual `Mote.Tests.dll` | `643DCBA28026E47A3E6B1B44C98C5D68DE39DAAD6D7D32B16E85B8DBFEF121FB` |
| Final combined TRX | `156931547CA11FEB482AE0155C5276627339C961CFB80A323C84ED932BD24414` |

## Verdict and limits

**Passed for these portable actual-controller workflows.** This is not a native runtime, Native AOT, physical input, IME, accessibility, performance, or product-default promotion certificate. No fresh process was launched for the save oracle: the test opens a fresh `Document` in the same process. Encoding chooser behavior has separate existing tests and is not newly certified here. Hosted native/AOT evidence is still required.
