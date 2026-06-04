using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentPlatform.Core.Models;

var options = ClientOptions.Parse(args);
using var httpClient = new HttpClient { BaseAddress = options.BaseUri };
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

Console.WriteLine("Agent Platform Console");
Console.WriteLine($"API: {httpClient.BaseAddress}");
Console.WriteLine("Type /help for commands, /exit to quit.");

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    line = line.Trim();
    if (line.Length == 0)
    {
        continue;
    }

    if (line is "/exit" or "exit" or "quit" or "/quit")
    {
        break;
    }

    try
    {
        await DispatchAsync(line, httpClient, jsonOptions);
    }
    catch (HttpRequestException ex)
    {
        WriteError($"HTTP error: {ex.Message}");
    }
    catch (JsonException ex)
    {
        WriteError($"JSON error: {ex.Message}");
    }
    catch (OperationCanceledException)
    {
        WriteError("Request cancelled.");
    }
    catch (Exception ex)
    {
        WriteError(ex.Message);
    }
}

static async Task DispatchAsync(string line, HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    var tokens = CommandTokenizer.Tokenize(line);
    if (tokens.Count == 0)
    {
        return;
    }

    var command = tokens[0];
    var args = tokens.Skip(1).ToList();
    if (!command.StartsWith('/'))
    {
        WriteError("Commands must start with '/'. Try /help.");
        return;
    }

    var route = command.Trim('/');
    var routeParts = route.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var values = KeyValueArgs.Parse(args);

    if (routeParts.Length == 0 || routeParts[0] is "help" or "?")
    {
        PrintHelp();
        return;
    }

    switch (routeParts[0])
    {
        case "catalog":
            await PrintCatalogAsync(httpClient, jsonOptions);
            break;
        case "agents":
            await HandleAgentsAsync(routeParts, values, httpClient, jsonOptions);
            break;
        case "tools":
            await PrintCatalogItemsAsync("Tools", catalog => catalog.Tools, httpClient, jsonOptions);
            break;
        case "middleware":
            await PrintCatalogItemsAsync("Middleware", catalog => catalog.Middleware, httpClient, jsonOptions);
            break;
        case "skills":
            await PrintCatalogItemsAsync("Skills", catalog => catalog.Skills, httpClient, jsonOptions);
            break;
        case "models":
            await PrintModelsAsync(httpClient, jsonOptions);
            break;
        case "context":
            await PrintContextAsync(httpClient, jsonOptions);
            break;
        case "thinking":
            await PrintThinkingAsync(httpClient, jsonOptions);
            break;
        case "sessions":
            await HandleSessionsAsync(routeParts, values, httpClient, jsonOptions);
            break;
        case "run":
        case "runs":
            await RunStreamAsync(values, httpClient, jsonOptions);
            break;
        case "raw":
            await RawAsync(args, httpClient, jsonOptions);
            break;
        default:
            WriteError($"Unknown command '/{routeParts[0]}'. Try /help.");
            break;
    }
}

static async Task HandleAgentsAsync(
    string[] routeParts,
    IReadOnlyDictionary<string, string> values,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    if (routeParts.Length == 1)
    {
        var catalog = await GetCatalogAsync(httpClient);
        PrintAgents(catalog.Agents);
        return;
    }

    if (routeParts is ["agents", "create"])
    {
        var request = await ReadAgentRequestAsync(null, values);
        await SendJsonAsync(HttpMethod.Post, "/api/agents", request, httpClient, jsonOptions);
        return;
    }

    if (routeParts is ["agents", "update", var updateId])
    {
        var existing = await GetJsonOrNullAsync<AgentDefinitionDto>($"/api/agents/{Uri.EscapeDataString(updateId)}", httpClient);
        if (existing is null)
        {
            WriteError("Agent not found.");
            return;
        }

        var request = await ReadAgentRequestAsync(existing, values);
        await SendJsonAsync(HttpMethod.Put, $"/api/agents/{Uri.EscapeDataString(updateId)}", request, httpClient, jsonOptions);
        return;
    }

    if (routeParts.Length == 2)
    {
        await PrintEndpointJsonAsync($"/api/agents/{Uri.EscapeDataString(routeParts[1])}", httpClient, jsonOptions);
        return;
    }

    WriteError("Unsupported agent command. Try /help.");
}

