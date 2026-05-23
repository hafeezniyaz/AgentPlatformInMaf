using System.Text.Json;

namespace AgentPlatform.Infrastructure.Sqlite;

internal static class JsonList
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Write(IReadOnlyList<string>? values)
        => JsonSerializer.Serialize(values ?? [], JsonOptions);

    public static IReadOnlyList<string> Read(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
}

