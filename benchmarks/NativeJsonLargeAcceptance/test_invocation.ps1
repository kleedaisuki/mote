# Verify the exact workflow argument construction using a real native child.
# This is portable argv validation, not AppKit input or Save acceptance.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..', '..'))
$workflow = [IO.Path]::Combine($root, '.github', 'workflows', 'ci.yml')
$source = Get-Content -LiteralPath $workflow -Raw
# Read the production two-line block rather than maintaining a copied builder.
# Replacing only the automatic platform variable permits all four RID branches
# to execute on one host without mutating PowerShell's read-only $IsMacOS.
$pattern = '(?m)^[ \t]*\$witness = @\(\)\r?\n[ \t]*if \(\$IsMacOS\) \{ \$witness = @\(''--mac-save-witness''\) \}'
$matches = [regex]::Matches($source, $pattern)
if ($matches.Count -ne 1) { throw 'Expected one reviewed JSON witness argument construction block.' }
$body = $matches[0].Value.Replace('$IsMacOS', '$macOSBranch')
$builder = [scriptblock]::Create('param([bool]$macOSBranch)' + "`n" + $body + "`nreturn ,`$witness")
foreach ($rid in @('osx-x64', 'osx-arm64', 'win-x64', 'win-arm64')) {
    $mac = $rid.StartsWith('osx-', [StringComparison]::Ordinal)
    $witness = & $builder $mac
    if ($witness -isnot [Array]) { throw 'Witness options must remain an array, including zero/one elements.' }
    $json = & python -c 'import json, sys; print(json.dumps(sys.argv[1:]))' @witness
    if ($LASTEXITCODE -ne 0) { throw 'Native argv control failed.' }
    $actual = ConvertFrom-Json -InputObject $json -NoEnumerate
    $expectedCount = if ($mac) { 1 } else { 0 }
    if ($actual.Count -ne $expectedCount -or
        ($mac -and -not [StringComparer]::Ordinal.Equals([string]$actual[0], '--mac-save-witness'))) {
        throw 'Witness option was split, omitted or leaked into a Windows invocation.'
    }
    Write-Host "JSON witness argv passed: $rid ($expectedCount optional arguments)."
}
