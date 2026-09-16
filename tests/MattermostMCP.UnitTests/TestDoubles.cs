using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MattermostMCP.UnitTests;

/// <summary>
/// HTTP-хендлер с настраиваемым ответом; пишет все запросы для последующих проверок.
/// </summary>
internal sealed class RoutingHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public RoutingHttpMessageHandler(HttpResponseMessage response)
        : this(_ => response)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}

/// <summary>
/// Фабрики тестовых зависимостей на базе NSubstitute.
/// </summary>
internal static class TestSubstitutes
{
    public static IHttpClientFactory HttpClientFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://test.local/")
            });
        return factory;
    }
}

internal static class TestHttp
{
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage Status(HttpStatusCode status)
        => new(status);

    public static HttpResponseMessage LoginWithToken(string token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.Add("Token", token);
        return response;
    }
}

internal static class TestConfig
{
    public static IConfiguration Create(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();
}

internal static class TestJson
{
    public static JsonElement Element(string json)
        => JsonDocument.Parse(json).RootElement;

    public static JsonElement Empty => Element("{}");
}

internal static class TestLoggers
{
    public static Microsoft.Extensions.Logging.ILogger<T> For<T>()
        => NullLogger<T>.Instance;

    public static Microsoft.Extensions.Logging.ILoggerFactory Factory()
        => NullLoggerFactory.Instance;
}

/// <summary>
/// Простой маршрутизатор Mattermost API: первое совпадение фрагмента пути отдаёт ответ.
/// </summary>
internal sealed class MmApiRouter
{
    private readonly List<(string Fragment, Func<HttpRequestMessage, HttpResponseMessage> Response)>
        _routes = [];

    public MmApiRouter Add(
        string pathFragment, Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        _routes.Add((pathFragment, response));
        return this;
    }

    public MmApiRouter AddJson(string pathFragment, string json)
        => Add(pathFragment, _ => TestHttp.Json(json));

    public MmApiRouter AddStatus(string pathFragment, HttpStatusCode status)
        => Add(pathFragment, _ => TestHttp.Status(status));

    public MmApiRouter AddPosts(string threadId, params (string Id, string Message)[] posts)
        => AddJson($"posts/{threadId}/thread", MmFixtures.PostThread(threadId, posts));

    public MmApiRouter AddChannel(
        string channelId,
        string displayName = "Test Channel",
        string name = "channel")
        => AddJson($"channels/{channelId}", MmFixtures.Channel(channelId, name, displayName));

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        foreach (var (fragment, response) in _routes)
        {
            if (path.Contains(fragment, StringComparison.Ordinal))
            {
                return response(request);
            }
        }

        return TestHttp.Status(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Заготовки JSON-ответов Mattermost REST API.
/// </summary>
internal static class MmFixtures
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string User(
        string id,
        string username = "testuser",
        string? email = "testuser@example.com")
        => JsonSerializer.Serialize(new { id, username, email }, Options);

    public static string Team(string id, string name = "test-team")
        => JsonSerializer.Serialize(new { id, name, display_name = name }, Options);

    public static string Channel(
        string id,
        string name = "test-channel",
        string displayName = "Test Channel")
        => JsonSerializer.Serialize(
            new { id, name, display_name = displayName }, Options);

    public static string Threads(
        string threadId,
        string message,
        long createAt = 1700000000000,
        int unread = 0)
        => JsonSerializer.Serialize(new
        {
            total = 1,
            total_unread_threads = unread,
            threads = new[]
            {
                new
                {
                    id = threadId,
                    unread_replies = unread,
                    last_reply_at = createAt,
                    post = new
                    {
                        id = $"post-{threadId}",
                        channel_id = "channel-1",
                        user_id = "user-1",
                        message,
                        create_at = createAt,
                        root_id = ""
                    }
                }
            }
        }, Options);

    public const string EmptyThreads =
        """{ "total": 0, "total_unread_threads": 0, "threads": [] }""";

    public static string PostThread(string threadId, params (string Id, string Message)[] posts)
    {
        var order = posts.Select(static p => p.Id).ToArray();
        var dict = posts.ToDictionary(
            static p => p.Id,
            static p => new
            {
                id = p.Id,
                channel_id = "channel-1",
                user_id = "user-1",
                message = p.Message,
                create_at = 1700000000000L,
                root_id = ""
            });
        _ = threadId;
        return JsonSerializer.Serialize(new { order, posts = dict }, Options);
    }
}
