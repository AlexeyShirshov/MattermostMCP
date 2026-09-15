using System.Text.Json;
using System.Text.Json.Serialization;

namespace MattermostMCP.Handlers;

/// <summary>
/// JSON-RPC 2.0 модели для MCP протокола.
/// </summary>
public static class McpJsonRpc
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Создаёт успешный JSON-RPC ответ.
    /// </summary>
    public static JsonDocument CreateResult(object? id, object result)
    {
        var obj = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions));
    }

    /// <summary>
    /// Создаёт успешный MCP CallToolResult с текстовым content-блоком.
    /// По спецификации MCP тула обязана возвращать <c>result.content[]</c>,
    /// иначе клиенты (Claude Code) не отображают ответ как текст.
    /// При <paramref name="isError"/> = true результат помечается как ошибка
    /// исполнения тула (<c>result.isError</c>) — это не protocol-level error.
    /// </summary>
    public static JsonDocument CreateTextResult(object? id, string text, bool isError = false)
    {
        object[] content = [new { type = "text", text }];
        var result = new Dictionary<string, object?> { ["content"] = content };
        if (isError)
        {
            result["isError"] = true;
        }

        var obj = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions));
    }

    /// <summary>
    /// Создаёт MCP CallToolResult с признаком ошибки (<c>isError: true</c>).
    /// Используется для ошибок исполнения тула: неверные аргументы, недоступный
    /// внешний сервис, отсутствующие данные. Protocol-level ошибки — <see cref="CreateError"/>.
    /// </summary>
    public static JsonDocument CreateToolError(object? id, string message)
        => CreateTextResult(id, message, isError: true);

    /// <summary>
    /// Создаёт MCP CallToolResult с текстовым блоком и машиночитаемым
    /// <c>structuredContent</c> (по <c>outputSchema</c> тула).
    /// </summary>
    public static JsonDocument CreateStructuredResult(object? id, string text, object structuredContent)
    {
        object[] content = [new { type = "text", text }];
        var result = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["structuredContent"] = structuredContent
        };

        var obj = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions));
    }

    /// <summary>
    /// Создаёт JSON-RPC ошибку.
    /// </summary>
    public static JsonDocument CreateError(object? id, int code, string message)
    {
        var obj = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions));
    }

    /// <summary>
    /// Инструмент (tool) для MCP.
    /// </summary>
    public sealed record McpTool
    {
        private static readonly string[] EmptyRequired = [];

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("inputSchema")]
        public object InputSchema { get; init; } = new
        {
            type = "object",
            properties = new Dictionary<string, object>(),
            required = EmptyRequired
        };

        /// <summary>
        /// JSON Schema структурированного ответа (для <c>structuredContent</c>).
        /// </summary>
        [JsonPropertyName("outputSchema")]
        public object? OutputSchema { get; init; }

        /// <summary>
        /// Подсказки о поведении тула для клиента.
        /// </summary>
        [JsonPropertyName("annotations")]
        public McpToolAnnotations? Annotations { get; init; }
    }

    /// <summary>
    /// Аннотации тула (MCP ToolAnnotations). Это подсказки, а не гарантии безопасности.
    /// </summary>
    public sealed record McpToolAnnotations
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        /// <summary>Тул не изменяет внешнее состояние.</summary>
        [JsonPropertyName("readOnlyHint")]
        public bool ReadOnlyHint { get; init; }

        /// <summary>Тул не выполняет разрушительных изменений.</summary>
        [JsonPropertyName("destructiveHint")]
        public bool DestructiveHint { get; init; }

        /// <summary>Повторный вызов с теми же аргументами не даёт дополнительного эффекта.</summary>
        [JsonPropertyName("idempotentHint")]
        public bool IdempotentHint { get; init; }

        /// <summary>Тул взаимодействует с внешней системой.</summary>
        [JsonPropertyName("openWorldHint")]
        public bool OpenWorldHint { get; init; }
    }

    /// <summary>
    /// Ресурс (resource) для MCP.
    /// </summary>
    public sealed record McpResource
    {
        [JsonPropertyName("uri")]
        public string Uri { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; init; } = "text/plain";
    }
}
