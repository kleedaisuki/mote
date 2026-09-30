# Compare File.Replace outcomes under controlled Windows sharing and attribute states.
# All files are synthetic, unique per run, and confined to the repository's .temp directory.
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This probe requires Windows.' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repoRoot ('.temp/save-replace-probe/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null

# A held stream without delete sharing models a persistent competing opener.
$cases = @(
    @{ Name = 'baseline'; TargetShare = $null; SourceShare = $null; ReadOnly = $false },
    @{ Name = 'target-readwrite-no-delete'; TargetShare = [IO.FileShare]::ReadWrite; SourceShare = $null; ReadOnly = $false },
    @{ Name = 'target-readwrite-delete'; TargetShare = ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete); SourceShare = $null; ReadOnly = $false },
    @{ Name = 'source-read-no-delete'; TargetShare = $null; SourceShare = [IO.FileShare]::Read; ReadOnly = $false },
    @{ Name = 'target-readonly'; TargetShare = $null; SourceShare = $null; ReadOnly = $true }
)

$results = foreach ($case in $cases) {
    $target = Join-Path $root "$($case.Name)-target.txt"
    $source = Join-Path $root "$($case.Name)-source.tmp"
    [IO.File]::WriteAllText($target, 'original')
    [IO.File]::WriteAllText($source, 'replacement')
    $targetHandle = $null
    $sourceHandle = $null
    try {
        if ($case.ReadOnly) { [IO.File]::SetAttributes($target, [IO.FileAttributes]::ReadOnly) }
        if ($null -ne $case.TargetShare) {
            $targetHandle = [IO.FileStream]::new($target, [IO.FileMode]::Open, [IO.FileAccess]::Read, $case.TargetShare)
        }
        if ($null -ne $case.SourceShare) {
            $sourceHandle = [IO.FileStream]::new($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, $case.SourceShare)
        }

        $errorType = $null
        $hresult = $null
        $message = $null
        try { [IO.File]::Replace($source, $target, [NullString]::Value) }
        catch {
            # PowerShell wraps File.Replace exceptions; report the actual IOException HResult.
            $inner = if ($_.Exception.InnerException) { $_.Exception.InnerException } else { $_.Exception }
            $errorType = $inner.GetType().FullName
            $hresult = ('0x{0:X8}' -f ($inner.HResult -band 0xffffffffL))
            $message = $inner.Message
        }
        [pscustomobject]@{
            case = $case.Name
            exception = $errorType
            hresult = $hresult
            message = $message
            target_exists = [IO.File]::Exists($target)
            source_exists = [IO.File]::Exists($source)
            target_text = if ([IO.File]::Exists($target)) { [IO.File]::ReadAllText($target) } else { $null }
        }
    }
    finally {
        if ($null -ne $targetHandle) { $targetHandle.Dispose() }
        if ($null -ne $sourceHandle) { $sourceHandle.Dispose() }
        if ($case.ReadOnly -and [IO.File]::Exists($target)) { [IO.File]::SetAttributes($target, [IO.FileAttributes]::Normal) }
    }
}

$results | ConvertTo-Json -Depth 4
