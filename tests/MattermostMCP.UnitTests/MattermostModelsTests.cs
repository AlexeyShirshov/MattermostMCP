using System.Text.Json;
using FluentAssertions;
using MattermostMCP.MmDesktop.Models;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class MattermostModelsTests
{
    [Fact]
    public void ThreadsResponse_DeserializesThreads()
    {
        const string json = """
        {
          "total": 1,
          "total_unread_threads": 3,
          "threads": [
            {
              "id": "thread-1",
              "unread_replies": 3,
              "post": {
                "id": "post-1",
                "channel_id": "channel-1",
                "user_id": "user-1",
                "message": "hello",
                "create_at": 1700000000000,
                "root_id": ""
              }
            }
          ]
        }
        """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = JsonSerializer.Deserialize<MattermostThreadsResponse>(json, options);

        response.Should().NotBeNull();
        response!.Total.Should().Be(1);
        response.TotalUnreadThreads.Should().Be(3);
        response.Threads.Should().ContainSingle();
        response.Threads[0].Post.Message.Should().Be("hello");
        response.Threads[0].UnreadReplies.Should().Be(3);
    }
}
