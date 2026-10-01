# Assemble a noncompliant, multi-file macOS diagnostic bundle for GUI smoke tests.
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version = '0.1.0',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9-]*(\.[A-Za-z0-9-]+)+$')][string] $BundleIdentifier = 'org.mote.editor'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'macOS app bundles must be assembled on macOS to preserve executable permissions.' }
. (Join-Path $PSScriptRoot 'Pack-Common.ps1')

$paths = Resolve-PackageInput $PublishDirectory $OutputDirectory $RuntimeIdentifier 'osx-'
$sourceExecutable = Join-Path $paths.Publish 'Mote.Desktop'
$nativeBackend = Join-Path $paths.Publish 'libAvaloniaNative.dylib'
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "Native AOT executable missing: $sourceExecutable"
}
if (-not (Test-Path -LiteralPath $nativeBackend -PathType Leaf)) {
    throw "Avalonia macOS native backend missing: $nativeBackend"
}

$app = Join-Path $paths.Output 'mote.app'
$archive = Join-Path $paths.Output "mote-$Version-$RuntimeIdentifier-unsigned.zip"
$manifest = Join-Path $paths.Output "mote-$Version-$RuntimeIdentifier-unsigned-manifest.json"
if ((Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $manifest)) {
    throw "Output already exists; choose a fresh output directory: $app"
}

$contents = Join-Path $app 'Contents'
$macOS = Join-Path $contents 'MacOS'
$frameworks = Join-Path $contents 'Frameworks'
$resources = Join-Path $contents 'Resources'
foreach ($directory in @($macOS, $frameworks, $resources)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-PublishPayload $paths.Publish $macOS

# Keep the app-host-relative lookup path while placing native libraries in Apple's
# conventional Frameworks directory. Relative symlinks survive ditto and relocation.
foreach ($library in Get-ChildItem -LiteralPath $macOS -Filter '*.dylib' -File) {
    Move-Item -LiteralPath $library.FullName -Destination (Join-Path $frameworks $library.Name)
    & ln -s "../Frameworks/$($library.Name)" (Join-Path $macOS $library.Name)
    if ($LASTEXITCODE -ne 0) { throw "Could not link native library: $($library.Name)" }
}

$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleExecutable</key><string>Mote.Desktop</string>
  <key>CFBundleIdentifier</key><string>$BundleIdentifier</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>mote</string>
  <key>CFBundleDisplayName</key><string>mote</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$Version</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>LSMinimumSystemVersion</key><string>14.0.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Structured text</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>public.plain-text</string>
        <string>net.daringfireball.markdown</string>
        <string>public.json</string>
        <string>public.yaml</string>
        <string>$BundleIdentifier.yaml</string>
        <string>public.comma-separated-values-text</string>
        <string>$BundleIdentifier.toml</string>
      </array>
    </dict>
  </array>
  <key>UTImportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key><string>$BundleIdentifier.toml</string>
      <key>UTTypeDescription</key><string>TOML document</string>
      <key>UTTypeConformsTo</key><array><string>public.plain-text</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>toml</string></array></dict>
    </dict>
    <dict>
      <key>UTTypeIdentifier</key><string>net.daringfireball.markdown</string>
      <key>UTTypeDescription</key><string>Markdown document</string>
      <key>UTTypeConformsTo</key><array><string>public.plain-text</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>md</string><string>markdown</string></array></dict>
    </dict>
    <dict>
      <key>UTTypeIdentifier</key><string>$BundleIdentifier.yaml</string>
      <key>UTTypeDescription</key><string>YAML document</string>
      <key>UTTypeConformsTo</key><array><string>public.plain-text</string></array>
      <key>UTTypeTagSpecification</key>
      <dict><key>public.filename-extension</key><array><string>yaml</string><string>yml</string></array></dict>
    </dict>
  </array>
</dict>
</plist>
"@
[IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist + "`n", [Text.UTF8Encoding]::new($false))

& chmod +x (Join-Path $macOS 'Mote.Desktop')
if ($LASTEXITCODE -ne 0) { throw 'Could not make the app executable.' }
& plutil -lint (Join-Path $contents 'Info.plist')
if ($LASTEXITCODE -ne 0) { throw 'Info.plist is invalid.' }

Write-PackageManifest $app $manifest $RuntimeIdentifier $Version 'Contents/MacOS/Mote.Desktop' 'macos-app'
& ditto -c -k --sequesterRsrc --keepParent $app $archive
if ($LASTEXITCODE -ne 0) { throw 'Could not create the macOS app archive.' }

Write-Host "App bundle: $app"
Write-Host "Archive: $archive"
Write-Host "Manifest: $manifest"
Write-Host 'NONCOMPLIANT: unsigned .app diagnostic artifact, not a mote release.'
