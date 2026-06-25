$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $workspace "outputs\CodexApiSwitcher.exe"
$icon = Join-Path $workspace "outputs\cas-logo.ico"
$root = Join-Path $PSScriptRoot ("exe-test-root-" + [guid]::NewGuid().ToString("N"))
$config = Join-Path $root "config.toml"

if (!(Test-Path -LiteralPath $icon) -or (Get-Item -LiteralPath $icon).Length -le 0) {
    throw "CAS icon was not generated."
}

$launchPlan = & $exe --codex-launch-plan
if ($LASTEXITCODE -ne 0 -or $launchPlan -notmatch '^Codex launch target: ') {
    throw "Codex launch target resolution failed: $launchPlan"
}

$closePlan = & $exe --close-codex --dry-run
if ($LASTEXITCODE -ne 0 -or $closePlan -notmatch '^Would close \d+ Codex process') {
    throw "Codex close dry-run failed: $closePlan"
}

$originalStartup = & $exe --show-startup
if ($LASTEXITCODE -ne 0 -or ($originalStartup -ne "Enabled" -and $originalStartup -ne "Disabled")) {
    throw "Startup state query failed: $originalStartup"
}
try {
    $enabledStartup = & $exe --enable-startup
    if ($LASTEXITCODE -ne 0 -or $enabledStartup -ne "Enabled") {
        throw "Failed to enable startup: $enabledStartup"
    }
    $shownEnabledStartup = & $exe --show-startup
    if ($LASTEXITCODE -ne 0 -or $shownEnabledStartup -ne "Enabled") {
        throw "Enabled startup state was not preserved: $shownEnabledStartup"
    }
    $disabledStartup = & $exe --disable-startup
    if ($LASTEXITCODE -ne 0 -or $disabledStartup -ne "Disabled") {
        throw "Failed to disable startup: $disabledStartup"
    }
    $shownDisabledStartup = & $exe --show-startup
    if ($LASTEXITCODE -ne 0 -or $shownDisabledStartup -ne "Disabled") {
        throw "Disabled startup state was not preserved: $shownDisabledStartup"
    }
} finally {
    if ($originalStartup -eq "Enabled") {
        & $exe --enable-startup | Out-Null
    } elseif ($originalStartup -eq "Disabled") {
        & $exe --disable-startup | Out-Null
    }
}

$hotkeyPlan = & $exe --normalize-hotkey --hotkey "Ctrl+Alt+C"
if ($LASTEXITCODE -ne 0 -or $hotkeyPlan -ne "Ctrl+Alt+C") {
    throw "Hotkey normalization failed: $hotkeyPlan"
}

$invalidHotkeySucceeded = $false
try {
    & $exe --normalize-hotkey --hotkey "C" 2>$null
    $invalidHotkeySucceeded = $LASTEXITCODE -eq 0
} catch {
    $invalidHotkeySucceeded = $false
}
if ($invalidHotkeySucceeded) {
    throw "Hotkey normalization accepted a shortcut without modifiers."
}

New-Item -ItemType Directory -Path $root | Out-Null

$fixture = @'
model_provider = "custom"
model = "gpt-5.5"
disable_response_storage = true

[windows]
sandbox = "unelevated"

[mcp_servers.example]
command = "example.exe"

[model_providers.custom]
name = "old"
wire_api = "responses"
requires_openai_auth = true
base_url = "https://old.example"
'@
[System.IO.File]::WriteAllText($config, $fixture, [System.Text.UTF8Encoding]::new($false))

$savedHotkey = & $exe --root $root --save-hotkey --hotkey "Ctrl+Shift+F9"
if ($LASTEXITCODE -ne 0) { throw "Failed to save hotkey setting." }
if ($savedHotkey -ne "Ctrl+Shift+F9") { throw "Saved hotkey command returned unexpected value: $savedHotkey" }
$storedHotkey = & $exe --root $root --show-hotkey
if ($LASTEXITCODE -ne 0 -or $storedHotkey -ne "Ctrl+Shift+F9") {
    throw "Stored hotkey was not preserved: $storedHotkey"
}

