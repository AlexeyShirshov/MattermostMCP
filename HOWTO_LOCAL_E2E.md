# Локальный запуск (E2E)

Пошаговый сценарий: поднять локальный Mattermost, подготовить данные, запустить
MCP-сервер и проверить инструменты.

## Предпосылки

- Windows + PowerShell.
- `docker-compose` / `docker compose` / `podman compose` / `podman-compose`.
- .NET SDK 10.

> **IPv6.** В WSL локальный Mattermost слушает только на IPv6 (`::1`), поэтому
> `http://127.0.0.1:8080` из .NET может отдавать `No route to host`. В этом
> случае используйте `http://[::1]:8080` в `MmDesktop:BaseUrl`.

## 1. Поднять Mattermost + Postgres

```powershell
docker compose up -d
```

Либо одной командой (стек + данные из шага 2):

```powershell
powershell -File scripts/mattermost-server-start.ps1
```

Либо только стек, без пользователей: `powershell -File scripts/mattermost-server-preview.ps1`.

Mattermost: <http://localhost:8080>. Данные (конфиг, БД, логи, плагины) пишутся в
`./data/` и исключены из git через `.gitignore` (`data/`).

## 2. Подготовить команду, канал и пользователей

```powershell
powershell -File scripts/prepare.ps1
```

Создаётся идемпотентно:

- команда `test-team`, канал `test-channel`;
- `admin@example.com` (system admin) и `testuser@example.com`, пароль `ChangeMe123!`.

## 3. Настроить `src/McpServer/appsettings.Local.json`

```json
{
  "Auth": {
    "Authority": "https://sso.example.com/auth/realms/dev",
    "Audience": "mcp-server",
    "RequireHttpsMetadata": true
  },
  "MmDesktop": {
    "BaseUrl": "http://localhost:8080",
    "Username": "testuser",
    "Password": "ChangeMe123!",
    "ThreadsApiVersion": "v11",
    "UiBaseUrl": "http://localhost:8080/test-team",
    "DeveloperUserName": "testuser"
  }
}
```

## 4. Запустить MCP-сервер

```powershell
dotnet run --project src/McpServer --launch-profile McpServer.Local
```

Поднимается на `http://127.0.0.1:8085`. В окружении `Local` авторизация отключена,
пользователь берётся из `MmDesktop:DeveloperUserName`.

## 5. Проверить руками

```bash
# Информация о сервере
curl -s -H 'Accept: application/json' http://127.0.0.1:8085/mcp

# Инициализация и список инструментов
curl -s -X POST http://127.0.0.1:8085/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
curl -s -X POST http://127.0.0.1:8085/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# Поиск по followed threads
curl -s -X POST http://127.0.0.1:8085/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"mattermost_search_threads","arguments":{"query":"привет"}}}'
```

## 6. Подключить MCP-клиент

`.mcp.json` в корне репозитория:

```json
{
  "mcpServers": {
    "followed-threads": {
      "type": "http",
      "url": "http://localhost:8085/mcp"
    }
  }
}
```

## 7. Опционально: авторизация через SSO

```powershell
$env:MCP_SSO_AUTHORITY = "https://sso.example.com/auth/realms/dev"
$env:MCP_SSO_CLIENT_ID  = "mcp-server"
$env:MCP_SSO_USERNAME   = "testuser"
$env:MCP_SSO_PASSWORD   = "<password>"
$env:MCP_UPSTREAM       = "http://127.0.0.1:8085"
powershell -File scripts/mcp_token_proxy.ps1
```

Прокси слушает `http://127.0.0.1:8081` и добавляет `Authorization: Bearer <token>`.

## Тесты

```bash
dotnet test MattermostMCP.slnx
```

## Типовые проблемы

| Симптом | Причина / решение |
| --- | --- |
| `No route to host (127.0.0.1:8080)` | Mattermost слушает только IPv6 — укажите `http://[::1]:8080` |
| `MmDesktop user ID not found` | Не выполнен `prepare.ps1` или неверный `DeveloperUserName` |
| `Permission denied` для `data/postgres/*` при git-операциях | Каталог игнорируется через `.gitignore` (`data/`); файлы БД не индексируются |