static async Task HandleSessionsAsync(
    string[] routeParts,
    IReadOnlyDictionary<string, string> values,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    if (routeParts.Length == 1)
    {
        var query = new QueryBuilder()
            .Add("agentId", values.GetValueOrDefault("agentId"))
            .Add("limit", values.GetValueOrDefault("limit"))
            .Add("cursor", values.GetValueOrDefault("cursor"))
            .Build();
        await PrintEndpointJsonAsync($"/api/sessions{query}", httpClient, jsonOptions);
        return;
    }

    if (routeParts is ["sessions", var sessionId, "messages"])
    {
        var query = new QueryBuilder()
            .Add("limit", values.GetValueOrDefault("limit"))
            .Add("offset", values.GetValueOrDefault("offset"))
            .Build();
        await PrintEndpointJsonAsync($"/api/sessions/{Uri.EscapeDataString(sessionId)}/messages{query}", httpClient, jsonOptions);
        return;
    }

    if (routeParts is ["sessions", var deleteSessionId, "delete"])
    {
        var response = await httpClient.DeleteAsync($"/api/sessions/{Uri.EscapeDataString(deleteSessionId)}");
        await PrintResponseAsync(response, jsonOptions);
        return;
    }

    if (routeParts.Length == 2)
    {
        await PrintEndpointJsonAsync($"/api/sessions/{Uri.EscapeDataString(routeParts[1])}", httpClient, jsonOptions);
        return;
    }

    WriteError("Unsupported session command. Try /help.");
}

static async Task RunStreamAsync(
    IReadOnlyDictionary<string, string> values,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    if (catalog.Agents.Count == 0)
    {
        WriteError("No agents are available. Create one with /agents/create first.");
        return;
    }

    var agentId = await PromptAsync(
        "agentId",
        values.GetValueOrDefault("agentId"),
        catalog.Agents[0].Id);
    var agent = catalog.Agents.FirstOrDefault(agent => agent.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));

    var request = new StreamRunRequest(
        SessionId: EmptyToNull(await PromptAsync("sessionId", values.GetValueOrDefault("sessionId"), "")),
        AgentId: agentId,
        ToolIds: ParseCsv(values.GetValueOrDefault("toolIds")) ?? agent?.ToolIds,
        MiddlewareIds: ParseCsv(values.GetValueOrDefault("middlewareIds")) ?? agent?.MiddlewareIds,
        SkillIds: ParseCsv(values.GetValueOrDefault("skillIds")) ?? agent?.SkillIds,
        Message: await PromptRequiredAsync("message", values.GetValueOrDefault("message")),
        Model: EmptyToNull(await PromptAsync("model", values.GetValueOrDefault("model"), agent?.Model ?? "")));

    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/runs/stream")
    {
        Content = JsonContent.Create(request, options: jsonOptions)
    };

    using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
    Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
    if (!response.IsSuccessStatusCode)
    {
        await PrintResponseAsync(response, jsonOptions);
        return;
    }

    await using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);

    string? eventName = null;
    var data = new StringBuilder();
    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length == 0)
        {
            FlushSseEvent(eventName, data.ToString(), jsonOptions);
            eventName = null;
            data.Clear();
            continue;
        }

        if (line.StartsWith("event: ", StringComparison.Ordinal))
        {
            eventName = line["event: ".Length..];
        }
        else if (line.StartsWith("data: ", StringComparison.Ordinal))
        {
            if (data.Length > 0)
            {
                data.AppendLine();
            }

            data.Append(line["data: ".Length..]);
        }
    }

    FlushSseEvent(eventName, data.ToString(), jsonOptions);
}

