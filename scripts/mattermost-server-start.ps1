# Полный локальный стенд Mattermost: поднять стек и завести команду/канал/пользователей.
# Дефолтный пароль локального ММ-юзера — совпадает с appsettings.Local.json.
# Запуск: powershell -File scripts/mattermost-server-start.ps1

param(
    [string]$ComposeEngine = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Дефолтный пароль локального ММ-юзера (совпадает с appsettings.Local.json)
$script:LocalPassword = "ChangeMe123!"

& (Join-Path $scriptDir "mattermost-server-preview.ps1") -ComposeEngine $ComposeEngine
& (Join-Path $scriptDir "prepare.ps1") -ComposeEngine $ComposeEngine

Write-Host ""
Write-Host "Готово. MM: http://localhost:8080, пользователь testuser, пароль $script:LocalPassword"
