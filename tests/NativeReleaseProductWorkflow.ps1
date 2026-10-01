# Verify six small representative source-edit tasks against an extracted release.
# This suite uses real native surfaces; hosted callbacks/messages are not physical
# keyboard, real Pinyin, screen-reader or input-to-photon evidence. Owned-view
# captures are retained, but their pixels require separate visual review.
[CmdletBinding()]
param(
    [string] $ExecutablePath,
    [string] $OutputDirectory,
    [string] $RuntimeIdentifier,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$originalMarker = 'mote-release-original'
$editedMarker = 'mote-release-edited中'

# Keep reports and retained failures inside the repository without touching user files.
function Assert-ArtifactPath {
    param([string] $Path)
    $full = [IO.Path]::GetFullPath($Path)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    foreach ($area in @('.cache', '.temp')) {
        $prefix = [IO.Path]::GetFullPath((Join-Path $root $area)) + [IO.Path]::DirectorySeparatorChar
        if ($full.StartsWith($prefix, $comparison)) {
            $ancestor = $full
            while ($ancestor -and -not $ancestor.Equals($root, $comparison)) {
                try {
                    $attributes = [IO.File]::GetAttributes($ancestor)
                    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw 'Release artifact ancestors must not be filesystem links.'
                    }
                }
                catch [IO.FileNotFoundException] { }
                catch [IO.DirectoryNotFoundException] { }
                $ancestor = [IO.Path]::GetDirectoryName($ancestor)
            }
            return $full
        }
    }
    throw 'Release artifacts must stay under repository .cache or .temp.'
}

# Fixtures model plausible individual tasks, not observed market/user frequency.
function Get-TaskFixtures {
    return @(
        @{ name='markdown'; extension='md'; text="# Release checklist`n`nOwner: **mote-release-original**`n`n- [ ] Review source`n- [x] Preserve original`n`n## 中文 note`nFind this section, update its owner, then save.`n" },
        @{ name='toml'; extension='toml'; text="# Local tool settings`n[profile]`nname = `"mote-release-original`"`nregion = `"中国`"`nretry = 3`n[display]`npreview = true`n" },
        @{ name='json'; extension='json'; text="{`n  `"profile`": { `"name`": `"mote-release-original`", `"region`": `"中国`" },`n  `"retry`": 3,`n  `"features`": [`"source`", `"preview`"]`n}`n" },
        @{ name='yaml'; extension='yaml'; text="# Small deployment settings`nprofile:`n  name: mote-release-original`n  region: 中国`nretry: 3`nfeatures:`n  - source`n  - preview`n" },
        @{ name='csv'; extension='csv'; text="id,name,note`r`n1,mote-release-original,`"中国, quoted field`"`r`n2,reviewer,`"He said `"`"ready`"`"`"`r`n" },
        @{ name='text'; extension='txt'; text="Reading note — 中文 / café / 😀`n`nOwner: mote-release-original`nThe first paragraph records the original context.`nThe second paragraph is the navigation target.`nEnd of note.`n" }
    )
}

# Exact byte equality protects encoding/newlines and untouched text, not just markers.
function Assert-ExactBytes {
    param([string] $Path, [byte[]] $Expected)
    if (-not [IO.File]::Exists($Path)) { throw 'Expected saved file is absent.' }
    $actual = [IO.File]::ReadAllBytes($Path)
    if ([Convert]::ToHexString($actual) -cne [Convert]::ToHexString($Expected)) {
        throw 'Saved bytes differ from independent expected edit.'
    }
}

# Certify retained native capture dimensions/format only, not semantic pixel quality.
function Assert-NativeCapture {
    param([string] $Path)
    if (-not [IO.File]::Exists($Path)) { throw 'Native product capture is absent.' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 33 -or [Convert]::ToHexString($bytes[0..7]) -cne '89504E470D0A1A0A' -or
        [Text.Encoding]::ASCII.GetString($bytes, 12, 4) -cne 'IHDR') { throw 'Native product capture is not a PNG.' }
    $width = [int64]$bytes[16] * 16777216 + [int64]$bytes[17] * 65536 + [int64]$bytes[18] * 256 + $bytes[19]
    $height = [int64]$bytes[20] * 16777216 + [int64]$bytes[21] * 65536 + [int64]$bytes[22] * 256 + $bytes[23]
    if ($width -lt 100 -or $height -lt 100) { throw 'Native product capture cannot represent the product content view.' }
    return @{path=$Path; width=$width; height=$height; sha256=(Get-FileHash -LiteralPath $Path).Hash; pixels_reviewed=$false}
}