static void FlushSseEvent(string? eventName, string data, JsonSerializerOptions jsonOptions)
{
    if (string.IsNullOrWhiteSpace(data))
    {
        return;
    }

    var parsed = JsonSerializer.Deserialize<RunStreamEvent>(data, jsonOptions);
    if (parsed is null)
    {
        Console.WriteLine(data);
        return;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[{eventName ?? parsed.Event}] session={parsed.SessionId} at={parsed.Timestamp:O}");
    Console.ResetColor();

    using var document = JsonDocument.Parse(data);
    if (!document.RootElement.TryGetProperty("data", out var payload))
    {
        Console.WriteLine(FormatJson(data, jsonOptions));
        return;
    }

    switch (parsed.Event)
    {
        case "text.delta" when payload.TryGetProperty("text", out var text):
        case "reasoning.delta" when payload.TryGetProperty("text", out text):
            Console.WriteLine(text.GetString());
            break;
        case "run.completed" when payload.TryGetProperty("assistantMessage", out var message):
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message.GetString());
            Console.ResetColor();
            break;
        case "run.error" when payload.TryGetProperty("message", out var error):
            WriteError(error.GetString() ?? "Run failed.");
            break;
        default:
            Console.WriteLine(JsonSerializer.Serialize(payload, jsonOptions));
            break;
    }
}