$mouseButtonPlan = & $exe --normalize-mouse-button --mouse-button "XButton1"
if ($LASTEXITCODE -ne 0 -or $mouseButtonPlan -ne "XButton1") {
    throw "Mouse button normalization failed: $mouseButtonPlan"
}
$savedMouseButton = & $exe --root $root --save-mouse-button --mouse-button "XButton2"
if ($LASTEXITCODE -ne 0 -or $savedMouseButton -ne "XButton2") {
    throw "Failed to save mouse button setting: $savedMouseButton"
}
$storedMouseButton = & $exe --root $root --show-mouse-button
if ($LASTEXITCODE -ne 0 -or $storedMouseButton -ne "XButton2") {
    throw "Stored mouse button was not preserved: $storedMouseButton"
}

$statePath = Join-Path $root "state_5.sqlite"
$activeStatePath = Join-Path $root "sqlite\state_5.sqlite"
$sessionDir = Join-Path $root "sessions\2026\06\11"
New-Item -ItemType Directory -Path $sessionDir | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $activeStatePath) | Out-Null
$userRollout = Join-Path $sessionDir "rollout-user-1.jsonl"
$cliRollout = Join-Path $sessionDir "rollout-cli-1.jsonl"
$staleSessionRollout = Join-Path $sessionDir "rollout-archived-1.jsonl"
$missingRollout = Join-Path $sessionDir "rollout-missing-1.jsonl"
$archivedDir = Join-Path $root "archived_sessions"
$archivedRollout = Join-Path $archivedDir "rollout-archived-1.jsonl"
$bodyLine = '{"type":"event_msg","payload":{"type":"user_message","message":"正文必须保持不变"}}'
[System.IO.File]::WriteAllText($userRollout, '{"type":"session_meta","payload":{"id":"user-1","model_provider":"openai","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($cliRollout, '{"type":"session_meta","payload":{"id":"cli-1","model_provider":"OpenAI","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $archivedDir | Out-Null
[System.IO.File]::WriteAllText($archivedRollout, '{"type":"session_meta","payload":{"id":"archived-1","model_provider":"openai","cwd":"D:\\project"}}' + "`n" + $bodyLine + "`n", [System.Text.UTF8Encoding]::new($false))
python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('create table threads(id text primary key, rollout_path text not null, source text not null, first_user_message text not null, has_user_event integer not null default 0, model_provider text not null)'); c.executemany('insert into threads values(?,?,?,?,?,?)',[('user-1',r'\\?\$userRollout','vscode','hello',0,'openai'),('cli-1',r'$cliRollout','cli','hello',0,'OpenAI'),('archived-1',r'$staleSessionRollout','vscode','hello',0,'openai'),('missing-1',r'$missingRollout','vscode','hello',0,'openai'),('agent-1','', '{\""subagent\"":{}}','worker',0,'openai')]); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create history synchronization fixture." }
Copy-Item -LiteralPath $statePath -Destination $activeStatePath
python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('update threads set rollout_path=? where id=?',(r'$archivedRollout','archived-1')); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create relocated history synchronization fixture." }
python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); c.execute('update threads set rollout_path=? where id=?',(r'C:\old-codex-home\sessions\rollout-archived-1.jsonl','archived-1')); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create cross-root relocated history synchronization fixture." }

$originalPath = $env:PATH
try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    $thirdSwitchOutput = & $exe --switch-third-party --root $root --url "https://api.example.test" --model "test-model" --key "test-token-not-a-real-key"
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Third-party switch failed with exit code $LASTEXITCODE." }
$thirdSwitchText = $thirdSwitchOutput -join "`n"
if ($thirdSwitchText -notmatch 'Skipped 1 missing or stale') { throw "Missing JSONL skip was not reported: $thirdSwitchText" }
if ($thirdSwitchText -notmatch 'Codex may have moved or cleaned them during an update') { throw "Codex update explanation was not reported: $thirdSwitchText" }

$third = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($third -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Third-party provider missing." }
if ($third -notmatch '(?m)^wire_api = "responses"\r?$') { throw "Responses wire API missing." }
if ($third -notmatch '(?m)^base_url = "https://api\.example\.test/v1"\r?$') { throw "Base URL was not normalized to /v1." }
if ($third -notmatch '(?m)^\[model_providers\.custom\.auth\]\r?$') { throw "Credential helper missing." }
$helperPath = Join-Path $root "api-switcher\CodexApiSwitcher.AuthHelper.exe"
$escapedHelperPath = [regex]::Escape(($helperPath -replace '\\', '\\'))
if ($third -notmatch ('(?m)^command = "' + $escapedHelperPath + '"\r?$')) { throw "Credential helper did not use stable helper path." }
if (!(Test-Path -LiteralPath $helperPath)) { throw "Stable credential helper was not installed." }
if ($third -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "MCP config was not preserved." }
if ($third -match 'test-token-not-a-real-key') { throw "Plaintext test key leaked into config." }

$profiles = & $exe --root $root --list-profiles
if ($LASTEXITCODE -ne 0) { throw "Profile list failed." }
$profilesText = $profiles -join "`n"
if ($profilesText -notmatch 'api\.example\.test\|https://api\.example\.test/v1\|test-model') {
    throw "Third-party switch did not save a reusable profile: $profilesText"
}
$profileToken = & $exe --root $root --emit-profile-token --name "api.example.test"
if ($LASTEXITCODE -ne 0 -or $profileToken -ne "test-token-not-a-real-key") {
    throw "Profile credential helper returned the wrong token: $profileToken"
}
$profileMetadata = Get-Content -LiteralPath (Join-Path $root "api-switcher\profiles.dat") -Raw -Encoding UTF8
if ($profileMetadata -match 'test-token-not-a-real-key') { throw "Plaintext profile key leaked into profile metadata." }
$profileCredentialText = Get-Content -LiteralPath (Join-Path $root "api-switcher\profiles\*.dat") -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
if ($profileCredentialText -match 'test-token-not-a-real-key') { throw "Plaintext profile key leaked into encrypted profile credential." }

$thirdCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($thirdCounts -ne "4,4,0") { throw "Third-party switch changed the wrong visible rows: $thirdCounts" }
$activeThirdCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $activeStatePath
if ($activeThirdCounts -ne "4,4,0") { throw "Third-party switch did not update the active SQLite database: $activeThirdCounts" }
$thirdProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($thirdProviders -ne "openai,custom,custom,custom,custom") { throw "Third-party history synchronization failed: $thirdProviders" }
$activeThirdProviders = python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($activeThirdProviders -ne "openai,custom,custom,custom,custom") { throw "Active SQLite third-party synchronization failed: $activeThirdProviders" }
$thirdUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$thirdCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$thirdArchivedMeta = (Get-Content -LiteralPath $archivedRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($thirdUserMeta -ne "custom" -or $thirdCliMeta -ne "custom" -or $thirdArchivedMeta -ne "custom") { throw "Third-party JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $userRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Third-party synchronization changed conversation content." }
if ((Get-Content -LiteralPath $archivedRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Third-party synchronization changed archived conversation content." }

$token = & $exe --emit-token --root $root
if ($LASTEXITCODE -ne 0) { throw "Credential helper failed with exit code $LASTEXITCODE." }
if ($token -ne "test-token-not-a-real-key") { throw "Credential helper returned the wrong token." }

$profileSwitchOutput = & $exe --switch-third-party --root $root --url "https://api.example.test" --model "test-model" --profile "api.example.test"
if ($LASTEXITCODE -ne 0) { throw "Third-party profile switch failed with exit code $LASTEXITCODE." }
$profileTokenAfterSwitch = & $exe --emit-token --root $root
if ($LASTEXITCODE -ne 0 -or $profileTokenAfterSwitch -ne "test-token-not-a-real-key") {
    throw "Switching with a saved profile did not restore the profile token."
}

$defaultCompatRoot = Join-Path $PSScriptRoot ("exe-default-compat-root-" + [guid]::NewGuid().ToString("N"))
$defaultCompatConfig = Join-Path $defaultCompatRoot "config.toml"
New-Item -ItemType Directory -Path $defaultCompatRoot | Out-Null
[System.IO.File]::WriteAllText(
    $defaultCompatConfig,
    'model_provider = "openai"' + "`n" +
    'model = "gpt-5.5"' + "`n",
    [System.Text.UTF8Encoding]::new($false))
$defaultCompatOutput = & $exe --switch-third-party --root $defaultCompatRoot --url "https://default-compat.example.test" --model "default-compat-model" --key "default-compat-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Default third-party switch failed." }
$defaultCompatResult = Get-Content -LiteralPath $defaultCompatConfig -Raw -Encoding UTF8
foreach ($pattern in @(
    '(?m)^web_search = "disabled"\r?$',
    '(?m)^tools_view_image = false\r?$',
    '(?m)^image_generation = false\r?$',
    '(?m)^imagegen = false\r?$',
    '(?m)^view_image = false\r?$'
)) {
    if ($defaultCompatResult -match $pattern) {
        throw "Third-party compatibility mode should be off by default but wrote: $pattern`n$defaultCompatResult"
    }
}
if (Test-Path -LiteralPath (Join-Path $defaultCompatRoot "api-switcher\third-party-compatibility.dat")) {
    throw "Third-party compatibility backup should not be created by default."
}

$legacyCompatRoot = Join-Path $PSScriptRoot ("exe-legacy-compat-root-" + [guid]::NewGuid().ToString("N"))
$legacyCompatConfig = Join-Path $legacyCompatRoot "config.toml"
$legacyCompatSettingsDir = Join-Path $legacyCompatRoot "api-switcher"
$legacyCompatSettings = Join-Path $legacyCompatSettingsDir "settings.dat"
New-Item -ItemType Directory -Path $legacyCompatSettingsDir | Out-Null
[System.IO.File]::WriteAllText(
    $legacyCompatConfig,
    'model_provider = "openai"' + "`n" +
    'model = "gpt-5.5"' + "`n",
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText(
    $legacyCompatSettings,
    'thirdPartyCompatibilityMode=' +
    [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('1')) +
    "`n",
    [System.Text.UTF8Encoding]::new($false))
$legacyCompatOutput = & $exe --switch-third-party --root $legacyCompatRoot --url "https://legacy-compat.example.test" --model "legacy-compat-model" --key "legacy-compat-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Legacy compatibility setting switch failed." }
$legacyCompatResult = Get-Content -LiteralPath $legacyCompatConfig -Raw -Encoding UTF8
foreach ($pattern in @(
    '(?m)^web_search = "disabled"\r?$',
    '(?m)^tools_view_image = false\r?$',
    '(?m)^image_generation = false\r?$',
    '(?m)^imagegen = false\r?$',
    '(?m)^view_image = false\r?$'
)) {
    if ($legacyCompatResult -match $pattern) {
        throw "Legacy saved compatibility mode should not turn compatibility on by default but wrote: $pattern`n$legacyCompatResult"
    }
}
if (Test-Path -LiteralPath (Join-Path $legacyCompatRoot "api-switcher\third-party-compatibility.dat")) {
    throw "Legacy saved compatibility mode should not create a compatibility backup by default."
}
$legacyCompatSettingsResult = Get-Content -LiteralPath $legacyCompatSettings -Raw -Encoding UTF8
$legacyCompatSavedValue = ($legacyCompatSettingsResult -split "`r?`n" |
    Where-Object { $_ -like 'thirdPartyCompatibilityMode=*' } |
    Select-Object -First 1)
if ($legacyCompatSavedValue -ne ('thirdPartyCompatibilityMode=' + [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('0')))) {
    throw "Legacy saved compatibility mode should be cleared after switching."
}

$compatModeRoot = Join-Path $PSScriptRoot ("exe-compat-mode-root-" + [guid]::NewGuid().ToString("N"))
$compatModeConfig = Join-Path $compatModeRoot "config.toml"
New-Item -ItemType Directory -Path $compatModeRoot | Out-Null
$compatModeFixture = @'
model_provider = "openai"
model = "gpt-5.5"
web_search = "cached"
tools_view_image = true

[features]
js_repl = false
image_generation = true
browser_use = true

[tools]
view_image = true

[plugins."browser-use@openai-bundled"]
enabled = true

[plugins."documents@openai-primary-runtime"]
enabled = true

[mcp_servers.example]
command = "example.exe"
'@
[System.IO.File]::WriteAllText($compatModeConfig, $compatModeFixture, [System.Text.UTF8Encoding]::new($false))
$compatModeThirdOutput = & $exe --switch-third-party --compat-mode --root $compatModeRoot --url "https://compat-mode.example.test" --model "compat-mode-model" --key "compat-mode-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Third-party compatibility mode switch failed." }
$compatModeThirdConfig = Get-Content -LiteralPath $compatModeConfig -Raw -Encoding UTF8
foreach ($pattern in @(
    '(?m)^web_search = "disabled"\r?$',
    '(?m)^tools_view_image = false\r?$',
    '(?m)^image_generation = false\r?$',
    '(?m)^imagegen = false\r?$',
    '(?m)^imagegenext = false\r?$',
    '(?m)^browser_use = false\r?$',
    '(?m)^computer_use = false\r?$',
    '(?m)^in_app_browser = false\r?$',
    '(?m)^view_image = false\r?$',
    '(?m)^\[plugins\."browser-use@openai-bundled"\]\r?\nenabled = false\r?$',
    '(?m)^\[plugins\."documents@openai-primary-runtime"\]\r?\nenabled = false\r?$'
)) {
    if ($compatModeThirdConfig -notmatch $pattern) {
        throw "Compatibility mode did not write expected config pattern: $pattern`n$compatModeThirdConfig"
    }
}
if ($compatModeThirdConfig -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Compatibility mode removed MCP config." }
$compatBackup = Join-Path $compatModeRoot "api-switcher\third-party-compatibility.dat"
if (!(Test-Path -LiteralPath $compatBackup)) { throw "Compatibility mode backup was not written." }

$compatModeOfficialOutput = & $exe --switch-official --root $compatModeRoot --model "official-compat-mode-model"
if ($LASTEXITCODE -ne 0) { throw "Official switch after compatibility mode failed." }
$compatModeOfficialConfig = Get-Content -LiteralPath $compatModeConfig -Raw -Encoding UTF8
foreach ($pattern in @(
    '(?m)^web_search = "cached"\r?$',
    '(?m)^tools_view_image = true\r?$',
    '(?m)^image_generation = true\r?$',
    '(?m)^browser_use = true\r?$',
    '(?m)^view_image = true\r?$',
    '(?m)^\[plugins\."browser-use@openai-bundled"\]\r?\nenabled = true\r?$',
    '(?m)^\[plugins\."documents@openai-primary-runtime"\]\r?\nenabled = true\r?$'
)) {
    if ($compatModeOfficialConfig -notmatch $pattern) {
        throw "Compatibility mode did not restore expected config pattern: $pattern`n$compatModeOfficialConfig"
    }
}
foreach ($pattern in @(
    '(?m)^imagegen = false\r?$',
    '(?m)^imagegenext = false\r?$',
    '(?m)^computer_use = false\r?$',
    '(?m)^in_app_browser = false\r?$'
)) {
    if ($compatModeOfficialConfig -match $pattern) {
        throw "Compatibility mode left behind a generated setting: $pattern`n$compatModeOfficialConfig"
    }
}
if (Test-Path -LiteralPath $compatBackup) { throw "Compatibility mode backup was not removed after restoration." }
python -c "import tomllib; d=tomllib.load(open(r'$compatModeConfig','rb')); assert d['model_provider']=='openai'; assert d['model']=='official-compat-mode-model'; assert d['web_search']=='cached'; assert d['features']['image_generation'] is True; assert d['tools']['view_image'] is True; assert d['plugins']['browser-use@openai-bundled']['enabled'] is True"
if ($LASTEXITCODE -ne 0) { throw "Restored compatibility mode TOML is invalid." }

try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    $officialSwitchOutput = & $exe --switch-official --root $root --model "official-test-model"
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Official switch failed with exit code $LASTEXITCODE." }
$officialSwitchText = $officialSwitchOutput -join "`n"
if ($officialSwitchText -notmatch 'Skipped 1 missing or stale') { throw "Official missing JSONL skip was not reported: $officialSwitchText" }

$official = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($official -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Official provider missing." }
if ($official -notmatch '(?m)^model = "official-test-model"\r?$') { throw "Official model missing." }
if ($official -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Official switch changed MCP config." }
if ($official -match '(?m)^\[model_providers\.custom(?:\.|\])') { throw "Official switch retained the custom provider section." }

$officialProviders = python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($officialProviders -ne "openai,openai,openai,openai,openai") { throw "Official history synchronization failed: $officialProviders" }
$activeOfficialProviders = python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); print(','.join(r[0] for r in c.execute('select model_provider from threads order by id')))"
if ($activeOfficialProviders -ne "openai,openai,openai,openai,openai") { throw "Active SQLite official synchronization failed: $activeOfficialProviders" }
$officialUserMeta = (Get-Content -LiteralPath $userRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$officialCliMeta = (Get-Content -LiteralPath $cliRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
$officialArchivedMeta = (Get-Content -LiteralPath $archivedRollout -Encoding UTF8 -TotalCount 1 | ConvertFrom-Json).payload.model_provider
if ($officialUserMeta -ne "openai" -or $officialCliMeta -ne "openai" -or $officialArchivedMeta -ne "openai") { throw "Official JSONL metadata synchronization failed." }
if ((Get-Content -LiteralPath $cliRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Official synchronization changed conversation content." }
if ((Get-Content -LiteralPath $archivedRollout -Encoding UTF8)[1] -ne $bodyLine) { throw "Official synchronization changed archived conversation content." }

$compatRoot = Join-Path $PSScriptRoot ("exe-compat-root-" + [guid]::NewGuid().ToString("N"))
$compatConfig = Join-Path $compatRoot "config.toml"
$compatState = Join-Path $compatRoot "state_5.sqlite"
New-Item -ItemType Directory -Path $compatRoot | Out-Null
[System.IO.File]::WriteAllText($compatConfig, 'model_provider = "openai"' + "`n" + 'model = "gpt-5.5"' + "`n", [System.Text.UTF8Encoding]::new($false))
python -c "import sqlite3; c=sqlite3.connect(r'$compatState'); c.execute('create table threads(id text primary key, rollout_path text not null)'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to create incompatible schema fixture." }
$compatOutput = & $exe --switch-third-party --root $compatRoot --url "https://compat.example.test" --model "compat-model" --key "compat-token-not-a-real-key"
if ($LASTEXITCODE -ne 0) { throw "Compatibility fallback blocked the API switch." }
$compatText = $compatOutput -join "`n"
if ($compatText -notmatch 'History synchronization warning \(the API switch still completed\)') { throw "Compatibility warning was not reported: $compatText" }
$compatResult = Get-Content -LiteralPath $compatConfig -Raw -Encoding UTF8
if ($compatResult -notmatch '(?m)^model_provider = "custom"\r?$') { throw "Compatibility fallback did not finish the API switch." }
$compatWarningLog = Join-Path $compatRoot "api-switcher\history-sync-warnings.log"
if (!(Test-Path -LiteralPath $compatWarningLog)) { throw "Compatibility warning log was not written." }

$envRoot = Join-Path $PSScriptRoot ("exe-env-root-" + [guid]::NewGuid().ToString("N"))
$envConfig = Join-Path $envRoot "config.toml"
New-Item -ItemType Directory -Path $envRoot | Out-Null
[System.IO.File]::WriteAllText(
    $envConfig,
    'model_provider = "custom"' + "`n" +
    'model = "gpt-5.5"' + "`n`n" +
    '[model_providers.custom]' + "`n" +
    'wire_api = "responses"' + "`n" +
    'base_url = "https://old.example/v1"' + "`n",
    [System.Text.UTF8Encoding]::new($false))
$originalProcessCodexApiKey = $env:CODEX_API_KEY
$originalEnvironmentTestFlag = $env:CODEX_SWITCHER_TEST_PROCESS_ENV
try {
    $env:CODEX_SWITCHER_TEST_PROCESS_ENV = "1"
    $env:CODEX_API_KEY = "process-test-key-not-a-real-secret"
    $envOfficialOutput = & $exe --switch-official --root $envRoot --model "official-env-model"
    if ($LASTEXITCODE -ne 0) { throw "Official environment override handling failed." }
    $envOfficialText = $envOfficialOutput -join "`n"
    if ($envOfficialText -notmatch 'Encrypted and cleared the user CODEX_API_KEY override') { throw "Official environment override notice missing: $envOfficialText" }
    $envOfficialConfig = Get-Content -LiteralPath $envConfig -Raw -Encoding UTF8
    if ($envOfficialConfig -match '(?m)^\[model_providers\.custom(?:\.|\])') { throw "Environment fixture retained custom provider in official mode." }
    $envBackup = Join-Path $envRoot "api-switcher\codex-api-key.user-env.dat"
    if (!(Test-Path -LiteralPath $envBackup) -or (Get-Item -LiteralPath $envBackup).Length -le 0) { throw "CODEX_API_KEY environment backup was not written." }

    Remove-Item Env:CODEX_API_KEY -ErrorAction SilentlyContinue
    $envThirdOutput = & $exe --switch-third-party --root $envRoot --url "https://env.example.test" --model "env-model" --key "env-token-not-a-real-key"
    if ($LASTEXITCODE -ne 0) { throw "Third-party environment override restoration failed." }
    $envThirdText = $envThirdOutput -join "`n"
    if ($envThirdText -notmatch 'Restored the previously saved user CODEX_API_KEY override') { throw "Third-party environment restore notice missing: $envThirdText" }
} finally {
    if ($null -eq $originalProcessCodexApiKey) {
        Remove-Item Env:CODEX_API_KEY -ErrorAction SilentlyContinue
    } else {
        $env:CODEX_API_KEY = $originalProcessCodexApiKey
    }
    if ($null -eq $originalEnvironmentTestFlag) {
        Remove-Item Env:CODEX_SWITCHER_TEST_PROCESS_ENV -ErrorAction SilentlyContinue
    } else {
        $env:CODEX_SWITCHER_TEST_PROCESS_ENV = $originalEnvironmentTestFlag
    }
}

$brokenFixture = @'
model = "broken-model"
disable_response_storage = true

[windows]
sandbox = "unelevated"

[mcp_servers.example]
command = "example.exe"

[model_providers.custom]
name = "broken"
wire_api = "responses"
'@
[System.IO.File]::WriteAllText($config, $brokenFixture, [System.Text.UTF8Encoding]::new($false))

& $exe --reset-config --root $root --model "reset-official-model"
if ($LASTEXITCODE -ne 0) { throw "Model configuration reset failed with exit code $LASTEXITCODE." }

$reset = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($reset -notmatch '(?m)^model_provider = "openai"\r?$') { throw "Reset did not restore model_provider." }
if ($reset -notmatch '(?m)^model = "reset-official-model"\r?$') { throw "Reset did not restore the official model." }
if ($reset -match '(?m)^\[model_providers\.custom\]\r?$') { throw "Reset did not remove the broken custom provider section." }
if ($reset -notmatch '(?m)^\[mcp_servers\.example\]\r?$') { throw "Reset removed MCP configuration." }
if ($reset -notmatch '(?m)^disable_response_storage = true\r?$') { throw "Reset removed unrelated top-level configuration." }
$storedHotkeyAfterSwitches = & $exe --root $root --show-hotkey
if ($LASTEXITCODE -ne 0 -or $storedHotkeyAfterSwitches -ne "Ctrl+Shift+F9") {
    throw "Stored hotkey was not preserved after switching: $storedHotkeyAfterSwitches"
}

$backups = Get-ChildItem -LiteralPath (Join-Path $root "config-switcher-backups") -File -Filter "config.toml.*.bak"
if ($backups.Count -lt 3) { throw "Expected automatic backups." }
$historyBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -File -Filter "state_5.sqlite.*.pre-provider-sync.*.bak"
if ($historyBackups.Count -lt 4) { throw "Expected automatic backups for both history databases." }
$sessionBackups = Get-ChildItem -LiteralPath (Join-Path $root "history_sync_backups") -Recurse -File -Filter "*rollout-*.jsonl"
if ($sessionBackups.Count -lt 6) { throw "Expected automatic JSONL metadata backups." }

python -c "import tomllib; d=tomllib.load(open(r'$config','rb')); assert d['model_provider']=='openai'; assert d['model']=='reset-official-model'; assert d['mcp_servers']['example']['command']=='example.exe'; assert 'custom' not in d.get('model_providers',{})"
if ($LASTEXITCODE -ne 0) { throw "Generated TOML is invalid." }

python -c "import sqlite3; c=sqlite3.connect(r'$statePath'); c.execute('update threads set has_user_event=0'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to reset sidebar repair fixture." }
python -c "import sqlite3; c=sqlite3.connect(r'$activeStatePath'); c.execute('update threads set has_user_event=0'); c.commit(); c.close()"
if ($LASTEXITCODE -ne 0) { throw "Failed to reset active sidebar repair fixture." }

try {
    $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot"
    & $exe --repair-sidebar --root $root
} finally {
    $env:PATH = $originalPath
}
if ($LASTEXITCODE -ne 0) { throw "Sidebar repair failed with exit code $LASTEXITCODE." }

$counts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $statePath
if ($counts -ne "4,4,0") { throw "Sidebar repair changed the wrong thread rows: $counts" }
$activeCounts = python (Join-Path $PSScriptRoot "check-sidebar-fixture.py") $activeStatePath
if ($activeCounts -ne "4,4,0") { throw "Sidebar repair did not update the active SQLite database: $activeCounts" }

Write-Output "PASS: Official ChatGPT login override cleanup, encrypted CODEX_API_KEY environment round-trip, custom provider removal in official mode, root and active SQLite synchronization, missing JSONL tolerance with Codex update notice, cross-root archived JSONL relocation, best-effort compatibility fallback, JSONL provider synchronization, unchanged conversation content, Python-free switching, model configuration reset, URL normalization, DPAPI storage, backups, TOML parsing, hotkey persistence, sidebar repair, and config preservation."
