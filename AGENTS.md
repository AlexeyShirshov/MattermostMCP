# AGENTS.md

## Конвенции

- Соглашения по коду (C# / SQL / общее): [Coding_Conventions.md](Coding_Conventions.md)
- Правила стиля, анализаторов и форматирования: [.editorconfig](.editorconfig)

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

- Пошаговый сценарий: [HOWTO_LOCAL_E2E.md](HOWTO_LOCAL_E2E.md).
