using System.Text.Json;
using FluentAssertions;
using MattermostMCP.Handlers;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class McpJsonRpcTests
{
    [Fact]
    public void CreateResult_WrapsResultAndEchoesId()
    {
        using var doc = McpJsonRpc.CreateResult(7, new { value = 42 });

        doc.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        doc.RootElement.GetProperty("id").GetInt32().Should().Be(7);
        doc.RootElement.GetProperty("result").GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public void CreateResult_WithStringId_EchoesStringId()
    {
        using var doc = McpJsonRpc.CreateResult("abc", new { ok = true });

        doc.RootElement.GetProperty("id").GetString().Should().Be("abc");
    }

    [Fact]
    public void CreateTextResult_WithoutError_HasContentOnly()
    {
        using var doc = McpJsonRpc.CreateTextResult(1, "hello");

        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("text");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("hello");
        result.TryGetProperty("isError", out _).Should().BeFalse();
    }

    [Fact]
    public void CreateTextResult_WithError_SetsIsError()
    {
        using var doc = McpJsonRpc.CreateTextResult(1, "boom", isError: true);

        doc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CreateToolError_ReturnsIsErrorResult()
    {
        using var doc = McpJsonRpc.CreateToolError(null, "failed");

        doc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString().Should().Be("failed");
    }

    [Fact]
    public void CreateStructuredResult_IncludesStructuredContent()
    {
        using var doc = McpJsonRpc.CreateStructuredResult(2, "text", new { total = 3 });

        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("text");
        result.GetProperty("structuredContent").GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public void CreateError_IncludesCodeAndMessage()
    {
        using var doc = McpJsonRpc.CreateError(5, -32601, "Method not found");

        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(-32601);
        error.GetProperty("message").GetString().Should().Be("Method not found");
    }

    [Fact]
    public void JsonOptions_UsesCamelCaseAndIgnoresNulls()
    {
        McpJsonRpc.JsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        McpJsonRpc.JsonOptions.DefaultIgnoreCondition
            .Should().Be(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
    }

    [Fact]
    public void McpTool_DefaultsAreUsable()
    {
        var tool = new McpJsonRpc.McpTool();

        tool.Name.Should().BeEmpty();
        tool.Description.Should().BeEmpty();
        tool.InputSchema.Should().NotBeNull();
        tool.OutputSchema.Should().BeNull();
        tool.Annotations.Should().BeNull();
    }

    [Fact]
    public void McpResource_DefaultsMimeType()
    {
        var resource = new McpJsonRpc.McpResource
        {
            Uri = "mcp://x",
            Name = "x"
        };

        resource.MimeType.Should().Be("text/plain");
        resource.Description.Should().BeNull();
    }
}
