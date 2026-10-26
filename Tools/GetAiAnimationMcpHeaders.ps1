$ErrorActionPreference = 'Stop'
$taskSettingsPath = Join-Path $PSScriptRoot '../UserSettings/AI-Game-Developer-Config.json'
$taskSettings = Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($taskSettings.token)) {
    throw 'AI Animation has no local token. Open its Unity panel to initialize the connection.'
}
@{ Authorization = 'Bearer ' + $taskSettings.token } | ConvertTo-Json -Compress
