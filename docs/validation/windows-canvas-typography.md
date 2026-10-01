# Windows Canvas input typography: paired native boundary validation

Date: 2026-10-01. Scope: the ordinary Windows Canvas input ribbon only.
Status: **local native requested/applied em parity established at effective 96 DPI;
not a visual, Native AOT, real IME or display-scale release certification**.

## Decision and preserved contracts

Following [the unit-boundary design](../architecture/native-typography-units.md)
and [the visual product assessment](../product/native-canvas-visual-acceptance.md),
replace Canvas input's `-Round(EditorFontSize * 96 / 72)` request with an internal
`CanvasDipToInputCharacterHeight` boundary: `-Round(EditorFontSize)`. Its input is
the existing DirectWrite em in DIPs; its output is GDI negative character height
in the same current client logical space. Native observations below establish
that this renderer uses 96 DPI and the input DC uses identity MM_TEXT mapping.
Do not independently add monitor DPI to this boundary while source painting and
hit geometry retain their current fixed-96-DPI transform.

Nearest-even rounding is unchanged. Nonfinite, nonpositive, round-to-zero and
integer-overflow requests fail explicitly instead of installing a GDI default
font or invalid height. This adds no public API, policy/config field, runtime
plugin, dependency, sidecar or font bundle. Shared theme values, LegacyPage,
status/UI fonts, Mac, Grid and previews remain unchanged. The existing HWND,
font replacement lifetime, composition deferral, selection, source and engine
history contracts are not rewritten.

