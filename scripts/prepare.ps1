# Подготовка Mattermost из docker-compose.yml: команда, канал, пользователи.
# Работает поверх стандартного compose: автоопределяет docker / podman / podman-compose.
# Идемпотентен (mmctl ругнётся "already exists" — игнорируем).

param(
    # "docker" | "podman" | "podman-compose" — если пусто, определяется автоматически
    [string]$ComposeEngine = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# docker-compose.yml лежит в корне репозитория (из scripts/ — уровень выше)
$ComposeFile = Join-Path $scriptDir ".." "docker-compose.yml"
$MmService   = "mattermost"
$Port        = 8080

$team_name   = "test-team"
$channel_name = "test-channel"
$admin_name  = "admin"
$admin_email = "admin@example.com"
$app_user_name = "testuser"
$app_user_email = "testuser@example.com"
$password    = "ChangeMe123!"

# --- определить compose-команду ---
# Приоритет: standalone docker-compose v1 (часто единственный рабочий на Windows),
# затем plugin docker compose v2, podman compose, podman-compose.
if ($ComposeEngine -eq "") {
    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        $ComposeEngine = "docker-compose"
    }
    elseif (Get-Command docker -ErrorAction SilentlyContinue) {
        docker compose version *>$null
        if ($LASTEXITCODE -eq 0) { $ComposeEngine = "docker" }
    }
    elseif (Get-Command podman -ErrorAction SilentlyContinue) {
        podman compose version *>$null
        if ($LASTEXITCODE -eq 0) { $ComposeEngine = "podman" }
    }
    elseif (Get-Command podman-compose -ErrorAction SilentlyContinue) {
        $ComposeEngine = "podman-compose"
    }
}
if ($ComposeEngine -eq "") {
    throw "Не найден compose: нужен 'docker-compose', 'docker compose', 'podman compose' или 'podman-compose'."
}

$upCmd = switch ($ComposeEngine) {
    "docker-compose" { "docker-compose -f $ComposeFile up -d" }
    "docker"         { "docker compose -f $ComposeFile up -d" }
    "podman"         { "podman compose -f $ComposeFile up -d" }
    "podman-compose" { "podman-compose -f $ComposeFile up -d" }
}
echo "Compose engine: $ComposeEngine"
echo "Сначала убедитесь, что стек поднят: $upCmd"

# --- mmctl --local внутри сервиса mattermost -- (без глушения stderr: ошибки видно) ---
function Invoke-Mmctl {
    param([string[]]$Cmds)
    switch ($ComposeEngine) {
        "docker-compose" { & docker-compose -f $ComposeFile exec $MmService mmctl --local @Cmds }
        "docker"         { & docker compose -f $ComposeFile exec -T $MmService mmctl --local @Cmds }
        "podman"         { & podman compose -f $ComposeFile exec -T $MmService mmctl --local @Cmds }
        "podman-compose" { & podman-compose -f $ComposeFile exec $MmService mmctl --local @Cmds }
    }
    return $LASTEXITCODE
}

# --- ждём готовности Mattermost (порт открывается, когда сервер уже слушает) ---
echo "Waiting for Mattermost on localhost:${Port} ..."
$ready = $false
for ($i = 0; $i -lt 120; $i++) {
    $open = Test-NetConnection -ComputerName localhost -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($open) { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) {
    echo "Mattermost не ответил — смотрите logs: $ComposeEngine compose -f $ComposeFile logs $MmService"
    exit 1
}

function Create-User([string] $name, [string] $email, [string] $pwd, [switch] $SystemAdmin) {
    echo "Creating user $name ($email)"
    $createArgs = @("user", "create", "--email=$email", "--password=$pwd", "--username=$name", "--email-verified")
    if ($SystemAdmin) { $createArgs += "--system-admin" }
    $null = Invoke-Mmctl $createArgs
    $null = Invoke-Mmctl team users add $team_name $name
    $null = Invoke-Mmctl channel users add "${team_name}:${channel_name}" $name
}

echo "Creating team $team_name"
$null = Invoke-Mmctl team create --name $team_name --display-name $team_name --email $admin_email

echo "Creating channel $channel_name"
$null = Invoke-Mmctl channel create --team $team_name --name $channel_name --display-name $channel_name

Create-User $admin_name $admin_email $password -SystemAdmin

# Пользователь под AD-логин: им логинится МСР-сервер в Mattermost
# (см. MmDesktop:AuthenticationOptions в appsettings.Local.json).
Create-User $app_user_name $app_user_email $password

echo "Done. Mattermost: http://localhost:${Port}"