static async Task RawAsync(IReadOnlyList<string> args, HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    if (args.Count < 2)
    {
        WriteError("Usage: /raw GET /api/catalog OR /raw POST /api/agents {json}");
        return;
    }

    var method = new HttpMethod(args[0].ToUpperInvariant());
    var path = args[1];
    var body = args.Count > 2 ? string.Join(' ', args.Skip(2)) : null;
    using var request = new HttpRequestMessage(method, path);
    if (!string.IsNullOrWhiteSpace(body))
    {
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    using var response = await httpClient.SendAsync(request);
    await PrintResponseAsync(response, jsonOptions);
}

static async Task<CreateAgentRequest> ReadAgentRequestAsync(
    AgentDefinitionDto? existing,
    IReadOnlyDictionary<string, string> values)
{
    return new CreateAgentRequest(
        Name: await PromptRequiredAsync("name", values.GetValueOrDefault("name"), existing?.Name),
        Description: await PromptAsync("description", values.GetValueOrDefault("description"), existing?.Description ?? ""),
        Instructions: await PromptRequiredAsync("instructions", values.GetValueOrDefault("instructions"), existing?.Instructions),
        Model: EmptyToNull(await PromptAsync("model", values.GetValueOrDefault("model"), existing?.Model ?? "")),
        ToolIds: ParseCsv(await PromptAsync("toolIds", values.GetValueOrDefault("toolIds"), Csv(existing?.ToolIds))),
        MiddlewareIds: ParseCsv(await PromptAsync("middlewareIds", values.GetValueOrDefault("middlewareIds"), Csv(existing?.MiddlewareIds))),
        SkillIds: ParseCsv(await PromptAsync("skillIds", values.GetValueOrDefault("skillIds"), Csv(existing?.SkillIds))),
        ContextPolicy: BuildContextPolicy(existing?.ContextPolicy, values),
        ThinkingPolicy: BuildThinkingPolicy(existing?.ThinkingPolicy, values));
}

static ContextPolicyDto? BuildContextPolicy(ContextPolicyDto? existing, IReadOnlyDictionary<string, string> values)
{
    if (values.TryGetValue("contextJson", out var json))
    {
        return JsonSerializer.Deserialize<ContextPolicyDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    if (!values.Keys.Any(key => key.StartsWith("context", StringComparison.OrdinalIgnoreCase)))
    {
        return existing;
    }

    return new ContextPolicyDto
    {
        Enabled = GetBool(values, "contextEnabled", existing?.Enabled),
        Mode = GetString(values, "contextMode", existing?.Mode),
        Profile = GetString(values, "contextProfile", existing?.Profile),
        SummarizerModel = GetString(values, "contextSummarizerModel", existing?.SummarizerModel),
        ToolResultTokenThreshold = GetInt(values, "contextToolResultTokenThreshold", existing?.ToolResultTokenThreshold),
        ToolResultMinimumPreservedGroups = GetInt(values, "contextToolResultMinimumPreservedGroups", existing?.ToolResultMinimumPreservedGroups),
        SummarizationTokenThreshold = GetInt(values, "contextSummarizationTokenThreshold", existing?.SummarizationTokenThreshold),
        SummarizationMinimumPreservedGroups = GetInt(values, "contextSummarizationMinimumPreservedGroups", existing?.SummarizationMinimumPreservedGroups),
        SlidingWindowMaxTurns = GetInt(values, "contextSlidingWindowMaxTurns", existing?.SlidingWindowMaxTurns),
        SlidingWindowMinimumPreservedTurns = GetInt(values, "contextSlidingWindowMinimumPreservedTurns", existing?.SlidingWindowMinimumPreservedTurns),
        TruncationTokenThreshold = GetInt(values, "contextTruncationTokenThreshold", existing?.TruncationTokenThreshold),
        TruncationMinimumPreservedGroups = GetInt(values, "contextTruncationMinimumPreservedGroups", existing?.TruncationMinimumPreservedGroups),
        SummarizationPrompt = GetString(values, "contextSummarizationPrompt", existing?.SummarizationPrompt)
    };
}

static ThinkingPolicyDto? BuildThinkingPolicy(ThinkingPolicyDto? existing, IReadOnlyDictionary<string, string> values)
{
    if (values.TryGetValue("thinkingJson", out var json))
    {
        return JsonSerializer.Deserialize<ThinkingPolicyDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    if (!values.Keys.Any(key => key.StartsWith("thinking", StringComparison.OrdinalIgnoreCase)))
    {
        return existing;
    }

    return new ThinkingPolicyDto
    {
        Enabled = GetBool(values, "thinkingEnabled", existing?.Enabled),
        Mode = GetString(values, "thinkingMode", existing?.Mode),
        Capture = GetString(values, "thinkingCapture", existing?.Capture),
        ExposeToClient = GetBool(values, "thinkingExposeToClient", existing?.ExposeToClient),
        MaxPreservedTokens = GetInt(values, "thinkingMaxPreservedTokens", existing?.MaxPreservedTokens),
        RawRequestOptionsJson = GetJsonElement(values, "thinkingRawRequestOptionsJson", existing?.RawRequestOptionsJson)
    };
}

static async Task PrintCatalogAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    Console.WriteLine(JsonSerializer.Serialize(catalog, jsonOptions));
}

static async Task PrintCatalogItemsAsync(
    string title,
    Func<CatalogResponse, IReadOnlyList<CatalogItemDto>> selector,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    var items = selector(catalog);
    Console.WriteLine($"{title} ({items.Count})");
    Console.WriteLine(new string('-', title.Length + items.Count.ToString().Length + 3));

    foreach (var item in items)
    {
        Console.WriteLine($"{item.Id} | {item.Name} | {item.Category}");
        Console.WriteLine($"  {item.Description}");
        if (item.Metadata is { Count: > 0 })
        {
            Console.WriteLine($"  metadata: {JsonSerializer.Serialize(item.Metadata, jsonOptions)}");
        }
    }
}

static async Task PrintModelsAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    Console.WriteLine($"Models ({catalog.Models.Count})");
    Console.WriteLine("--------");
    foreach (var model in catalog.Models)
    {
        Console.WriteLine($"{model.Id} | provider={model.Provider} | baseUrl={model.BaseUrl ?? "default"}");
        Console.WriteLine($"  compatibility={model.CompatibilityGroup ?? "n/a"} contextWindow={model.ContextWindowTokens?.ToString() ?? "n/a"}");
        if (model.ThinkingPolicy is not null)
        {
            Console.WriteLine($"  thinking={JsonSerializer.Serialize(model.ThinkingPolicy, jsonOptions)}");
        }
    }
}

static async Task PrintContextAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    Console.WriteLine("Context capabilities");
    Console.WriteLine($"modes: {Csv(catalog.Context.SupportedModes)}");
    Console.WriteLine($"profiles: {Csv(catalog.Context.SupportedProfiles)}");
    Console.WriteLine(JsonSerializer.Serialize(catalog.Context.DefaultPolicy, jsonOptions));
}

