# Control flow: MattermostMCP

Потоки управления проекта `src/McpServer`: сначала проход запроса через middleware
и маршрутизацию, затем диспетчеризация JSON-RPC и выполнение MCP-инструмента.
Основано на `Startup.Configure`, `McpEndpoints.MapMcpEndpoints`,
`McpServer.HandleRequestAsync` и `ToolsHandler.HandleCallAsync`.

## 1. Middleware и маршрутизация HTTP

```mermaid
flowchart TD
    A["HTTP request"] --> B{"Origin задан?"}
    B -- "да, не разрешён" --> B1["403 Forbidden"]
    B -- "нет / разрешён" --> D["UseCors -> UseAuthentication -> UseAuthorization"]
    D --> E{"Маршрут"}
    E -- "GET /mcp" --> F{"Accept: application/json?"}
    F -- "да" --> F1["GetServerInfo + Mcp-Session-Id"]
    F -- "нет" --> F2["302 Redirect -> /mcp/sse"]
    E -- "GET /mcp/sse" --> G{"requireAuth?"}
    G -- "да" --> G1["Политика AuthenticatedUser"]
    G -- "нет (Local)" --> G2["SSE-стрим + keep-alive"]
    G1 --> G2
    E -- "POST /mcp или /mcp/message" --> H{"Аутентифицирован? (вне Local)"}
    H -- "нет" --> H1["401 Unauthorized"]
    H -- "да / Local" --> H2["Читать тело запроса"]
    E -- "GET /.well-known/oauth-authorization-server" --> I{"requireAuth?"}
    I -- "нет (Local)" --> I1["404 Not Found"]
    I -- "да" --> I2["RFC 8414 metadata (OAuthMetadata)"]
    E -- "GET /api/threads/search or followed" --> J["ThreadEndpoints (политика AuthenticatedUser)"]
```

## 2. Диспетчеризация JSON-RPC и вызов инструмента

```mermaid
flowchart TD
    K["Тело POST /mcp"] --> L{"JSON парсится?"}
    L -- "нет" --> L1["-32700 Parse error"]
    L -- "да" --> M["HandleRequestAsync: method, id, params"]
    M --> N{"Principal аутентифицирован?"}
    N -- "нет (Local)" --> N1["username = MmDesktop:DeveloperUserName"]
    N -- "да" --> N2["username = AuthService.GetUsername(claims)"]
    N1 --> O{"login / email заданы?"}
    N2 --> O
    O -- "да" --> O1["ThreadSearchService.ResolveUserIdAsync -> userId"]
    O -- "нет" --> O2["userId = null"]
    O1 --> P{"method"}
    O2 --> P
    P -- "initialize" --> P1["McpServer.GetServerInfo()"]
    P -- "tools/list" --> P2["ToolsHandler.GetTools() -> 3 инструмента"]
    P -- "tools/call" --> Q["ToolsHandler.HandleCallAsync"]
    P -- "другое" --> P3["-32601 Method not found"]
    Q --> R{"params.name — непустая строка?"}
    R -- "нет" --> R1["-32602 Invalid params"]
    R -- "да" --> S{"Имя инструмента"}
    S -- "mattermost_search_threads" --> S1["Валидация query / limit / searchTitleOnly / response_format"]
    S -- "mattermost_list_followed_threads" --> S2["Валидация limit / page / response_format"]
    S -- "mattermost_get_thread" --> S3["Валидация threadId / response_format"]
    S -- "неизвестный" --> S4["isError: неизвестный инструмент"]
    S1 --> T{"userId пуст?"}
    S2 --> T
    S3 --> T
    T -- "да" --> T1["isError: Mattermost-пользователь не найден"]
    T -- "нет" --> U["Вызов ThreadSearchService"]
    U --> V{"Успех?"}
    V -- "да" --> V1["CreateStructuredResult: content + structuredContent"]
    V -- "исключение" --> V2["Лог + isError с безопасным текстом"]
    V -- "OperationCanceled" --> V3["Проброс исключения (отмена)"]
```

## См. также

- [class-diagram.md](class-diagram.md) — классы и связи.
- [data-flow.md](data-flow.md) — потоки данных.
- [sequence.md](sequence.md) — сценарии вызовов.
- [quickstart.md](quickstart.md) — протокол MCP и эндпоинты.
