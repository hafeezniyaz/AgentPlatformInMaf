using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using Microsoft.Extensions.AI;

namespace AgentPlatform.Infrastructure.Sqlite.Runtime;

public sealed class VllmOpenAICompatibleChatClient : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ProtectedRequestKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model",
        "messages",
        "tools",
        "stream"
    };

    private readonly HttpClient _httpClient;
    private readonly ThinkingPolicyDto _thinkingPolicy;
    private readonly string _modelId;

    public VllmOpenAICompatibleChatClient(
        HttpClient httpClient,
        Uri endpoint,
        string modelId,
        ThinkingPolicyDto thinkingPolicy)
    {
        _httpClient = httpClient;
        _thinkingPolicy = thinkingPolicy;
        _modelId = modelId;
        Metadata = new ChatClientMetadata(nameof(VllmOpenAICompatibleChatClient), endpoint, modelId);
    }

    public ChatClientMetadata Metadata { get; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            updates.Add(update);
        }

        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(_modelId, chatMessages, options, _thinkingPolicy);
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var toolCalls = new SortedDictionary<int, ToolCallBuilder>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var responseId = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var modelId = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : _modelId;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
            {
                continue;
            }

            if (TryReadString(delta, "reasoning", out var reasoning) ||
                TryReadString(delta, "reasoning_content", out reasoning))
            {
                var update = new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(reasoning)])
                {
                    ResponseId = responseId,
                    MessageId = responseId,
                    ModelId = modelId,
                    RawRepresentation = root.Clone()
                };
                yield return update;
            }

            if (TryReadString(delta, "content", out var content))
            {
                var update = new ChatResponseUpdate(ChatRole.Assistant, content)
                {
                    ResponseId = responseId,
                    MessageId = responseId,
                    ModelId = modelId,
                    RawRepresentation = root.Clone()
                };
                yield return update;
            }

            AccumulateToolCalls(delta, toolCalls);

            if (TryReadString(choice, "finish_reason", out var finishReason) &&
                string.Equals(finishReason, "tool_calls", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var call in FlushToolCalls(toolCalls))
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, [call])
                    {
                        ResponseId = responseId,
                        MessageId = responseId,
                        ModelId = modelId,
                        RawRepresentation = root.Clone()
                    };
                }
            }
        }

        foreach (var call in FlushToolCalls(toolCalls))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [call])
            {
                MessageId = Guid.NewGuid().ToString("n"),
                ModelId = _modelId
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    public static Dictionary<string, object?> BuildRequestBody(
        string modelId,
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        ThinkingPolicyDto thinkingPolicy)
    {
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model"] = modelId,
            ["messages"] = BuildMessages(chatMessages, options),
            ["stream"] = true
        };

        if (options?.Tools is { Count: > 0 } tools)
        {
            body["tools"] = tools.OfType<AIFunctionDeclaration>().Select(ToOpenAITool).ToList();
        }

        MergeRawOptions(body, thinkingPolicy.RawRequestOptionsJson);
        return body;
    }

    private static List<Dictionary<string, object?>> BuildMessages(IEnumerable<ChatMessage> chatMessages, ChatOptions? options)
    {
        var messages = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "system",
                ["content"] = options.Instructions
            });
        }

        foreach (var message in chatMessages)
        {
            messages.Add(ToOpenAIMessage(message));
        }

        return messages;
    }

    private static Dictionary<string, object?> ToOpenAIMessage(ChatMessage message)
    {
        var role = message.Role.Value;
        var result = new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = message.Text
        };

        var reasoning = string.Concat(message.Contents.OfType<TextReasoningContent>().Select(content => content.Text));
        if (!string.IsNullOrWhiteSpace(reasoning) && string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            result["reasoning_content"] = reasoning;
        }

        var functionCalls = message.Contents.OfType<FunctionCallContent>().ToList();
        if (functionCalls.Count > 0)
        {
            result["tool_calls"] = functionCalls.Select(call => new Dictionary<string, object?>
            {
                ["id"] = call.CallId,
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = call.Name,
                    ["arguments"] = JsonSerializer.Serialize(call.Arguments, JsonOptions)
                }
            }).ToList();
        }

        var functionResult = message.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        if (functionResult is not null)
        {
            result["role"] = "tool";
            result["tool_call_id"] = functionResult.CallId;
            result["content"] = Convert.ToString(functionResult.Result, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        return result;
    }

    private static Dictionary<string, object?> ToOpenAITool(AIFunctionDeclaration function)
        => new()
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = function.Name,
                ["description"] = function.Description,
                ["parameters"] = function.JsonSchema.ValueKind == JsonValueKind.Undefined
                    ? JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()
                    : function.JsonSchema
            }
        };

    private static void MergeRawOptions(Dictionary<string, object?> body, JsonElement? rawOptions)
    {
        if (rawOptions is null)
        {
            return;
        }

        if (rawOptions.Value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentPlatformValidationException("Thinking raw request options must be a JSON object.");
        }

        foreach (var property in rawOptions.Value.EnumerateObject())
        {
            if (ProtectedRequestKeys.Contains(property.Name))
            {
                throw new AgentPlatformValidationException($"Thinking raw request options cannot override '{property.Name}'.");
            }

            body[property.Name] = property.Value.Clone();
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return value.Length > 0;
    }

    private static void AccumulateToolCalls(JsonElement delta, SortedDictionary<int, ToolCallBuilder> toolCalls)
    {
        if (!delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var call in calls.EnumerateArray())
        {
            var index = call.TryGetProperty("index", out var indexElement) ? indexElement.GetInt32() : toolCalls.Count;
            if (!toolCalls.TryGetValue(index, out var builder))
            {
                builder = new ToolCallBuilder();
                toolCalls[index] = builder;
            }

            if (TryReadString(call, "id", out var id))
            {
                builder.Id = id;
            }

            if (call.TryGetProperty("function", out var function))
            {
                if (TryReadString(function, "name", out var name))
                {
                    builder.Name = name;
                }

                if (TryReadString(function, "arguments", out var arguments))
                {
                    builder.Arguments.Append(arguments);
                }
            }
        }
    }

    private static IReadOnlyList<FunctionCallContent> FlushToolCalls(SortedDictionary<int, ToolCallBuilder> toolCalls)
    {
        var results = toolCalls.Values
            .Where(call => !string.IsNullOrWhiteSpace(call.Name))
            .Select(call => new FunctionCallContent(
                string.IsNullOrWhiteSpace(call.Id) ? Guid.NewGuid().ToString("n") : call.Id,
                call.Name,
                ParseArguments(call.Arguments.ToString())))
            .ToList();
        toolCalls.Clear();
        return results;
    }

    private static IDictionary<string, object?> ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }

        public string Name { get; set; } = "";

        public StringBuilder Arguments { get; } = new();
    }
}