static async Task PrintThinkingAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    var catalog = await GetCatalogAsync(httpClient);
    Console.WriteLine("Thinking capabilities");
    Console.WriteLine($"modes: {Csv(catalog.Thinking.SupportedModes)}");
    Console.WriteLine($"captures: {Csv(catalog.Thinking.SupportedCaptures)}");
    Console.WriteLine(JsonSerializer.Serialize(catalog.Thinking.DefaultPolicy, jsonOptions));
}

static void PrintAgents(IReadOnlyList<AgentDefinitionDto> agents)
{
    Console.WriteLine($"Agents ({agents.Count})");
    Console.WriteLine("--------");
    foreach (var agent in agents)
    {
        Console.WriteLine($"{agent.Id} | {agent.Name} | source={agent.Source} | model={agent.Model}");
        Console.WriteLine($"  {agent.Description}");
        Console.WriteLine($"  tools={Csv(agent.ToolIds)} middleware={Csv(agent.MiddlewareIds)} skills={Csv(agent.SkillIds)}");
    }
}

static async Task PrintEndpointJsonAsync(string endpoint, HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    using var response = await httpClient.GetAsync(endpoint);
    await PrintResponseAsync(response, jsonOptions);
}

static async Task SendJsonAsync<T>(
    HttpMethod method,
    string endpoint,
    T payload,
    HttpClient httpClient,
    JsonSerializerOptions jsonOptions)
{
    using var request = new HttpRequestMessage(method, endpoint)
    {
        Content = JsonContent.Create(payload, options: jsonOptions)
    };
    using var response = await httpClient.SendAsync(request);
    await PrintResponseAsync(response, jsonOptions);
}

static async Task PrintResponseAsync(HttpResponseMessage response, JsonSerializerOptions jsonOptions)
{
    Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
    var body = await response.Content.ReadAsStringAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return;
    }

    Console.WriteLine(response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) is true
        ? FormatJson(body, jsonOptions)
        : body);
}

static async Task<CatalogResponse> GetCatalogAsync(HttpClient httpClient)
{
    return await httpClient.GetFromJsonAsync<CatalogResponse>("/api/catalog")
        ?? throw new InvalidOperationException("The API returned an empty catalog response.");
}

static async Task<T?> GetJsonOrNullAsync<T>(string endpoint, HttpClient httpClient)
{
    using var response = await httpClient.GetAsync(endpoint);
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        return default;
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<T>();
}

static string FormatJson(string json, JsonSerializerOptions jsonOptions)
{
    using var document = JsonDocument.Parse(json);
    return JsonSerializer.Serialize(document.RootElement, jsonOptions);
}

static async Task<string> PromptRequiredAsync(string name, string? value, string? defaultValue = null)
{
    var result = await PromptAsync(name, value, defaultValue ?? "");
    if (string.IsNullOrWhiteSpace(result))
    {
        throw new InvalidOperationException($"{name} is required.");
    }

    return result;
}

static Task<string> PromptAsync(string name, string? value, string defaultValue)
{
    if (!string.IsNullOrWhiteSpace(value))
    {
        return Task.FromResult(value);
    }

    Console.Write($"{name}");
    if (!string.IsNullOrWhiteSpace(defaultValue))
    {
        Console.Write($" [{defaultValue}]");
    }

    Console.Write(": ");
    var input = Console.ReadLine();
    return Task.FromResult(string.IsNullOrWhiteSpace(input) ? defaultValue : input);
}

