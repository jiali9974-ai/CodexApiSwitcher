param(
    [string]$Executable = ""
)

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $workspace "outputs\cross-platform\win-x64\CodexApiSwitcher.exe"
}
$Executable = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $Executable)) { throw "Cross-platform executable was not found: $Executable" }

$root = Join-Path $PSScriptRoot ("cross-test-root-" + [guid]::NewGuid().ToString("N"))
$snapshot = Join-Path $PSScriptRoot ("cross-ui-" + [guid]::NewGuid().ToString("N") + ".png")
$uiSmokeRoot = Join-Path $PSScriptRoot ("cross-ui-buttons-root-" + [guid]::NewGuid().ToString("N"))
$uiSmokeReport = Join-Path $PSScriptRoot ("cross-ui-buttons-" + [guid]::NewGuid().ToString("N") + ".txt")
$singleRoot = Join-Path $PSScriptRoot ("cross-single-root-" + [guid]::NewGuid().ToString("N"))
$originalStartup = $null
$startupChanged = $false
$singleProcess = $null
$importRoot = $null
$conversationPackage = $null

function New-CodexFixtureRoot([string]$Path, [string]$Model = "gpt-5.5") {
    New-Item -ItemType Directory -Path $Path | Out-Null
    $configPath = Join-Path $Path "config.toml"
    $fixtureText = @"
model_provider = "openai"
model = "$Model"
disable_response_storage = true

[mcp_servers.example]
command = "example"
"@
    [System.IO.File]::WriteAllText($configPath, $fixtureText, [System.Text.UTF8Encoding]::new($false))
    $sessionDirectory = Join-Path $Path "sessions\2026\06\29"
    New-Item -ItemType Directory -Path $sessionDirectory | Out-Null
    $rolloutPath = Join-Path $sessionDirectory "rollout-user-1.jsonl"
    $bodyLine = '{"type":"event_msg","payload":{"type":"user_message","message":"conversation body stays unchanged"}}'
    [System.IO.File]::WriteAllText(
        $rolloutPath,
        '{"type":"session_meta","payload":{"id":"user-1","model_provider":"openai"}}' + "`n" + $bodyLine + "`n",
        [System.Text.UTF8Encoding]::new($false))
    $databasePath = Join-Path $Path "state_5.sqlite"
    python -c "import sqlite3; c=sqlite3.connect(r'$databasePath'); c.execute('create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null)'); c.execute('insert into threads values(?,?,?,?,?,?)',('user-1',r'$rolloutPath','vscode','hello',0,'openai')); c.commit(); c.close()"
    if ($LASTEXITCODE -ne 0) { throw "Unable to create SQLite fixture." }
    return @{
        Config = $configPath
        Rollout = $rolloutPath
        Body = $bodyLine
        Database = $databasePath
    }
}

