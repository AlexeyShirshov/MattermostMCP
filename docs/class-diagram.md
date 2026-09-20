# Class diagram: MattermostMCP

UML-диаграммы классов проекта `src/McpServer` (C# / .NET 10, primary constructors).
Классы сгруппированы по слоям; связи — это композиция (`*--`), агрегация (`o--`),
зависимость (`..>`) и реализация интерфейса (`..|>`). Статические классы и методы
(`Program`, `McpEndpoints`, `McpJsonRpc`, `AuthService`, `OAuthMetadata`) отмечены
по смыслу, а не отдельной нотацией.

## Основные классы (хост, handlers, MmDesktop)

```mermaid
classDiagram
    direction LR

    class Program {
        +Main(args) Task
    }

    class Startup {
        +AUTHENTICATED_USER_POLICY
        +ConfigureServices(services)
        +Configure(app)
    }

    class McpEndpoints {
        +MapMcpEndpoints(endpoints, env)
    }

    class ThreadEndpoints {
        +MapThreadEndpoints(endpoints)
    }

    class McpServer {
        +HandleRequestAsync(request, user, ct) Task
        +GetServerInfo() object
    }

    class ToolsHandler {
        +GetTools()
        +HandleCallAsync(parameters, id, username, userId, ct) Task
    }

    class McpJsonRpc {
        +JsonOptions
        +CreateResult(id, result)
        +CreateTextResult(id, text, isError)
        +CreateToolError(id, message)
        +CreateStructuredResult(id, text, structuredContent)
        +CreateError(id, code, message)
    }

    class AuthService {
        +GetUsername(user) string
        +GetEmail(user) string
        +GetMattermostLogin(user) string
        +HasRole(user, role) bool
    }

    class OAuthMetadata {
        +Resolve(configuration) OAuthMetadataOptions
        +BuildJson(o) string
    }

    class ThreadSearchService {
        +ResolveUserIdAsync(login, email, ct) Task
        +GetFollowedThreadsAsync(userId, limit, page, ct) Task
        +SearchFollowedThreadsAsync(userId, request, ct) Task
        +GetThreadPostsAsync(threadId, ct) Task
        +BuildThreadUrl(threadId) string
    }

    class MattermostClient {
        +GetMeAsync(ct) Task
        +FindUserByNameAsync(username, ct) Task
        +FindUserByEmailAsync(email, ct) Task
        +FindChannelByIdAsync(channelId, ct) Task
        +GetUserTeamsAsync(userId, ct) Task
        +GetUserThreadsAsync(userId, limit, teamId, page, ct) Task
        +GetThreadPostsAsync(threadId, ct) Task
    }

    class MattermostAuthHandler {
        #SendAsync(request, ct) Task
    }

    class MattermostTokenProvider {
        +AuthClientName
        +GetTokenAsync(ct) Task
    }

    Program ..> Startup : creates
    Startup ..> McpServer : registers (scoped)
    Startup ..> MattermostClient : AddHttpClient
    Startup ..> MattermostTokenProvider : singleton

    McpEndpoints ..> McpServer : invokes
    ThreadEndpoints ..> ThreadSearchService : invokes

    McpServer o-- ToolsHandler
    McpServer o-- ThreadSearchService
    McpServer ..> AuthService : claims
    McpServer ..> McpJsonRpc : builds JSON-RPC

    ToolsHandler o-- ThreadSearchService
    ToolsHandler ..> McpJsonRpc : builds results
    ThreadEndpoints ..> AuthService : claims

    ThreadSearchService o-- MattermostClient
    ThreadSearchService o-- MattermostTokenProvider

    MattermostClient ..> MattermostAuthHandler : HttpClient pipeline
    MattermostAuthHandler o-- MattermostTokenProvider

    McpEndpoints ..> OAuthMetadata : discovery
```

## Модели и контракты (`MmDesktop/Models`, `ThreadSearchContracts`)

```mermaid
classDiagram
    direction LR

    class IIdentifiable {
        <<interface>>
        +string Id
    }

    class MattermostUser {
        +string Id
        +string Username
        +string Email
    }

    class MattermostTeam {
        +string Id
        +string Name
        +string DisplayName
    }

    class MattermostPost {
        +string Id
        +string ChannelId
        +string UserId
        +string Message
        +long CreateAt
        +string RootId
        +DateTimeOffset CreatedAt
    }

    class MattermostChannel {
        +string Id
        +string Name
        +string DisplayName
    }

    class MattermostPostThread {
        +List Order
        +Dictionary Posts
    }

    class MattermostThread {
        +string Id
        +MattermostPost Post
        +int UnreadReplies
    }

    class MattermostThreadsResponse {
        +int Total
        +int TotalUnreadThreads
        +IReadOnlyList Threads
    }

    class UserThread {
        +string Id
        +MattermostPost Post
        +string ChannelId
        +int UnreadReplies
        +DateTimeOffset LastReplyAt
    }

    class UserThreadsResponse {
        +List Threads
        +int Total
        +int TotalUnread
        +int TotalUnreadThreads
    }

    class ThreadSearchRequest {
        +string Query
        +int Limit
        +bool SearchTitleOnly
    }

    class ThreadSearchResult {
        +string ThreadId
        +string ChannelName
        +string ChannelId
        +string RootMessage
        +string MatchedMessage
        +int ReplyCount
        +DateTimeOffset CreatedAt
        +DateTimeOffset LastReplyAt
        +string MattermostUrl
    }

    MattermostUser ..|> IIdentifiable
    MattermostTeam ..|> IIdentifiable

    MattermostThreadsResponse "1" *-- "*" MattermostThread : threads
    MattermostThread "1" *-- "1" MattermostPost : post
    MattermostPostThread "1" *-- "*" MattermostPost : posts
    UserThreadsResponse "1" *-- "*" UserThread : threads
    UserThread "1" o-- "0..1" MattermostPost : post
```

## Конфигурация (Options)

```mermaid
classDiagram
    direction LR

    class AuthOptions {
        +Authority
        +MetadataAddress
        +Audience
        +Issuer
        +RequireHttpsMetadata
    }

    class MmDesktopOptions {
        +BaseUrl
        +Username
        +Password
        +Token
        +PerPage
        +TimeoutSeconds
        +AuthenticationOptions
    }

    class MmDesktopAuthenticationOptions {
        +AuthorityHost
        +ClientId
        +Scope
    }

    class OAuthMetadataOptions {
        +Issuer
        +AuthorizationEndpoint
        +TokenEndpoint
        +JwksUri
        +RegistrationEndpoint
        +ClientId
        +Scopes
    }

    MmDesktopOptions *-- MmDesktopAuthenticationOptions : AuthenticationOptions
```

## Примечания

- Связь `MattermostClient ..> MattermostAuthHandler` отражает реальную инъекцию
  обработчика через `AddHttpMessageHandler` в `Startup.ConfigureServices`.
- Вложенные generic-типы (`Task<IReadOnlyList<T>>`) в Mermaid намеренно не
  раскрыты: члены коллекций показаны без параметров типа, чтобы не полагаться на
  поддержку вложенных generic в конкретной версии рендерера.
- Приватные `ThreadsWire` / `ThreadWire` внутри `MattermostClient` в диаграмму
  не вынесены — они деталь реализации wire-контракта.
- Проверка синтаксиса: рендеринг Mermaid в этом окружении недоступен (нет
  `mmdc`/Chrome), диаграммы следуют синтаксису Mermaid v10+ для classDiagram.
