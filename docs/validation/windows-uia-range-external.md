# External Windows Native AOT UIA range validation

## Contract and method

`tests/WindowsUiaRangeExternal.ps1` launches the ordinary product route with a
synthetic `abcdef\nsecond line\n` file and isolated `MOTE_HOME`, entirely under
repository `.temp/`. It generates a managed .NET 10 WPF UI Automation client in
repository `.cache/`. The client independently discovers only the launched PID's
windows, requires exactly one `mote.source.document` and Document control type,
and exercises the actual Native AOT COM boundary. No theme workflow, registry,
global keyboard, clipboard, or foreground/focus mutation is involved.

Expected range behavior is derived from the fixture: clone DocumentRange, collapse
End to Start, move End three Characters and Start one Character. Endpoint
comparisons against the unchanged original document Start must be exactly 1 and
3; selected text must be `bc`. Select must publish that global selection without
changing the source or original clone. Further range mutation must not mutate the
selection clone. Target-owned WM_CLOSE must exit normally, and the held range
must reject access after close with ElementNotAvailable.

The owner process independently limits the UIA client to 60 seconds (configurable
1–120), retaining stage reports before potentially blocking calls. Cleanup closes
only the process it launched; any forced kill is explicitly recorded. The script
returns the client exit code and never turns a Select failure into a pass.

## Reproduction and observed result (2026-10-01)

```powershell
pwsh -NoProfile -File tests/WindowsUiaRangeExternal.ps1
```

Environment: Windows x64, PowerShell 7, .NET 10 external managed STA client;
fresh parent-published `.cache/uia-range-aot/mote.exe`, 7,122,432 bytes,
SHA-256 `A23E12F28BB977A5EA045BC9F5580E43A350B6A73EB4C7D17AB2A55846BD4C92`.
Artifact: `.cache/windows-uia-range-external/report.json`.

Three attempts, including the final explicit Document-control-type check, consistently
reached Select with endpoint distances **1/3** and text **bc**. Both calls threw
`System.InvalidOperationException`, HRESULT **0x80131509**, from the external
UIA marshaling boundary. Client managed thread ID was 2, apartment STA. The
target's controller thread identity was not observed: this HRESULT is consistent
with the known WrongThread mapping but does not by itself prove which internal
rejection branch fired. It is a genuine external Select integration failure,
not a missing UIA dependency or a fixture expectation mismatch.

The target then closed normally with exit 0, **no forced cleanup**; the fixture
file remained byte-equivalent UTF-8 source. Thus exact endpoint navigation and
clone-read behavior before Select are verified, but global GetSelection after
Select, further clone independence, and stale-range-after-close are **not
verified**, because the harness stops on the first consequential failure. The
full external workflow remains **FAIL**, independently of managed unit tests.

## Next discriminating check

The provider owner should locate the actual Select callback thread and rejection
outcome without weakening thread/lifetime guarantees. After a causal fix,
republish the same ordinary Native AOT binary and rerun this exact harness; a
green run must include all downstream assertions, not merely a successful Select
HRESULT. No production files were changed by this validation assignment.