try {
    $fixture = New-CodexFixtureRoot $root
    $config = $fixture.Config
    $rollout = $fixture.Rollout
    $body = $fixture.Body
    $database = $fixture.Database

    $platform = & $Executable --platform-info --root $root
    if ($LASTEXITCODE -ne 0 -or $platform -notmatch '^OS=Windows; SecretStore=Windows DPAPI; Runtime=') {
        throw "Platform adapter smoke test failed: $platform"
    }
    $hotkey = & $Executable --normalize-hotkey --hotkey "Ctrl+Shift+F9"
    if ($LASTEXITCODE -ne 0 -or $hotkey -ne "Ctrl+Shift+F9") { throw "Hotkey normalization failed: $hotkey" }
    $mouse = & $Executable --normalize-mouse-button --mouse-button "XButton2"
    if ($LASTEXITCODE -ne 0 -or $mouse -ne "XButton2") { throw "Mouse button normalization failed: $mouse" }

    $thirdOutput = & $Executable --switch-third-party --root $root --url "https://relay.example.test/v1/responses" --model "relay-model" --key "cross-platform-test-token"
    if ($LASTEXITCODE -ne 0) { throw "Third-party switch failed: $thirdOutput" }
    $thirdConfig = Get-Content -LiteralPath $config -Raw -Encoding UTF8
    if ($thirdConfig -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Custom provider was not written." }
    if ($thirdConfig -notmatch '(?m)^base_url = "https://relay\.example\.test/v1"\r?$') { throw "Base URL normalization failed." }
    if ($thirdConfig -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Unrelated MCP config was removed." }
    if ($thirdConfig -match 'cross-platform-test-token') { throw "Plaintext API key leaked into config.toml." }

    $helper = Join-Path $root "api-switcher\CodexApiSwitcher.AuthHelper.exe"
    if (-not (Test-Path -LiteralPath $helper)) { throw "Stable credential helper was not installed." }
    $token = & $helper --emit-token --root $root
    if ($LASTEXITCODE -ne 0 -or $token -ne "cross-platform-test-token") { throw "Credential helper round-trip failed: $token" }
    $profileToken = & $Executable --emit-profile-token --root $root --name "relay.example.test"
    if ($LASTEXITCODE -ne 0 -or $profileToken -ne "cross-platform-test-token") { throw "Profile credential round-trip failed." }

    $provider = python -c "import sqlite3; c=sqlite3.connect(r'$database'); row=c.execute('select model_provider, has_user_event from threads').fetchone(); print(str(row[0]) + ',' + str(row[1])); c.close()"
    if ($provider -ne "custom,1") { throw "SQLite provider synchronization failed: $provider" }
    $meta = (Get-Content -LiteralPath $rollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
    if ($meta -ne "custom") { throw "JSONL provider synchronization failed: $meta" }
    if ((Get-Content -LiteralPath $rollout -Encoding UTF8)[1] -ne $body) { throw "Conversation body was modified." }

    $officialOutput = & $Executable --switch-official --root $root --model "official-model"
    if ($LASTEXITCODE -ne 0) { throw "Official switch failed: $officialOutput" }
    $officialConfig = Get-Content -LiteralPath $config -Raw -Encoding UTF8
    if ($officialConfig -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Official provider was not restored." }
    if ($officialConfig -match '(?m)^\[model_providers\.custom(?:\.|\])') { throw "Custom provider sections remained in official mode." }

    $armorStatus = & $Executable --armor-status --root $root
    if ($LASTEXITCODE -ne 0 -or $armorStatus -notmatch "未启用") { throw "Armor status before enabling failed: $armorStatus" }
    $armorOutput = & $Executable --enable-armor --root $root
    if ($LASTEXITCODE -ne 0 -or $armorOutput -notmatch "已启用一键破甲") { throw "Enable armor failed: $armorOutput" }
    $armorConfig = Get-Content -LiteralPath $config -Raw -Encoding UTF8
    if ($armorConfig -notmatch '(?m)^model_instructions_file = "\./gpt5\.5-unrestricted\.md"\r?$') { throw "Armor model_instructions_file was not written." }
    $armorFile = Join-Path $root "gpt5.5-unrestricted.md"
    if (-not (Test-Path -LiteralPath $armorFile)) { throw "Armor instructions file was not written." }
    $enabledArmorStatus = & $Executable --armor-status --root $root
    if ($LASTEXITCODE -ne 0 -or $enabledArmorStatus -notmatch "已启用") { throw "Armor status after enabling failed: $enabledArmorStatus" }
    $restoreArmorOutput = & $Executable --restore-armor --root $root
    if ($LASTEXITCODE -ne 0 -or $restoreArmorOutput -notmatch "已移除") { throw "Restore armor failed: $restoreArmorOutput" }
    $restoredArmorConfig = Get-Content -LiteralPath $config -Raw -Encoding UTF8
    if ($restoredArmorConfig -match '(?m)^model_instructions_file\s*=') { throw "Armor model_instructions_file remained after restore." }
    if (Test-Path -LiteralPath $armorFile) { throw "Armor instructions file remained after restore." }


    $conversationList = & $Executable --list-conversations --root $root --query "hello"
    if ($LASTEXITCODE -ne 0 -or $conversationList -notmatch "user-1") { throw "Conversation list failed: $conversationList" }
    $conversationPackage = Join-Path $PSScriptRoot ("cross-conversations-" + [guid]::NewGuid().ToString("N") + ".casconv.zip")
    $conversationExport = & $Executable --export-conversations --root $root --ids "user-1" --output $conversationPackage
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $conversationPackage)) { throw "Conversation export failed: $conversationExport" }
    $importRoot = Join-Path $PSScriptRoot ("cross-import-root-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path (Join-Path $importRoot "sessions\2026\06\29") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $importRoot "sqlite") -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $importRoot "config.toml"), "model_provider = `"openai`"`nmodel = `"gpt-5.5`"`n", [System.Text.UTF8Encoding]::new($false))
    $importDatabase = Join-Path $importRoot "sqlite\state_5.sqlite"
    $createImportDb = @"
import sqlite3, sys
connection = sqlite3.connect(sys.argv[1])
connection.execute("create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null, title text not null default '', updated_at integer not null default 0, updated_at_ms integer, model text, preview text not null default '')")
connection.commit()
connection.close()
"@
    $createImportDb | python - $importDatabase
    if ($LASTEXITCODE -ne 0) { throw "Unable to create import SQLite fixture." }
    $conversationImport = & $Executable --import-conversations --root $importRoot --input $conversationPackage
    if ($LASTEXITCODE -ne 0) { throw "Conversation import failed: $conversationImport" }
    $importedList = & $Executable --list-conversations --root $importRoot --query "hello"
    if ($LASTEXITCODE -ne 0 -or $importedList -notmatch "user-1") { throw "Imported conversation was not listed: $importedList" }
    $conversationDelete = & $Executable --delete-conversations --root $importRoot --ids "user-1"
    if ($LASTEXITCODE -ne 0) { throw "Conversation delete failed: $conversationDelete" }
    $deletedList = & $Executable --list-conversations --root $importRoot --query "hello"
    if ($deletedList -match "user-1") { throw "Deleted conversation still listed: $deletedList" }

    $originalStartup = & $Executable --show-startup
    $startupChanged = $true
    $enabled = & $Executable --enable-startup
    if ($enabled -ne "Enabled") { throw "Windows startup enable failed: $enabled" }
    $disabled = & $Executable --disable-startup
    if ($disabled -ne "Disabled") { throw "Windows startup disable failed: $disabled" }

    & $Executable --render-ui $snapshot --root $root
    if ($LASTEXITCODE -ne 0) { throw "UI startup/render smoke test failed." }
    if (-not (Test-Path -LiteralPath $snapshot) -or (Get-Item -LiteralPath $snapshot).Length -lt 10000) {
        throw "UI snapshot was not generated correctly."
    }

    New-CodexFixtureRoot $uiSmokeRoot "ui-smoke-initial" | Out-Null
    & $Executable --ui-smoke-test $uiSmokeReport --root $uiSmokeRoot
    if ($LASTEXITCODE -ne 0) {
        $reportText = if (Test-Path -LiteralPath $uiSmokeReport) { Get-Content -LiteralPath $uiSmokeReport -Raw -Encoding UTF8 } else { "<missing report>" }
        throw "UI button smoke test failed: $reportText"
    }
    $uiReportText = Get-Content -LiteralPath $uiSmokeReport -Raw -Encoding UTF8
    foreach ($expected in @("save-profile-button", "delete-profile-button", "switch-third-party-button", "switch-official-button", "reset-config-button", "repair-sidebar-button", "history-manager-button", "armor-reminder-button", "armor-button", "restore-armor-button", "rollback-button", "launch-codex-button", "close-codex-button")) {
        if ($uiReportText -notmatch [regex]::Escape("PASS " + $expected)) { throw "UI button smoke test did not cover: $expected`n$uiReportText" }
    }

    New-CodexFixtureRoot $singleRoot "single-instance-initial" | Out-Null
    $env:CODEX_API_SWITCHER_INSTANCE_SCOPE = "test-" + [guid]::NewGuid().ToString("N")
    $singleProcess = Start-Process -FilePath $Executable -ArgumentList @("--startup-launch", "--root", $singleRoot) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 1800
    if ($singleProcess.HasExited) { throw "Primary single-instance process exited unexpectedly." }
    $before = Get-Process -Id $singleProcess.Id -ErrorAction Stop
    $second = Start-Process -FilePath $Executable -ArgumentList @("--root", $singleRoot) -PassThru -WindowStyle Hidden
    if (-not $second.WaitForExit(6000)) {
        try { Stop-Process -Id $second.Id -Force } catch { }
        throw "Second single-instance launch did not exit after notifying the existing process."
    }
    if ($second.ExitCode -ne 0) { throw "Second single-instance launch returned exit code $($second.ExitCode)." }
    if ($singleProcess.HasExited) { throw "Primary single-instance process exited after second launch." }

    Write-Output "PASS: cross-platform core switch, bundled SQLite, DPAPI compatibility, profile helper, history synchronization, startup adapter, Avalonia render, UI buttons, and single-instance behavior."
}
finally {
    if ($null -ne $singleProcess -and -not $singleProcess.HasExited) {
        try { Stop-Process -Id $singleProcess.Id -Force } catch { }
        try { $singleProcess.WaitForExit(3000) | Out-Null } catch { }
    }
    Remove-Item Env:\CODEX_API_SWITCHER_INSTANCE_SCOPE -ErrorAction SilentlyContinue
    if ($startupChanged -and $null -ne $originalStartup) {
        if ($originalStartup -eq "Enabled") { & $Executable --enable-startup | Out-Null }
        else { & $Executable --disable-startup | Out-Null }
    }
    $workRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    foreach ($candidate in @($root, $uiSmokeRoot, $singleRoot, $importRoot)) {
        $resolvedRoot = [System.IO.Path]::GetFullPath($candidate)
        if ($resolvedRoot.StartsWith($workRoot, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedRoot)) {
            Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
        }
    }
}

Write-Output ("UI_SNAPSHOT=" + $snapshot)
Write-Output ("UI_BUTTON_REPORT=" + $uiSmokeReport)
if ($null -ne $conversationPackage -and (Test-Path -LiteralPath $conversationPackage)) { Remove-Item -LiteralPath $conversationPackage -Force }
