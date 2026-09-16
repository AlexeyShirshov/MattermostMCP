# Спека: поиск по followed threads

Требования к инструменту поиска по отслеживаемым (followed) тредам:
MCP `mattermost_search_threads` и REST-зеркало `GET /api/threads/search`.

## Требования

1. **Полнотекстовый поиск по сообщениям.** Искать по текстам сообщений в
   отслеживаемых тредах пользователя. Запрос разбивается на слова; тред попадает
   в выдачу, если в одном сообщении встречаются **все** слова запроса (в любом
   порядке, регистронезависимо). При `searchTitleOnly: true` поиск ограничивается
   первым (корневым) сообщением треда.
2. **Формат ответа.** Для каждого совпадения возвращать начало текста сообщения
   (первые 40 символов; при обрезке добавляется `...`) и ссылку на это сообщение —
   пермалинк `{MmDesktop:UiBaseUrl}/pl/{postId}`, а не на корень треда.
3. **Локальный запуск без авторизации.** В окружении `Local` вход не требуется:
   MCP- и REST-эндпоинты не защищаются политикой `AuthenticatedUser`, а
   пользователь берётся из конфигурации `MmDesktop:DeveloperUserName`.

## Критерии приёмки

- `mattermost_search_threads` для каждого совпадения отдаёт `matchedMessage`
  (первые 40 символов) и `url` на конкретное сообщение.
- `searchTitleOnly` переключает поиск между всеми сообщениями треда и только
  корневым.
- При `ASPNETCORE_ENVIRONMENT=Local` запрос выполняется без заголовка
  `Authorization`, пользователь задаётся `MmDesktop:DeveloperUserName`.

## Реализация

| Часть | Файл |
| --- | --- |
| Поиск, обрезка и построение ссылки | `src/McpServer/MmDesktop/ThreadSearchService.cs` |
| MCP-инструмент и вывод | `src/McpServer/Handlers/ToolsHandler.cs` |
| REST-зеркало | `src/McpServer/Handlers/ThreadEndpoints.cs` |
| Локальный режим (auth bypass) | `src/McpServer/Handlers/McpEndpoints.cs`, `src/McpServer/Startup.cs` |
