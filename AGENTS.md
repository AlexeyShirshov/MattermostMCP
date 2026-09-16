# AGENTS.md

## Конвенции

- Соглашения по коду (C# / SQL / общее): [instructions/Coding_Conventions.md](instructions/Coding_Conventions.md)
  (папка `instructions/`, а не `docs/` — ссылка в README на `docs/Coding_Conventions.md` устарела).
- Стиль, анализаторы, именование: [.editorconfig](.editorconfig).
- Устройство MCP: [docs/quickstart.md](docs/quickstart.md).
- Спека поиска по followed threads: [docs/specs/followed-threads-search.md](docs/specs/followed-threads-search.md).

## Форматирование

- Все текстовые файлы — CRLF (`end_of_line = crlf`). В рабочей копии CRLF
  сохраняется даже на Linux через `.gitattributes` (`* text=auto eol=crlf`).
- Кодировка — UTF-8 без BOM (`charset = utf-8` в `.editorconfig`).
- Проверка: `dotnet format MattermostMCP.slnx --verify-no-changes`.
- Код в блоках внутри `.md`-документации форматируйте по тем же правилам, что и
  исходники: `dotnet format` markdown не проверяет, поэтому отступы и стиль
  сниппетов соблюдайте вручную; у блока указывайте язык (`csharp`, `json`, `bash`,
  `powershell`).
- Приватные поля — `_camelCase`, приватные `const` / `static readonly` — PascalCase.
- Коммиты — на английском, с маленькой буквы (см. Coding_Conventions).

## Сборка и тесты

- Нужен .NET SDK 10, target — `net10.0`. CI в репозитории нет — всё запускается локально.
- Сборка: `dotnet build MattermostMCP.slnx`. Включены `EnforceCodeStyleInBuild` и
  `AnalysisLevel=latest`, поэтому предупреждения стиля ломают сборку — проверяйте
  её, а не только тесты.
- Все тесты: `dotnet test MattermostMCP.slnx`.
- Один класс/тест: `dotnet test MattermostMCP.slnx --filter "FullyQualifiedName~AuthServiceTests"`.
- Тесты: xUnit v3 + NSubstitute + FluentAssertions. `FluentAssertions` запинена
  как `[7.0.0]` — не обновлять (в 8.x другая лицензия).
- Покрытие:
  `dotnet test MattermostMCP.slnx --settings coverlet.runsettings --collect:"XPlat Code Coverage"`,
  затем `dotnet tool restore` и
  `dotnet reportgenerator "-reports:tests/MattermostMCP.UnitTests/TestResults/**/coverage.opencover.xml" "-targetdir:coverage-report" "-reporttypes:TextSummary"`.

## Особенности репозитория

- `bin/` и `obj/` изолированы по ОС хоста: `bin/<windows|linux|osx>/`, `obj/<...>/`
  (`Directory.Build.props`). WSL и Windows делят одну рабочую копию — не переносите
  артефакты между ними; пути вида `bin/Debug/net10.0` не используются.
- `appsettings.Local.json` нужен для запуска, но не хранится в git (`.gitignore`).
- `data/` — volume Mattermost/Postgres, в git не коммитить.

## Локальный запуск

- Пошаговый сценарий: [docs/HOWTO_LOCAL_E2E.md](docs/HOWTO_LOCAL_E2E.md).
- Сервер: `dotnet run --project src/McpServer --launch-profile McpServer.Local` →
  `http://127.0.0.1:8085`. В окружении `Local` авторизация выключена, пользователь
  берётся из `MmDesktop:DeveloperUserName`.
- Mattermost: `docker compose up -d` (порт 8080), данные готовят PowerShell-скрипты
  `scripts/*.ps1`. В WSL Mattermost может слушать только IPv6 — тогда в
  `MmDesktop:BaseUrl` укажите `http://[::1]:8080`.
