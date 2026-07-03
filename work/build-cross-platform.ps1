param(
    [string[]]$Runtime = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace "src\CodexApiSwitcher\CodexApiSwitcher.csproj"
$outputRoot = Join-Path $workspace "outputs\cross-platform"
$Runtime = @($Runtime | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$dotnetCandidates = @(
    (Join-Path $env:LOCALAPPDATA "codex-tools\dotnet10\dotnet.exe"),
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
    "dotnet"
)
$dotnet = $dotnetCandidates | Where-Object {
    if ($_ -eq "dotnet") { return $null -ne (Get-Command dotnet -ErrorAction SilentlyContinue) }
    return Test-Path -LiteralPath $_
} | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($dotnet)) {
    throw ".NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}

function New-MacAppBundle([string]$Destination, [string]$Workspace) {
    $bundle = Join-Path $Destination "Codex API Switcher.app"
    if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Recurse -Force }
    $contents = Join-Path $bundle "Contents"
    $macos = Join-Path $contents "MacOS"
    $resources = Join-Path $contents "Resources"
    New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
    Copy-Item -LiteralPath (Join-Path $Destination "CodexApiSwitcher") -Destination (Join-Path $macos "CodexApiSwitcher") -Force
    Copy-Item -LiteralPath (Join-Path $Workspace "outputs\cas-logo.ico") -Destination (Join-Path $resources "cas-logo.ico") -Force
    Copy-Item -LiteralPath (Join-Path $Workspace "outputs\cas-logo.icns") -Destination (Join-Path $resources "cas-logo.icns") -Force

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Codex API Switcher</string>
  <key>CFBundleDisplayName</key><string>Codex API Switcher</string>
  <key>CFBundleIdentifier</key><string>com.codex-api-switcher.desktop</string>
  <key>CFBundleExecutable</key><string>CodexApiSwitcher</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>2.1.0</string>
  <key>CFBundleVersion</key><string>2.1.0</string>
  <key>CFBundleIconFile</key><string>cas-logo.icns</string>
  <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
  <key>LSMultipleInstancesProhibited</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
</dict>
</plist>
"@
    [System.IO.File]::WriteAllText((Join-Path $contents "Info.plist"), $plist, [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-ZipExternalAttributes([int]$UnixMode) {
    $raw = [uint32]($UnixMode * 65536)
    return [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes($raw), 0)
}

function Get-RelativeZipPath([string]$BasePath, [string]$Path) {
    $baseUri = [Uri](([System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar))
    $pathUri = [Uri]([System.IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function New-ZipWithUnixModes([string]$SourceDirectory, [string]$ZipPath, [string]$EntryRootName) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $sourceFull = [System.IO.Path]::GetFullPath($SourceDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $rootEntry = $zip.CreateEntry($EntryRootName + "/", [System.IO.Compression.CompressionLevel]::NoCompression)
        $rootEntry.ExternalAttributes = ConvertTo-ZipExternalAttributes 16877
        foreach ($directory in [System.IO.Directory]::EnumerateDirectories($sourceFull, "*", [System.IO.SearchOption]::AllDirectories)) {
            $relative = Get-RelativeZipPath $sourceFull $directory
            $entry = $zip.CreateEntry(($EntryRootName + "/" + $relative + "/"), [System.IO.Compression.CompressionLevel]::NoCompression)
            $entry.ExternalAttributes = ConvertTo-ZipExternalAttributes 16877
        }
        foreach ($file in [System.IO.Directory]::EnumerateFiles($sourceFull, "*", [System.IO.SearchOption]::AllDirectories)) {
            $relative = Get-RelativeZipPath $sourceFull $file
            $entryName = $EntryRootName + "/" + $relative
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $isExecutable = $relative -eq "Contents/MacOS/CodexApiSwitcher"
            $mode = if ($isExecutable) { 33261 } else { 33188 }
            $entry.ExternalAttributes = ConvertTo-ZipExternalAttributes $mode
        }
    }
    finally {
        $zip.Dispose()
    }
}

function New-MacDmg([string]$AppBundle, [string]$DmgPath, [string]$ScratchRoot) {
    if (-not $IsMacOS) { return }
    $hdiutil = Get-Command hdiutil -ErrorAction SilentlyContinue
    if ($null -eq $hdiutil) { return }
    if (Test-Path -LiteralPath $ScratchRoot) { Remove-Item -LiteralPath $ScratchRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ScratchRoot | Out-Null
    Copy-Item -LiteralPath $AppBundle -Destination (Join-Path $ScratchRoot "Codex API Switcher.app") -Recurse -Force
    New-Item -ItemType SymbolicLink -Path (Join-Path $ScratchRoot "Applications") -Target "/Applications" | Out-Null
    if (Test-Path -LiteralPath $DmgPath) { Remove-Item -LiteralPath $DmgPath -Force }
    & $hdiutil.Source create -volname "Codex API Switcher" -srcfolder $ScratchRoot -ov -format UDZO $DmgPath
    if ($LASTEXITCODE -ne 0) { throw "hdiutil create failed for $DmgPath." }
}

if (-not $SkipRestore) {
    & $dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
}

foreach ($rid in $Runtime) {
    $destination = Join-Path $outputRoot $rid
    & $dotnet publish $project `
        --configuration Release `
        --runtime $rid `
        --self-contained true `
        --output $destination `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid." }
    Get-ChildItem -LiteralPath $destination -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    if ($rid -like "osx-*") {
        New-MacAppBundle $destination $workspace
    }
}

$workspaceFull = [System.IO.Path]::GetFullPath($workspace).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
function Assert-InWorkspace([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($workspaceFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to update top-level artifact outside workspace: $resolved"
    }
}

if ($Runtime -contains "win-x64") {
    $source = Join-Path $outputRoot "win-x64\CodexApiSwitcher.exe"
    $target = Join-Path $workspace "CodexApiSwitcher-win-x64.exe"
    Assert-InWorkspace $target
    Copy-Item -LiteralPath $source -Destination $target -Force
}

foreach ($rid in @("linux-x64", "linux-arm64")) {
    if ($Runtime -notcontains $rid) { continue }
    $source = Join-Path $outputRoot "$rid\CodexApiSwitcher"
    $target = Join-Path $workspace ("CodexApiSwitcher-" + $rid)
    Assert-InWorkspace $target
    Copy-Item -LiteralPath $source -Destination $target -Force
}

foreach ($item in @(
    @{ Rid = "osx-arm64"; Name = "CodexApiSwitcher-macos-arm64.app"; Zip = "CodexApiSwitcher-macos-arm64.zip"; Dmg = "CodexApiSwitcher-macos-arm64.dmg" },
    @{ Rid = "osx-x64"; Name = "CodexApiSwitcher-macos-x64.app"; Zip = "CodexApiSwitcher-macos-x64.zip"; Dmg = "CodexApiSwitcher-macos-x64.dmg" }
)) {
    if ($Runtime -notcontains $item.Rid) { continue }
    $source = Join-Path $outputRoot ($item.Rid + "\Codex API Switcher.app")
    $target = Join-Path $workspace $item.Name
    $zipTarget = Join-Path $workspace $item.Zip
    Assert-InWorkspace $target
    Assert-InWorkspace $zipTarget
    $dmgTarget = Join-Path $workspace $item.Dmg
    Assert-InWorkspace $dmgTarget
    if (Test-Path -LiteralPath $target) {
        $resolvedTarget = [System.IO.Path]::GetFullPath($target)
        if (-not $resolvedTarget.StartsWith($workspaceFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove app bundle outside workspace: $resolvedTarget"
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
    New-ZipWithUnixModes $target $zipTarget $item.Name
    New-MacDmg $target $dmgTarget (Join-Path $outputRoot ($item.Rid + "\dmg-root"))
}

Get-ChildItem -LiteralPath $outputRoot -Recurse -File |
    Select-Object FullName, Length, LastWriteTime
