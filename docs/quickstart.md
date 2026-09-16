# Quickstart: как работает MCP (на примере MattermostMCP)

MCP — это JSON-RPC 2.0 между **клиентом** (Claude Code и т.п.) и **сервером**.
Сервер объявляет «инструменты» (tools), клиент их вызывает. В этом проекте все
инструменты работают с followed threads пользователя в Mattermost.

## 1. Транспорт и эндпоинты

`McpEndpoints.MapMcpEndpoints` (`src/McpServer/Handlers/McpEndpoints.cs:15`):

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/mcp` | Streamable HTTP: с `Accept: application/json` отдаёт `GetServerInfo()` (`McpEndpoints.cs:44`), иначе редирект на `/mcp/sse` |
| POST | `/mcp` | основной JSON-RPC |
| GET | `/mcp/sse` + POST `/mcp/message` | legacy SSE |
| GET | `/.well-known/oauth-authorization-server` | OAuth-discovery (RFC 8414), скрыт в `Local` |

В `Local` авторизация выключена (`requireAuth = !env.IsEnvironment("Local")`,
`McpEndpoints.cs:21`); иначе действует политика `AuthenticatedUser`
(`Startup.cs:77`).

## 2. Жизненный цикл протокола

### initialize — клиент узнаёт возможности

Запрос:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize"}
```

Ответ (собирается в `McpServer.GetServerInfo()`, `src/McpServer/McpServer.cs:97`):

```json
{"protocolVersion":"2024-11-05","capabilities":{"tools":{}},
 "serverInfo":{"name":"mattermost-mcp-server","version":"1.0.0"}}
```

### tools/list — список инструментов

`HandleListTools` (`McpServer.cs:114`) отдаёт 3 дескриптора из
`ToolsHandler.GetTools()` (`ToolsHandler.cs:31`):

- `mattermost_search_threads`
- `mattermost_list_followed_threads`
- `mattermost_get_thread`

У каждого есть `inputSchema` (JSON Schema аргументов), `outputSchema` и
аннотации:

```csharp
new McpJsonRpc.McpToolAnnotations {
    ReadOnlyHint = true, DestructiveHint = false,
    IdempotentHint = true, OpenWorldHint = true
}   // ReadOnlyAnnotations, ToolsHandler.cs:149
```

### tools/call — вызов инструмента

Запрос:

```json
{"jsonrpc":"2.0","id":3,"method":"tools/call",
 "params":{"name":"mattermost_search_threads","arguments":{"query":"v11"}}}
```

Ответ по спецификации MCP: `result.content[]` (для клиента) плюс
`structuredContent` (машиночитаемо):

```json
{"jsonrpc":"2.0","id":3,"result":{
  "content":[{"type":"text","text":"Найдено: 1 тред(ов) по запросу \"v11\"\n- [zeuk...] test-channel (...): v11 test thread for MCP"}],
  "structuredContent":{"query":"v11","page":1,"limit":50,"total":1,
     "threads":[{"threadId":"zeuk...","channelName":null,"matchedMessage":"v11 test thread for MCP","url":"..."}]}}}
```

Сборка ответа — `McpJsonRpc.CreateStructuredResult`
(`src/McpServer/Handlers/McpJsonRpc.cs:68`).

## 3. Маршрутизация запроса внутри сервера

`McpServer.HandleRequestAsync` (`McpServer.cs:27`):

1. читает `method`, `id` (эхо как есть — string/number), `params`;
2. берёт пользователя из claims (`AuthService.GetUsername`) или, в `Local`, из
   `MmDesktop:DeveloperUserName` (`McpServer.cs:50`);
3. резолвит Mattermost `userId` через `ThreadSearchService.ResolveUserIdAsync`
   (по логину, затем email, `ThreadSearchService.cs:67`);
4. `switch`: `initialize` / `tools/list` / `tools/call`, иначе `-32601`.

`ToolsHandler.HandleCallAsync` (`ToolsHandler.cs:219`) валидирует `params.name` и
`arguments`, затем вызывает tool-метод. Валидация аргументов —
`TryGetLimit` / `TryGetPage` / `TryGetBoolean` / `TryGetResponseFormat`
(`ToolsHandler.cs:509+`), например `limit` от 1 до 1000.

## 4. Инструмент → Mattermost REST

Tool-метод не ходит в Mattermost напрямую, а через сервис:

`SearchFollowedThreadsAsync` (`ThreadSearchService.cs:174`) →
`GetFollowedThreadsAsync` (`:115`, `v7`/`v11`) →
`MattermostClient.GetUserThreadsAsync` (`MattermostClient.cs:45`), где
формируется REST-запрос вида:

```
GET /api/v4/users/{uid}/teams/{tid}/threads?per_page=50&page=1&deleted=false&unread=false
```

Авторизация к Mattermost: `MattermostTokenProvider` логинит и хранит токен,
`MattermostAuthHandler` подставляет `Authorization: Bearer` в каждый запрос.

## 5. Ошибки: два уровня

- **Protocol-level** (`McpJsonRpc.CreateError`, `McpJsonRpc.cs:89`): `-32601`
  неизвестный метод, `-32602` неверные `params`, `-32700` parse error
  (`McpEndpoints.cs:171`).
- **Tool-level** (`result.isError: true`, `CreateToolError`, `McpJsonRpc.cs:61`):
  например `"MmDesktop user not found"`, недоступный Mattermost,
  `"'limit' должен быть от 1 до 1000."`. Исключения внутри tool ловятся и
  логируются, клиенту отдаётся безопасный текст (`ToolsHandler.cs:262`).

## 6. Проверка руками

```bash
curl -s -H 'Accept: application/json' http://127.0.0.1:8085/mcp

curl -s -X POST http://127.0.0.1:8085/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"mattermost_search_threads","arguments":{"query":"v11"}}}'
```

## Итого

**MCP = initialize / tools/list / tools/call по JSON-RPC.** Сервер объявляет
схемы и аннотации, а внутри цепочка
`McpServer → ToolsHandler → ThreadSearchService → MattermostClient`
превращает запрос в REST-вызовы Mattermost: JWT-аутентификация на входе и
token-аутентификация к Mattermost.

## См. также

- [README.md](../README.md) — обзор проекта и MCP-инструментов.
- [HOWTO_LOCAL_E2E.md](HOWTO_LOCAL_E2E.md) — локальный запуск.
- [AGENTS.md](../AGENTS.md) — конвенции и форматирование.