static IReadOnlyList<string>? ParseCsv(string? value)
{
    if (value is null)
    {
        return null;
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return [];
    }

    return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

static string Csv(IEnumerable<string>? values)
{
    return values is null ? "" : string.Join(',', values);
}

static string? EmptyToNull(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static string? GetString(IReadOnlyDictionary<string, string> values, string key, string? fallback)
{
    return values.TryGetValue(key, out var value) ? EmptyToNull(value) : fallback;
}

static bool? GetBool(IReadOnlyDictionary<string, string> values, string key, bool? fallback)
{
    if (!values.TryGetValue(key, out var value))
    {
        return fallback;
    }

    return bool.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"{key} must be true or false.");
}

static int? GetInt(IReadOnlyDictionary<string, string> values, string key, int? fallback)
{
    if (!values.TryGetValue(key, out var value))
    {
        return fallback;
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return int.TryParse(value, out var parsed)
        ? parsed
        : throw new InvalidOperationException($"{key} must be an integer.");
}

static JsonElement? GetJsonElement(IReadOnlyDictionary<string, string> values, string key, JsonElement? fallback)
{
    if (!values.TryGetValue(key, out var value))
    {
        return fallback;
    }

    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    using var document = JsonDocument.Parse(value);
    return document.RootElement.Clone();
}

static void PrintHelp()
{
    Console.WriteLine("""
    Commands
      /catalog
      /agents
      /agents/{agentId}
      /agents/create name="Planner" description="..." instructions="..." model="gpt-4.1" toolIds="web_search"
      /agents/update/{agentId} name="Planner v2"
      /tools
      /middleware
      /skills
      /models
      /context
      /thinking
      /sessions agentId={agentId} limit=20 cursor={cursor}
      /sessions/{sessionId}
      /sessions/{sessionId}/messages limit=100 offset=0
      /sessions/{sessionId}/delete
      /run agentId={agentId} message="Help me plan" sessionId={sessionId} model={model} toolIds="id1,id2" middlewareIds="id1" skillIds="id1,id2"
      /raw GET /api/catalog
      /raw POST /api/agents {"name":"Agent","description":"","instructions":"Help","model":null,"toolIds":[],"middlewareIds":[],"skillIds":[]}
      /exit

    Notes
      Omit command values to be prompted interactively.
      Agent policy fields can be passed with contextJson/thinkingJson, or with contextMode, contextProfile, thinkingMode, thinkingCapture, and related scalar keys.
      IDs in comma-separated lists should not contain spaces.
      Set the API base URL with --base-url, AGENT_PLATFORM_API_URL, or API_BASE_URL.
    """);
}

static void WriteError(string message)
{
    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(message);
    Console.ForegroundColor = originalColor;
}

internal sealed record ClientOptions(Uri BaseUri)
{
    public static ClientOptions Parse(string[] args)
    {
        var baseUrl = Environment.GetEnvironmentVariable("AGENT_PLATFORM_API_URL")
            ?? Environment.GetEnvironmentVariable("API_BASE_URL")
            ?? "http://localhost:5001";

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--base-url" or "-u")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--base-url requires a value.");
                }

                baseUrl = args[index + 1];
                index++;
            }
        }

        return new ClientOptions(new Uri(baseUrl.TrimEnd('/')));
    }
}

internal static class CommandTokenizer
{
    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';
        var escaping = false;

        foreach (var character in input)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\')
            {
                escaping = true;
                continue;
            }

            if (inQuotes)
            {
                if (character == quoteChar)
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character is '"' or '\'')
            {
                inQuotes = true;
                quoteChar = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

internal static class KeyValueArgs
{
    public static IReadOnlyDictionary<string, string> Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            var equalsIndex = arg.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            values[arg[..equalsIndex].TrimStart('-')] = arg[(equalsIndex + 1)..];
        }

        return values;
    }
}

internal sealed class QueryBuilder
{
    private readonly List<string> _parts = [];

    public QueryBuilder Add(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }

        return this;
    }

    public string Build()
    {
        return _parts.Count == 0 ? "" : $"?{string.Join('&', _parts)}";
    }
}
