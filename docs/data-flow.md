# Data flow: MattermostMCP

Потоки данных проекта `src/McpServer`: как данные превращаются из JSON-RPC-запроса
в REST-вызовы Mattermost, проходят через модели и возвращаются как
`structuredContent` + текстовый блок. Основано на `ThreadSearchService`,
`MattermostClient` и моделях `MmDesktop/Models`.

## 1. Основной путь (поиск по followed threads)

```mermaid
flowchart LR
    C["MCP-клиент"] -->|"params: query, limit, searchTitleOnly, response_format"| S["McpServer"]
    S -->|"login, email"| TS["ThreadSearchService"]
    TS -->|"login"| MC["MattermostClient"]

    MC -->|"GET /users, /teams, /threads, /posts + Bearer"| MM["Mattermost REST API v4"]
    MM <-->|"SQL"| PG[("PostgreSQL 15")]
    MM -->|"users / teams / threads / posts"| MC
    MC -->|"MattermostUser / MattermostTeam / MattermostPost / UserThread"| TS

    TS -->|"ExtractSearchTerms + ContainsAllTerms"| TS
    TS -->|"FindChannelByIdAsync -> ChannelName"| MC
    TS -->|"permalink {UiBaseUrl}/pl/{postId}"| TS
    TS -->|"ThreadSearchResult[]"| TH["ToolsHandler"]

    TH -->|"structuredContent + markdown-текст"| M["McpJsonRpc"]
    M -->|"JSON-RPC result: content[], structuredContent"| S
    S -->|"HTTP 200 JSON"| C

    CFG["MmDesktop:UiBaseUrl"] -.->|"база пермалинков"| TS
    DB[("Mattermost: ThreadsApiVersion v7 / v11")] -.->|"выбор роута"| TS
```

## 2. Идентификация пользователя (SSO -> Mattermost userId)

```mermaid
flowchart LR
    JWT["SSO JWT (Authorization: Bearer)"] -->|"claims"| AS["AuthService"]
    AS -->|"preferred_username / email"| MS["McpServer"]
    MS -->|"login, email"| TS["ThreadSearchService"]
    TS -->|"login"| MC["MattermostClient"]
    MC -->|"GET /api/v4/users/username/{login}"| MM["Mattermost REST API v4"]
    MM -->|"MattermostUser.Id"| MC
    MC -->|"userId"| TS
    TS -->|"userId"| MS

    ENV["MmDesktop:DeveloperUserName"] -.->|"только Local, без SSO"| MS
```

## Примечания

- `response_format` (`markdown` / `json`) влияет только на текстовый блок
  ответа; `structuredContent` формируется всегда.
- В режиме `v11` данные тредов агрегируются по всем командам пользователя
  (`GetUserTeamsAsync` -> `GetUserThreadsAsync` для каждого `team.Id`).
- `ThreadSearchResult.MattermostUrl` строится локально из `UiBaseUrl`, а не
  приходит из Mattermost.

## См. также

- [class-diagram.md](class-diagram.md) — классы и связи.
- [control-flow.md](control-flow.md) — потоки управления.
- [sequence.md](sequence.md) — сценарии вызовов.
