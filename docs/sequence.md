# Sequence: MattermostMCP

Сценарии взаимодействия проекта `src/McpServer`: основной вызов инструмента
(`tools/call`), базовая инициализация и OAuth-поток при включённой авторизации.

## 1. tools/call: поиск по followed threads

```mermaid
sequenceDiagram
    autonumber
    participant C as "MCP-клиент"
    participant EP as "McpEndpoints"
    participant MS as "McpServer"
    participant TH as "ToolsHandler"
    participant TS as "ThreadSearchService"
    participant TP as "MattermostTokenProvider"
    participant MC as "MattermostClient"
    participant MM as "Mattermost REST API"

    C->>+EP: POST /mcp (tools/call)
    EP->>+MS: HandleRequestAsync(body, user)
    MS->>MS: method / id / params
    MS->>+TS: ResolveUserIdAsync(login, email)
    TS->>+TP: GetTokenAsync()
    alt Токен не закэширован
        TP->>+MM: POST /api/v4/users/login
        MM-->>-TP: Token header
    end
    TP-->>-TS: Bearer-токен
    TS->>+MC: FindUserByNameAsync(login)
    MC->>+MM: GET /api/v4/users/username/{login}
    MM-->>-MC: MattermostUser
    MC-->>-TS: MattermostUser
    TS-->>-MS: userId
    MS->>+TH: HandleCallAsync(params, id, user, userId)
    TH->>TH: Валидация name и аргументов
    TH->>+TS: SearchFollowedThreadsAsync(userId, request)
    TS->>+MC: GetUserThreadsAsync(userId)
    MC->>+MM: GET /users/{uid}/teams/{tid}/threads (v11)
    MM-->>-MC: threads
    MC-->>-TS: UserThreadsResponse
    loop По каждому треду
        TS->>+MC: GetThreadPostsAsync(threadId)
        MC->>+MM: GET /api/v4/posts/{id}/thread
        MM-->>-MC: posts
        MC-->>-TS: posts
        TS->>TS: Совпадение всех слов + имя канала
    end
    TS-->>-TH: ThreadSearchResult[]
    TH->>TH: Сборка structuredContent и markdown
    TH-->>-MS: JSON-RPC result
    MS-->>-EP: JsonDocument
    EP-->>-C: 200 content[] + structuredContent
```

## 2. initialize / tools/list

```mermaid
sequenceDiagram
    autonumber
    participant C as "MCP-клиент"
    participant EP as "McpEndpoints"
    participant MS as "McpServer"
    participant TH as "ToolsHandler"

    C->>+EP: POST /mcp (initialize)
    EP->>+MS: HandleRequestAsync(body, user)
    MS-->>-EP: protocolVersion, capabilities, serverInfo
    EP-->>-C: 200 JSON-RPC result

    C->>+EP: POST /mcp (tools/list)
    EP->>+MS: HandleRequestAsync(body, user)
    MS->>+TH: GetTools()
    TH-->>-MS: 3 x McpTool (inputSchema, outputSchema, annotations)
    MS-->>-EP: JSON-RPC result
    EP-->>-C: 200 JSON-RPC result
```

## 3. OAuth 2.0 + PKCE (когда авторизация включена)

```mermaid
sequenceDiagram
    autonumber
    participant C as "MCP-клиент"
    participant S as "MCP-сервер"
    participant SSO as "SSO Keycloak"

    C->>+S: GET /.well-known/oauth-authorization-server
    S-->>-C: authorization / token / jwks endpoints

    C->>SSO: Authorization Code + PKCE
    SSO-->>C: authorization code
    C->>SSO: POST token endpoint (code + verifier)
    SSO-->>C: access token (JWT)

    C->>+S: POST /mcp (Authorization Bearer JWT)
    S->>+SSO: GET JWKS (signing keys)
    SSO-->>-S: набор ключей
    S->>S: Проверка подписи, issuer, audience
    S-->>-C: JSON-RPC result
```

## См. также

- [class-diagram.md](class-diagram.md) — классы и связи.
- [control-flow.md](control-flow.md) — потоки управления.
- [data-flow.md](data-flow.md) — потоки данных.
- [quickstart.md](quickstart.md) — протокол MCP и эндпоинты.