Microsoft's contracts distinguish a negative GDI character/em request from a
printer-point conversion; RichEdit character formatting reports twips, whereas
DirectWrite's installed text format reports DIPs.
[CreateFontW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createfontw),
[EM_GETCHARFORMAT](https://learn.microsoft.com/en-us/windows/win32/controls/em-getcharformat),
[IDWriteTextFormat](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwritetextformat).
This correction follows those actual unit contracts rather than a screenshot's
apparent size. No academic renderer replacement or shaping algorithm is needed
for this demonstrated adapter conversion defect.

## Environment and method

- Local Windows `10.0.26200`, process x64; .NET SDK `10.0.400`, test runtime
  `.NET 10.0.11`, Release JIT test host. This is **not** a published AOT binary.
- Source checkpoint before the local production patch: `5c1b8b3cc76400aa1ee8f05e1e82e767691bd35b`.
- Owned hidden STATIC parent and the actual `WindowsRichEditIsland` constructor,
  real child `RICHEDIT50W`, all created/measured/disposed on one dedicated STA
  thread. Tests are a nonparallel collection because native dispatch currently
  has process-wide state. No user window activation, OS theme/input/scale change,
  global configuration change, file I/O or physical key injection occurs.
- Synthetic canonical source `alpha 中文 😀 é` (last accent U+0301), selection
  `[1,3)`, generation 7, binding nonce 11. Palette changes dark -> light using
  the actual `SetTheme`; then an owned `WM_CHAR` replaces selected `lp` with `Z`.
  `FlushPendingText` yields exactly one canonical edit callback `(1,2,"Z")`
  with original generation/nonce. The test observes but never applies that edit
  to its immutable Engine snapshot.
- The baseline was measured **before** changing the production conversion.
  The same native assertions then ran after correcting it; only the expected
  input em changes from 17 to 13. Baseline source copy, structured JSON,
  logs/TRX and hashes are retained in `.cache/native-typography/`.
- Probe distinguishes owned logical HFONT (`GetObjectW`) from mapper-resolved
  face/em (`SelectObject` + `GetTextFaceW`/`GetTextMetricsW`) and from RichEdit
  actual default/bound/newly inserted character size (`EM_GETCHARFORMAT`).
  `WM_GETFONT` returns **0 on both builds**, so the probe reads the retained
  owned `_inputFont` through test-only reflection; it does **not** claim that
  WM_GETFONT confirms font installation. Actual RichEdit formatting is the
  independent applied-size check. The mapper metrics alone are not such proof.
- Native read-only COM ABI queries inspect both source geometry and painter
  `IDWriteTextFormat.GetFontSize`, plus painter `ID2D1RenderTarget.GetDpi`.
  A real geometry `HitTest` supplies a source-position control measurement.
  Reflection/ABI probing is test-only; no telemetry/product instrumentation or
  reflection dependency was added to the product.

## Paired observations

| Actual observation | Baseline A | Corrected B |
| --- | ---: | ---: |
| Source geometry em (`GetFontSize`) | 13 DIP | 13 DIP |
| Source painter em (`GetFontSize`) | 13 DIP | 13 DIP |
| Painter render target DPI (`GetDpi`) | 96 x 96 | 96 x 96 |
| Input effective window/DC DPI | 96 / 96 | 96 / 96 |
| Window/thread awareness enum | 0 / 0 (unaware) | 0 / 0 (unaware) |
| Input DC mapping/window/viewport extents | MM_TEXT / 1,1 / 1,1 | same |
| Owned LOGFONT requested character height | -17 | -13 |
| GDI requested / resolved face | Cascadia Code / Cascadia Code | same |
| GDI cell / internal leading / em | 22 / 5 / 17 | 17 / 4 / 13 |
| RichEdit default applied height | 255 twips | 195 twips |
| Bound selected ASCII applied height | 255 twips | 195 twips |
| Newly inserted selected ASCII applied height | 255 twips | 195 twips |
| Default/bound format face | Cascadia Code | Cascadia Code |
| Newly inserted character format face | Calibri | Calibri |
| RichEdit zoom | 0/0, normal | 0/0, normal |
| Source offset-1 hit X / Y | 7.6171875 / 1.9677734 | exactly unchanged |
| Source offset-1 hit width / height | 7.6171875 / 15.107422 | exactly unchanged |

At this **measured** 96-DPI context, 1440 twips/inch gives 15 twips/client unit;
255 -> 195 corroborates actual applied size 17 -> 13, not merely a changed font
request. The regression derives expected twips from observed DC DPI rather than
assuming that every environment reports 96. RichEdit's inserted character face
is Calibri in this mixed-script scenario: no primary-face identity is asserted
across all characters/shapers. This task fixes size; it neither resolves that
native fallback choice nor claims identical glyph ink/baselines.

Dark -> light preserves the same input HWND/HFONT, exact source text, native
selection `[1,3)`, focus handle, source geometry metrics and Engine snapshot
identity, and emits no source edit. The subsequent native character insertion
produces the expected single source-coordinate callback. Hidden-window focus
preservation is **not** a real foreground/candidate/physical typing test.

## Verification and reproducibility

Executed (all artifact paths inside the repository):

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj -c Release -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release `
  --filter FullyQualifiedName~WindowsCanvasTypographyTests -warnaserror `
  --logger "trx;LogFileName=corrected.trx" `
  --results-directory .cache/native-typography --logger "console;verbosity=detailed"
dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter "FullyQualifiedName~WindowsCanvasTypographyTests|FullyQualifiedName~NativeCsvGridSourceNulTests|FullyQualifiedName~NativePaintTraceTests|FullyQualifiedName~NativeThemeOverrideWindowsTests" `
  --logger "trx;LogFileName=integration.trx" `
  --results-directory .cache/native-typography --logger "console;verbosity=normal"
```

Both Release builds: **0 warnings, 0 errors**. After moving the callback observer
before Bind/theme reload (so the zero-edit assertion genuinely covers them), a
final Release warn-as-error test build and dedicated **13/13** run also passed;
`final-build.log`, `final-typography.log` and `final-typography.trx` retain this
tightened check. Corrected dedicated slice:
**13/13 passed** (one actual native test and twelve rounding/invalid-request
cases). Related native integration slice: **49/49 passed**, including source
NUL safety, paint-trace and same-ID theme override tests. Non-Windows executions
return before Windows-only native operations and are not Windows font evidence.
No whole suite, AOT publish, benchmark or CI rerun was performed for this local
slice. The conversion adds constant-time arithmetic only on font installation;
color-only reload retains its no-font-rebuild path. No new hot edit/paint scan,
per-line storage or full-file mirror is introduced; this is not a measured
end-to-end latency claim.

Retained SHA-256 values (compiled images are the JIT managed assemblies):

| Artifact | SHA-256 |
| --- | --- |
| Baseline `mote.dll` | `B8F4D54A248A48BE57576F09A53FAA549A32CAB37EA45D1C21746F4E63C9BA44` |
| Corrected `mote.dll` | `13477A51863879C86CAAFCCD586194031497D4A8570B9B831183A6A4B86CC5DF` |
| Baseline native JSON | `2B1287E3C05DE5D5051D7496875638A49F9B767354743863AAB0C4E009E8D466` |
| Corrected native JSON | `21332DBF415DC9C6BE9CBFE1ADEE8519DBC15B6D5E00A123C07B53F257E81675` |
| Baseline TRX | `1C28DDA03C102C3B889C0089463CFD6CF83D8262E57BFA4B9C7524109AE72602` |
| Corrected TRX | `C448BB8CDCEAEF187F84443D7A338777DB0BB4520967FCF8D39848E45CD39EBC` |
| Final corrected `mote.dll` (whitespace-normalized source) | `E43C4254C5555B982225FA14BD899E3C33CB35C7EE0061C4F0F92B2854E506A0` |
| Final `Mote.Tests.dll` | `2047AAD921BEFE41C119919BAACCBAB1C9780CF03C0C7580A2C1A9D0E41148DC` |
| Final typography TRX | `46E09C61B50569EA3FE549A911C5959F0A4F6DC6DA79411DDCCD6469A16CBE7F` |
| Final product source file | `05DD183A1E4F6F899329E6F861F185A81C00E63F24BF0232691CE41C117158D0` |
| Final test source file | `821BE674A44C98C6CD5D4F3D2ADEDB97CC467D6A7536B30D31105DB0116ED16F` |
| 49-test integration TRX | `4D3985FE425549D239459B16D6BCBFDA2F75B24BA2546F0532EAF66CF8034A52` |

## Still-open acceptance

This is a causal unit-boundary correction, **not** completion of the proposed
12-state Markdown/JSON/CSV x dark/light x 100%/200% product matrix. Physical
monitor scale is not established by an unaware window reporting 96 DPI. No
200% display, per-monitor transition, real Microsoft Pinyin commit/cancel,
candidate placement, fallback clipping/ink/baseline visual inspection, Narrator,
Save/reopen journey, large-file throughput/tail latency or Windows ARM/AOT run
was executed here. Keep those gates open. Do not generalize the correction to
LegacyPage/UI/RTF/Mac or change default 13 to 14 as part of this patch.
