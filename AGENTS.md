# AGENTS.md

## Конвенции

- Соглашения по коду (C# / SQL / общее): [docs/Coding_Conventions.md](docs/Coding_Conventions.md)
- Правила стиля, анализаторов и форматирования: [.editorconfig](.editorconfig)
- Как устроен MCP в проекте: [docs/quickstart.md](docs/quickstart.md)

## Форматирование

- Все текстовые файлы проекта должны использовать переводы строк **CRLF**
  (`end_of_line = crlf` в `.editorconfig`).
- Проверка форматирования: `dotnet format MattermostMCP.slnx --verify-no-changes`.
- Приватные константы и `private static readonly` поля — PascalCase, остальные
  приватные поля — `_camelCase` (см. `.editorconfig`).

## Сборка и тесты

- Все тесты: `dotnet test MattermostMCP.slnx`.
- Покрытие: `dotnet test MattermostMCP.slnx --settings coverlet.runsettings --collect:"XPlat Code Coverage"`.

## Локальный запуск

- Пошаговый сценарий: [docs/HOWTO_LOCAL_E2E.md](docs/HOWTO_LOCAL_E2E.md).
