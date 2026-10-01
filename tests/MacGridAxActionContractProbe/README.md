# Independent macOS AppKit action contract control

This synthetic native executable does **not** launch, link or patch mote. An
AppKit owner exposes seven empty `AXTable` elements and starts an exact-PID
external client. Each element uses the same owned, next-turn native menu and
tracking-mode cancellation. One advertised AXShowMenu is attempted per variant;
no action retry, global input, app activation, TCC change or source file exists.

Variants compare compiler-provided versus runtime class metadata, native/B/c
BOOL encoding, the correct `isAccessibilityEnabled` getter versus the misspelled
`accessibilityEnabled`, and a narrow legacy action-name/perform-action bridge.
The legacy controls follow a Chromium production precedent, not a conclusion
that mote requires the old API.

```powershell
./tests/MacGridAxActionContractProbe/Test-Guards.ps1
./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-arm64
```

`Run.ps1` refuses reports outside repository `.cache/ci-inventory`; fixed native
artifacts stay under `.cache/mac-grid-action-contract/<rid>/`. Native compilation
uses the installed Apple SDK. The owner has a 45-second process-tree watchdog;
the client checks a 20-second admission deadline at bounded phase/poll boundaries,
has a 32-node discovery bound, array count-before-
copy, exact PID checks and at most 20 state polls per variant. Admitted native
calls can outlive the client deadline; the owner watchdog is the hard bound.
Output records
only fixed names, method type strings, booleans, errors and counters.

`control-completed` means that all seven controls were discovered, advertised
the action and independently opened/closed exactly one menu. It deliberately
does **not** mean all action return codes were successful. Always compare actual
`actionError` values and native modern/legacy dispatch counters. Native trust
unavailability remains separately classified. Portable preflight tests certify
only paths/schema and never claim AppKit compilation or AX execution.