# Capture before exiting; 90 seconds is a hang watchdog, never an experience budget.
function Invoke-Child {
    param([string] $File, [string[]] $Arguments, [string] $moteHome, [string] $Trace,
          [string] $EvidencePrefix, [int] $ExpectedExit = 0)
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $root
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = $moteHome
    $start.Environment['MOTE_TRACE'] = $Trace
    [void]$start.Environment.Remove('MOTE_TRACE_SUBDIR')
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    try {
        $outTask = $child.StandardOutput.ReadToEndAsync()
        $errTask = $child.StandardError.ReadToEndAsync()
        $timedOut = -not $child.WaitForExit(90000)
        if ($timedOut) {
            $child.Kill($true)
            $child.WaitForExit()
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText("$EvidencePrefix.stdout.txt", $stdout, $utf8)
        [IO.File]::WriteAllText("$EvidencePrefix.stderr.txt", $stderr, $utf8)
        if ($timedOut) { throw 'Release child exceeded the hang watchdog; stdout/stderr retained.' }
        if ($child.ExitCode -ne $ExpectedExit) { throw "Release child exited $($child.ExitCode), expected $ExpectedExit. See $EvidencePrefix.stderr.txt." }
        return @{ exit_code=$child.ExitCode; stdout=$stdout; stderr=$stderr }
    }
    finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
}

# Reuse the closed privacy schema and graph oracle instead of weakening telemetry gates.
function Assert-Traces {
    param([string] $moteHome, [string] $Directory, [string[]] $Private, [string] $Prefix,
          [switch] $RequireSave, [switch] $RequireNativeSource,
          [string] $SemanticsPath, [string] $ExpectedKind, [int] $ExpectedSourceUnits,
          [ValidateSet('macos','windows')][string] $SemanticPlatform='macos')
    $traceDir = Join-Path $moteHome $Directory
    $traces = @(Get-ChildItem -LiteralPath $traceDir -Filter '*.jsonl' -File -ErrorAction SilentlyContinue)
    if ($traces.Count -eq 0) { throw 'Opt-in tracing did not retain a trace.' }
    $arguments = @('-B', (Join-Path $PSScriptRoot 'release_trace_oracle.py'))
    foreach ($trace in $traces) { $arguments += @('--trace', $trace.FullName) }
    foreach ($value in $Private) { $arguments += @('--private', $value) }
    if ($RequireSave) { $arguments += '--require-save' }
    if ($RequireNativeSource) { $arguments += '--require-native-source' }
    if ($SemanticsPath) {
        $arguments += @('--semantics', $SemanticsPath, '--expected-kind', $ExpectedKind,
            '--expected-source-units', $ExpectedSourceUnits.ToString([Globalization.CultureInfo]::InvariantCulture),
            '--semantic-platform', $SemanticPlatform)
    }
    return Invoke-Child 'python' $arguments $moteHome '0' $Prefix
}

# PowerShell identifiers are case-insensitive: parameter/local writes must not
# collide with automatic state such as $HOME, even when a self-test skips GUI code.
function Assert-NoReservedVariableWrites {
    param([string] $ScriptPath)
    $syntaxErrors=$null
    $tokens=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($ScriptPath,[ref]$tokens,[ref]$syntaxErrors)
    if ($syntaxErrors.Count) { throw 'Release script has PowerShell syntax errors.' }
    $reserved=@('home','host','pid','profile','pshome','psversiontable','shellid','executioncontext',
        'true','false','null','args','input','error','matches','psitem','_',
        'iswindows','ismacos','islinux','pscommandpath','psscriptroot','myinvocation')
    $writes=$ast.FindAll({param($node)
        $node -is [Management.Automation.Language.ParameterAst] -or
        $node -is [Management.Automation.Language.AssignmentStatementAst] -or
        $node -is [Management.Automation.Language.ForEachStatementAst]
    },$true)
    foreach ($write in $writes) {
        $variables = if ($write -is [Management.Automation.Language.ParameterAst]) { @($write.Name) }
            elseif ($write -is [Management.Automation.Language.ForEachStatementAst]) { @($write.Variable) }
            else { @($write.Left.FindAll({param($node) $node -is [Management.Automation.Language.VariableExpressionAst]},$true)) }
        foreach ($variable in $variables) {
            if ($reserved -contains $variable.VariablePath.UserPath) {
                throw "Release script writes reserved PowerShell variable: $($variable.VariablePath.UserPath)."
            }
        }
    }
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ".temp/release-acceptance/$([guid]::NewGuid().ToString('N'))" }
$output = Assert-ArtifactPath $OutputDirectory
if ([IO.Directory]::Exists($output) -or [IO.File]::Exists($output)) { throw 'Release evidence directory must be new; existing evidence is not overwritten.' }
if (-not $RuntimeIdentifier -and -not $SelfTest) {
    $platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'unsupported' }
    $RuntimeIdentifier = "$platform-$([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())"
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$result = [ordered]@{
    status='running'; runtime_identifier=$RuntimeIdentifier; executable_sha256=$null
    fixtures=@(); cli=@(); configuration=@(); error=$null
    coverage='native product callbacks/messages and new GUI processes; not physical input, real Pinyin, reader or pixel evidence'
}
try {
    $fixtures = @(Get-TaskFixtures)
    if ($fixtures.Count -ne 6) { throw 'Release fixture set must contain six formats.' }
    if ($SelfTest) {
        Assert-NoReservedVariableWrites $PSCommandPath
        foreach ($control in @(
            @{name='home';source='function Collision { param([string] $hOmE) }'},
            @{name='pid';source='$pId=1'},
            @{name='profile';source='$PrOfIlE="not a user profile"'}
        )) {
            $reservedFixture=Join-Path $output "reserved-$($control.name).ps1"
            [IO.File]::WriteAllText($reservedFixture, $control.source, $utf8)
            $caught=$false
            try { Assert-NoReservedVariableWrites $reservedFixture } catch { $caught=$true }
            if (-not $caught) { throw 'Reserved-variable oracle accepted case-insensitive automatic writes.' }
        }

        # Run the real child/trace helpers on a harmless console fixture, not a
        # GUI stand-in. This catches parameter binding/environment bugs that a
        # fixture-only self-test cannot see (the first hosted run exposed $HOME).
        $consoleFixture=Join-Path $output 'console-child.ps1'
        $consoleSource=@'
param([int] $ExitCode)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($ExitCode -ne 0) { [Console]::Error.WriteLine('synthetic expected error'); exit $ExitCode }
$config=[IO.File]::ReadAllBytes((Join-Path $env:MOTE_HOME 'config.toml'))
$traceDirectory=Join-Path $env:MOTE_HOME 'custom-traces'
New-Item -ItemType Directory -Path $traceDirectory | Out-Null
$record=@{schema_version=1;utc_time='2026-10-02T00:00:00Z';session_id=('1'*32);trace_id=('2'*32);
    span_id='0000000000000001';parent_span_id=$null;operation='mote.session';duration_us=0;status='success';attributes=@{}}
[IO.File]::WriteAllText((Join-Path $traceDirectory 'console.jsonl'),($record|ConvertTo-Json -Compress)+"`n",[Text.UTF8Encoding]::new($false))
@{mote_home=$env:MOTE_HOME;trace=$env:MOTE_TRACE;config_sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($config))}|ConvertTo-Json -Compress
'@
        [IO.File]::WriteAllText($consoleFixture,$consoleSource,$utf8)
        $consoleHome=Join-Path $output 'console-home'
        New-Item -ItemType Directory -Path $consoleHome | Out-Null
        $consoleConfig=$utf8.GetBytes("[paths]`ntraces = `"custom-traces`"`n")
        [IO.File]::WriteAllBytes((Join-Path $consoleHome 'config.toml'),$consoleConfig)
        $pwshName=if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
        $console=Invoke-Child (Join-Path $PSHOME $pwshName) @('-NoProfile','-File',$consoleFixture,'-ExitCode','0') $consoleHome '1' (Join-Path $output 'console-success')
        $observed=$console.stdout|ConvertFrom-Json -AsHashtable
        if ($observed.mote_home -cne $consoleHome -or $observed.trace -cne '1' -or
            $observed.config_sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($consoleConfig))) {
            throw 'Child helper did not preserve isolated environment/config evidence.'
        }
        [void](Assert-Traces $consoleHome 'custom-traces' @($consoleHome) (Join-Path $output 'console-trace'))
        $consoleError=Invoke-Child (Join-Path $PSHOME $pwshName) @('-NoProfile','-File',$consoleFixture,'-ExitCode','2') $consoleHome '0' (Join-Path $output 'console-error') 2
        if ($consoleError.stderr.Trim() -cne 'synthetic expected error') { throw 'Child helper lost expected stderr.' }
        Assert-ExactBytes (Join-Path $consoleHome 'config.toml') $consoleConfig
        $result['console_helpers']=@{success_exit=0;expected_error_exit=2;isolated_environment=$true;config_unchanged=$true;trace_oracle=$true;reserved_write_negative_control=$true}
        foreach ($fixture in $fixtures) {
            if (($fixture.text.Split($originalMarker).Length - 1) -ne 1) { throw 'Each fixture needs exactly one edit target.' }
            $path = Join-Path $output "$($fixture.name).$($fixture.extension)"
            $bytes = $utf8.GetBytes($fixture.text.Replace($originalMarker, $editedMarker))
            [IO.File]::WriteAllBytes($path, $bytes)
            Assert-ExactBytes $path $bytes
            $caught = $false
            try { Assert-ExactBytes $path $utf8.GetBytes($fixture.text) } catch { $caught = $true }
            if (-not $caught) { throw 'Byte oracle accepted the unedited original.' }
            $result.fixtures += @{ name=$fixture.name; bytes=$bytes.Length; exact_byte_negative_control=$true }
        }
        $caught = $false
        try { [void](Assert-ArtifactPath (Join-Path $root 'outside-evidence.json')) } catch { $caught=$true }
        if (-not $caught) { throw 'Artifact guard accepted an outside path.' }
        $target = Join-Path $output 'link-target'
        $link = Join-Path $output 'linked-area'
        New-Item -ItemType Directory -Path $target | Out-Null
        $linkCreated = $false
        try {
            $itemType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
            New-Item -ItemType $itemType -Path $link -Target $target -ErrorAction Stop | Out-Null
            $linkCreated = $true
        }
        catch { $result['link_guard'] = 'not-exercised-environment-cannot-create-link' }
        if ($linkCreated) {
            $caught=$false
            try { [void](Assert-ArtifactPath (Join-Path $link 'new-evidence.json')) } catch { $caught=$true }
            # Directory.Delete removes the link itself, not the link target tree.
            [IO.Directory]::Delete($link)
            if (-not $caught) { throw 'Artifact guard accepted a filesystem link ancestor.' }
            $result['link_guard'] = 'passed'
        }
        $result.status='self-test-passed'
    }
    else {
        if (-not ($IsWindows -or $IsMacOS)) { throw 'Release workflow requires Windows or macOS.' }
        if (-not $ExecutablePath) { throw 'ExecutablePath is required.' }
        $exe = [IO.Path]::GetFullPath($ExecutablePath)
        if (-not [IO.File]::Exists($exe)) { throw 'Release executable is absent.' }
        $result.executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        foreach ($case in @(
            @{name='runtime'; arguments=@('--check-runtime'); exit=0; marker='mote-native-ready'},
            @{name='help'; arguments=@('--help'); exit=0; marker='Usage: mote'},
            @{name='too-many-paths'; arguments=@('first.txt','second.txt'); exit=2; marker=''},
            @{name='conflicting-profile'; arguments=@('--native-source','--legacy-page'); exit=2; marker=''},
            @{name='unknown-option'; arguments=@('--release-unknown-option'); exit=2; marker=''}
        )) {
            $child = Invoke-Child $exe $case.arguments (Join-Path $output 'cli-home') '0' (Join-Path $output $case.name) $case.exit
            if ($case.marker -and -not $child.stdout.Contains($case.marker, [StringComparison]::Ordinal)) { throw 'CLI omitted its required readiness/help marker.' }
            if ($case.exit -ne 0 -and [string]::IsNullOrWhiteSpace($child.stderr)) { throw 'Invalid CLI invocation omitted its error.' }
            $result.cli += @{name=$case.name; exit_code=$child.exit_code}
        }
        foreach ($case in @(
            @{name='default-off'; config=$null; trace='0'; traceDirectory=$null},
            @{name='configured-light'; config="[appearance]`ntheme = `"mote-light`"`n[paths]`ncache = `"custom-cache`"`ndata = `"custom-data`"`ntraces = `"custom-traces`"`n[telemetry]`nenabled = true`n"; trace='0'; traceDirectory='custom-traces'},
            @{name='environment-optin'; config="[appearance]`ntheme = `"mote-dark`"`n[telemetry]`nenabled = false`n"; trace='1'; traceDirectory='traces'}
        )) {
            $moteHome = Join-Path $output "$($case.name)-home"
            if ($case.config) {
                New-Item -ItemType Directory -Force -Path $moteHome | Out-Null
                [IO.File]::WriteAllText((Join-Path $moteHome 'config.toml'), $case.config, $utf8)
            }
            $child = Invoke-Child $exe @('--smoke-gui') $moteHome $case.trace (Join-Path $output $case.name)
            if ($child.stdout.Trim() -cne 'mote-native-gui-ready') { throw 'Configured product GUI smoke omitted readiness marker.' }
            if ($case.traceDirectory) {
                [void](Assert-Traces $moteHome $case.traceDirectory @($moteHome) (Join-Path $output "$($case.name)-trace") -RequireNativeSource)
            }
            elseif ([IO.Directory]::Exists($moteHome)) { throw 'Default-off product smoke created mutable state.' }
            if ($case.config) { Assert-ExactBytes (Join-Path $moteHome 'config.toml') $utf8.GetBytes($case.config) }
            $result.configuration += @{name=$case.name; exit_code=$child.exit_code; trace_directory=$case.traceDirectory;
                launch_route='bare-default'; native_source_surface_witness=[bool]$case.traceDirectory; theme_rendering_verified=$false}
        }
        foreach ($fixture in $fixtures) {
            $area = Join-Path $output $fixture.name
            New-Item -ItemType Directory -Force -Path $area | Out-Null
            $inputPath = Join-Path $area "original.$($fixture.extension)"
            $outputPath = Join-Path $area "edited.$($fixture.extension)"
            if ([IO.File]::Exists($inputPath) -or [IO.File]::Exists($outputPath)) { throw 'Task paths already exist; use a fresh evidence directory.' }
            $original = $utf8.GetBytes($fixture.text)
            $expected = $utf8.GetBytes($fixture.text.Replace($originalMarker, $editedMarker))
            [IO.File]::WriteAllBytes($inputPath, $original)
            $moteHome = Join-Path $area 'home'
            if ($IsWindows) {
                $arguments = @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Invoke-NativeWindowsReleaseProduct.ps1'),
                    '-ExecutablePath',$exe,'-InputPath',$inputPath,'-OutputPath',$outputPath,'-EvidenceDirectory',$area)
                [void](Invoke-Child (Join-Path $PSHOME 'pwsh.exe') $arguments $moteHome '1' (Join-Path $area 'workflow'))
            }
            else {
                $child = Invoke-Child $exe @('--check-native-mac-release-workflow',$inputPath,$outputPath) $moteHome '1' (Join-Path $area 'workflow')
                if ($child.stdout.Trim() -cne 'mote-native-mac-release-workflow-ready') { throw 'AppKit release workflow omitted completion marker.' }
                $reopen = Invoke-Child $exe @('--check-native-mac-release-reopen',$outputPath) (Join-Path $area 'reopen-home') '0' (Join-Path $area 'reopen')
                if ($reopen.stdout.Trim() -cne 'mote-native-mac-release-reopen-ready') { throw 'Fresh AppKit GUI reopen omitted completion marker.' }
            }
            Assert-ExactBytes $inputPath $original
            Assert-ExactBytes $outputPath $expected
            $capture = Assert-NativeCapture (Join-Path $area 'native-product.png')
            $kind=@{markdown='Markdown';toml='Toml';json='Json';yaml='Yaml';csv='Csv';text='PlainText'}[$fixture.name]
            $units=$fixture.text.Replace($originalMarker,$editedMarker).Length
            $traceEvidence=Assert-Traces $moteHome 'traces' @($originalMarker,$editedMarker,$inputPath,$outputPath,$moteHome) `
                (Join-Path $area 'trace-oracle') -RequireSave -SemanticsPath (Join-Path $area 'native-product-semantics.json') `
                -ExpectedKind $kind -ExpectedSourceUnits $units -SemanticPlatform $(if ($IsWindows) {'windows'} else {'macos'})
            $semanticReport=($traceEvidence.stdout|ConvertFrom-Json -AsHashtable).semantics
            if (-not $semanticReport -or $semanticReport.status -cne 'passed') { throw 'Trace oracle omitted checked final-view evidence.' }
            $result.fixtures += @{name=$fixture.name; original_bytes=$original.Length; saved_bytes=$expected.Length;
                original_sha256=(Get-FileHash -LiteralPath $inputPath).Hash; saved_sha256=(Get-FileHash -LiteralPath $outputPath).Hash;
                exact_bytes=$true; original_protected=$true; fresh_gui_process_reopen=$true; fixture_origin='self-created representative task, not market evidence'}
            $result.fixtures[-1]['native_capture'] = $capture
            $result.fixtures[-1]['final_semantics'] = $semanticReport
        }
        if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -cne $result.executable_sha256) { throw 'Release executable changed during acceptance.' }
        $result.status='passed'
    }
}
catch {
    $result.status='failed'
    $result.error=$_.Exception.Message
    throw
}
finally {
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'release-acceptance.json') -Encoding utf8
    Write-Output ($result | ConvertTo-Json -Depth 8 -Compress)
}
