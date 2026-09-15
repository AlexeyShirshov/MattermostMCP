# MattermostMCP

MCP-сервер, который отдаёт **followed threads** пользователя из Mattermost и умеет
искать по ним. Реализован на ASP.NET Core и работает с Mattermost REST API v4
напрямую (локальный клиент `MmDesktop`, внешний сервис не требуется).

## MCP-инструменты

| Инструмент | Назначение | Параметры |
| --- | --- | --- |
| `mattermost_search_threads` | Полнотекстовый поиск по followed threads | `query` (обязательный), `limit` (default 50, max 1000), `searchTitleOnly` (default false), `response_format` (`markdown`/`json`) |
| `mattermost_list_followed_threads` | Постраничный список followed threads | `limit` (default 50, max 1000), `page` (default 1), `response_format` |
| `mattermost_get_thread` | Полное содержимое треда по id | `threadId` (обязательный), `response_format` |

Все инструменты помечены аннотациями `readOnlyHint: true`, `destructiveHint: false`,
`idempotentHint: true`, `openWorldHint: true`, имеют `outputSchema` и возвращают
`structuredContent` (машиночитаемо) плюс текстовый блок в выбранном
`response_format`.

Совпадение по словам: тред подходит, если **все** слова запроса встречаются в
сообщении (в любом порядке). `searchTitleOnly` ограничивает поиск первым
сообщением треда. В ответе на найденное сообщение отдаётся пермалинк
`{MmDesktop:UiBaseUrl}/pl/{postId}` — ссылка именно на сообщение, а не на корень треда.

Список followed threads постраничный: ответ содержит `page`, `limit`, `total`,
`hasMore` и `nextPage`; используйте `page`, чтобы получить следующую страницу.

## Состав решения

| Проект | Назначение |
| --- | --- |
| `src/McpServer` | ASP.NET Core хост: JSON-RPC MCP (Streamable HTTP + legacy SSE), OAuth-метаданные, REST `/api/threads/*` |
| `tests/MattermostMCP.UnitTests` | Юнит-тесты (xUnit) |

## Как это работает

1. MCP-клиент вызывает `tools/call` с одним из инструментов выше.
2. `McpServer` разбирает JSON-RPC, определяет пользователя
   (`AuthService`) и резолвит его Mattermost user id
   (`/users/name/{name}` или `/users/email/{email}`).
3. `ThreadSearchService` забирает followed threads пользователя:
   - `v11` — `/users/{uid}/teams/{tid}/threads` по всем командам пользователя;
   - `v7` — `/users/{uid}/threads`;

   затем фильтрует их по словам запроса и формирует ссылки на сообщения.
4. REST-зеркало доступно по `GET /api/threads/search` и `GET /api/threads/followed`.

## Транспорт и эндпоинты

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/mcp` | Streamable HTTP: данные инициализации (`Accept: application/json`), иначе редирект на `/mcp/sse` |
| POST | `/mcp` | JSON-RPC (Streamable HTTP) — основной endpoint |
| GET | `/mcp/sse` | Legacy SSE-подписка (устаревший транспорт, оставлен для совместимости) |
| POST | `/mcp/message` | JSON-RPC для legacy SSE |
| GET | `/.well-known/oauth-authorization-server` | OAuth-discovery (RFC 8414), только при включённой авторизации |
| GET | `/api/threads/search?query=&limit=` | REST-поиск по followed threads |
| GET | `/api/threads/followed?limit=&page=` | REST-список followed threads (с `hasMore`) |

Основной транспорт — Streamable HTTP (`POST /mcp`); SSE помечен как legacy.

## Конфигурация

Секция `MmDesktop`:

| Ключ | Назначение |
| --- | --- |
| `BaseUrl` | Адрес Mattermost API (напр. `http://localhost:8080`) |
| `Username` / `Password` | Локальные креды для логина в Mattermost (только для локального запуска) |
| `PerPage` | Размер страницы при выборке тредов |
| `TimeoutSeconds` | Таймаут HTTP-запросов |
| `ThreadsApiVersion` | `v7` или `v11` |
| `UiBaseUrl` | База для пермалинков, напр. `http://localhost:8080/test-team` |
| `DeveloperUserName` | Пользователь для локального запуска (auth bypass) |

Секция `Auth`: `Authority`, `Audience`, `RequireHttpsMetadata`.

Секция `Cors`: `AllowedOrigins` (массив). Если не задана, разрешены только
loopback-origin'ы; запросы с `Origin` вне списка отклоняются (403).

## Аутентификация

Входящий запрос приходит с `Authorization: Bearer <SSO JWT>`; политика
`AuthenticatedUser` защищает MCP- и REST-эндпоинты. Локально JWT инжектит
`scripts/mcp_token_proxy.ps1` (password/refresh grant к SSO). Пользователь из
claim'ов (`preferred_username` → `user_id` → `name` → `sub`) маппится на
Mattermost-login. В окружении `Local` авторизация отключена — используется
`MmDesktop:DeveloperUserName`.

## Локальный запуск

См. [HOWTO_LOCAL_E2E.md](HOWTO_LOCAL_E2E.md).

## Соглашения

Код следует внутреннему стайлгайду: primary constructors, pattern matching для
null-проверок, `_camelCase` для приватных полей, `DateTimeOffset`, `Async`-суффиксы,
`CancellationToken` полностью, логи с префиксом `Log`, `sealed`/`record` для DTO.

Полный свод правил: [Соглашения по коду (C# / SQL / Общее)](Coding_Conventions.md).
