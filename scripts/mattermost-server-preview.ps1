# Быстрый локальный Mattermost preview: только сервис mattermost, без пользователей.
# Вызывается из mattermost-server-start.ps1 или запускается отдельно.
# Запуск: powershell -File scripts/mattermost-server-preview.ps1

param(
    [string]$ComposeEngine = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ComposeFile = Join-Path $scriptDir ".." "docker-compose.yml"

if ($ComposeEngine -eq "") {
    if (Get-Command docker-compose -ErrorAction SilentlyContinue) { $ComposeEngine = "docker-compose" }
    elseif (Get-Command docker -ErrorAction SilentlyContinue) {
        docker compose version *>$null
        if ($LASTEXITCODE -eq 0) { $ComposeEngine = "docker" }
    }
    elseif (Get-Command podman -ErrorAction SilentlyContinue) {
        podman compose version *>$null
        if ($LASTEXITCODE -eq 0) { $ComposeEngine = "podman" }
    }
    elseif (Get-Command podman-compose -ErrorAction SilentlyContinue) { $ComposeEngine = "podman-compose" }
}

switch ($ComposeEngine) {
    "docker-compose" { & docker-compose -f $ComposeFile up -d }
    "docker"         { & docker compose -f $ComposeFile up -d }
    "podman"         { & podman compose -f $ComposeFile up -d }
    "podman-compose" { & podman-compose -f $ComposeFile up -d }
    default          { throw "Не найден compose." }
}

Write-Host "Mattermost preview поднимается на http://localhost:8080"
