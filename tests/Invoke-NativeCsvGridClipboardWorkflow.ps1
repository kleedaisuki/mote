# Run the reviewed acceptance only on an explicitly approved disposable GitHub-hosted Windows job.
# The surrounding job must enforce timeout-minutes: 5 and upload the repository-local reports.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Refuse local/self-hosted use before any file, environment or clipboard side effect.
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or !$IsWindows) {
    throw 'Disposable GitHub-hosted Windows runner required'
}
foreach ($name in @('GITHUB_WORKSPACE', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_SHA')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required CI identity missing: $name"
    }
}
# Mutation permission must not be inherited into a build or ordinary suite.
if (![string]::IsNullOrEmpty($env:MOTE_DISPOSABLE_GRID_CLIPBOARD)) { throw 'Unexpected inherited clipboard opt-in' }
$root = (Resolve-Path -LiteralPath $env:GITHUB_WORKSPACE).Path
$scriptRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (![string]::Equals($root.TrimEnd('\'), $scriptRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Script is not in the current GitHub checkout'
}
Push-Location -LiteralPath $root
try {
    # Require a clean exact checkout, including nonignored untracked source/test files.
    $head = (& git rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head -cne $env:GITHUB_SHA) { throw 'Checkout commit does not match CI identity' }
    & git ls-files --error-unmatch -- tests/Mote.Tests/NativeCsvGridClipboardWorkflowTests.cs tests/Invoke-NativeCsvGridClipboardWorkflow.ps1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard harness and invocation must be committed files' }
    $changes = @(& git status --porcelain --untracked-files=all -- src tests)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Source/test checkout is not clean' }
    $source = Join-Path $root 'tests/Mote.Tests/NativeCsvGridClipboardWorkflowTests.cs'
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    # Fixed reviewed byte representations differ ONLY in CRLF versus LF line endings.
    # Any code edit requires renewed independent review; never self-certify current content.
    $reviewedHashes = @(
        '7F23EF2EF31C85D6E4177C2B71590B47B4D977014A51D06979B5606BA32BB855', # CRLF
        '40310CB3B0D8EB1EC68EE29C3BAC8D98EE2F5DB9C40EF55004646C5AB50D0EE8'  # LF
    )
    if ($hash -cnotin $reviewedHashes) { throw 'Clipboard harness differs from reviewed source' }

    $directory = [IO.Path]::GetFullPath((Join-Path $root '.cache/native-grid-clipboard'))
    if (!$directory.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Report path escaped checkout'
    }
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    # Clear only four named artifacts inside the checked repository; no recursive cleanup.
    foreach ($name in @('report.json', 'clipboard.trx', 'approval.txt', 'approval-run.txt')) {
        $path = Join-Path $directory $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    # Build with mutation permission absent; never trust a cached DLL as reviewed evidence.
    dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release -p:TreatWarningsAsErrors=true
    if ($LASTEXITCODE -ne 0) { throw 'Fresh clipboard harness build failed' }
    $runKey = "$($env:GITHUB_RUN_ID):$($env:GITHUB_RUN_ATTEMPT):$($env:GITHUB_SHA)"
    try {
        # Permission markers are created only after all admission checks and the fresh build.
        Set-Content -LiteralPath (Join-Path $directory 'approval.txt') -Value 'disposable-github-hosted-windows-only'
        Set-Content -LiteralPath (Join-Path $directory 'approval-run.txt') -Value $runKey
        $started = [DateTimeOffset]::UtcNow
        $env:MOTE_DISPOSABLE_GRID_CLIPBOARD = '1'
        # This one selected process is the only subprocess that inherits clipboard permission.
        dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build --no-restore `
            --filter 'FullyQualifiedName=Mote.Tests.NativeCsvGridClipboardWorkflowTests.Approved_disposable_runner_publishes_exact_unicode_and_preserves_refused_sentinel' `
            --logger 'console;verbosity=normal' --logger 'trx;LogFileName=clipboard.trx' --results-directory $directory
        if ($LASTEXITCODE -ne 0) { throw 'Clipboard test failed' }
        $path = Join-Path $directory 'report.json'
        if (!(Test-Path -LiteralPath $path)) { throw 'No fresh clipboard report; test may not have executed' }
        $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        # Recent PowerShell versions decode ISO dates automatically; preserve fractional precision in both forms.
        $reportedAt = if ($report.utc -is [DateTime]) {
            [DateTimeOffset]::new($report.utc)
        } else {
            [DateTimeOffset]::Parse([string]$report.utc, [Globalization.CultureInfo]::InvariantCulture)
        }
        # Zero exit can represent opt-out: require exact fresh actual-mode evidence as well.
        if ($report.status -cne 'passed' -or $report.nativeClipboard -ne $true -or
            $report.runKey -cne $runKey -or $report.sourceHash -cne $hash -or
            $reportedAt -lt $started) { throw 'Invalid or stale clipboard report' }
        $expectedCases = @('quoted-crlf', 'empty-final-row', 'missing-refusal', 'explicit-missing-padding', 'nul-refusal', 'over-cap-refusal')
        if (@($report.cases).Count -ne 6 -or @(Compare-Object $expectedCases @($report.cases) -CaseSensitive).Count -ne 0) {
            throw 'Clipboard report lacks the six exact reviewed cases'
        }
        if (!(Test-Path -LiteralPath (Join-Path $directory 'clipboard.trx'))) { throw 'Missing test result artifact' }
    } finally {
        # Failed tests also lose permission before any later step; preserve only report artifacts.
        Remove-Item Env:MOTE_DISPOSABLE_GRID_CLIPBOARD -ErrorAction SilentlyContinue
        foreach ($name in @('approval.txt', 'approval-run.txt')) {
            $path = Join-Path $directory $name
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
} finally {
    Pop-Location
}
