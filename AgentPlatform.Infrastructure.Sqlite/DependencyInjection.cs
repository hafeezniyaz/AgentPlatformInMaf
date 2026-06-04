using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Runtime;
using AgentPlatform.Infrastructure.Sqlite.Stores;
using AgentPlatform.Infrastructure.Sqlite.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentPlatform.Infrastructure.Sqlite;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentPlatformSqlite(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentPlatform")
            ?? "Data Source=agent-platform.db";

        services.AddDbContext<AgentPlatformDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAgentPlatformDbContext>(provider => provider.GetRequiredService<AgentPlatformDbContext>());
        services.AddAgentPlatformSqliteServices();
        services.Configure<AgentPlatformOptions>(agentOptions =>
        {
            var section = configuration.GetSection("AgentPlatform");
            agentOptions.DefaultModel = section["DefaultModel"] ?? agentOptions.DefaultModel;
            agentOptions.SkillsPath = section["SkillsPath"] ?? agentOptions.SkillsPath;
            var context = section.GetSection("Context");
            agentOptions.Context.Enabled = ReadBool(context, "Enabled", agentOptions.Context.Enabled);
            agentOptions.Context.Mode = context["Mode"] ?? agentOptions.Context.Mode;
            agentOptions.Context.Profile = context["Profile"] ?? agentOptions.Context.Profile;
            agentOptions.Context.SummarizerModel = context["SummarizerModel"] ?? agentOptions.Context.SummarizerModel;
            agentOptions.Context.ToolResultTokenThreshold = ReadInt(context, "ToolResultTokenThreshold", agentOptions.Context.ToolResultTokenThreshold);
            agentOptions.Context.ToolResultMinimumPreservedGroups = ReadInt(context, "ToolResultMinimumPreservedGroups", agentOptions.Context.ToolResultMinimumPreservedGroups);
            agentOptions.Context.SummarizationTokenThreshold = ReadInt(context, "SummarizationTokenThreshold", agentOptions.Context.SummarizationTokenThreshold);
            agentOptions.Context.SummarizationMinimumPreservedGroups = ReadInt(context, "SummarizationMinimumPreservedGroups", agentOptions.Context.SummarizationMinimumPreservedGroups);
            agentOptions.Context.SlidingWindowMaxTurns = ReadInt(context, "SlidingWindowMaxTurns", agentOptions.Context.SlidingWindowMaxTurns);
            agentOptions.Context.SlidingWindowMinimumPreservedTurns = ReadInt(context, "SlidingWindowMinimumPreservedTurns", agentOptions.Context.SlidingWindowMinimumPreservedTurns);
            agentOptions.Context.TruncationTokenThreshold = ReadInt(context, "TruncationTokenThreshold", agentOptions.Context.TruncationTokenThreshold);
            agentOptions.Context.TruncationMinimumPreservedGroups = ReadInt(context, "TruncationMinimumPreservedGroups", agentOptions.Context.TruncationMinimumPreservedGroups);
            agentOptions.Context.SummarizationPrompt = context["SummarizationPrompt"] ?? agentOptions.Context.SummarizationPrompt;

            var thinking = section.GetSection("Thinking");
            agentOptions.Thinking.Enabled = ReadBool(thinking, "Enabled", agentOptions.Thinking.Enabled);
            agentOptions.Thinking.Mode = thinking["Mode"] ?? agentOptions.Thinking.Mode;
            agentOptions.Thinking.Capture = thinking["Capture"] ?? agentOptions.Thinking.Capture;
            agentOptions.Thinking.ExposeToClient = ReadBool(thinking, "ExposeToClient", agentOptions.Thinking.ExposeToClient);
            agentOptions.Thinking.MaxPreservedTokens = ReadInt(thinking, "MaxPreservedTokens", agentOptions.Thinking.MaxPreservedTokens);
            agentOptions.Thinking.RawRequestOptionsJson = ReadRawJson(thinking.GetSection("RawRequestOptionsJson"));

            agentOptions.Models.Clear();
            foreach (var modelSection in section.GetSection("Models").GetChildren())
            {
                var id = modelSection["Id"];
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                agentOptions.Models.Add(new ModelOptions
                {
                    Id = id,
                    Provider = modelSection["Provider"] ?? "openai",
                    BaseUrl = modelSection["BaseUrl"],
                    CompatibilityGroup = modelSection["CompatibilityGroup"],
                    ContextWindowTokens = int.TryParse(modelSection["ContextWindowTokens"], out var contextWindowTokens) ? contextWindowTokens : null,
                    ThinkingPolicy = ReadThinkingPolicy(modelSection.GetSection("ThinkingPolicy"))
                });
            }
        });
        return services;
    }

    public static IServiceCollection AddAgentPlatformSqliteWithDbContext<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IAgentPlatformDbContext
    {
        services.AddScoped<IAgentPlatformDbContext>(provider => provider.GetRequiredService<TDbContext>());
        services.AddAgentPlatformSqliteServices();
        return services;
    }

    public static IServiceCollection AddAgentPlatformBuiltinTools(this IServiceCollection services)
    {
        services.AddSingleton<IClockService, SystemClockService>();
        services.AddSingleton<IAgentToolDefinition, ClockToolDefinition>();

        services.AddTransient<ICalculatorService, DataTableCalculatorService>();
        services.AddTransient<IAgentToolDefinition, CalculatorToolDefinition>();

        services.AddScoped<IWeatherService, DemoWeatherService>();
        services.AddScoped<IAgentToolDefinition, WeatherToolDefinition>();

        return services;
    }

    private static IServiceCollection AddAgentPlatformSqliteServices(this IServiceCollection services)
    {
        services.AddScoped<IUserAgentStore, SqliteUserAgentStore>();
        services.AddScoped<IConversationStore, SqliteConversationStore>();
        services.AddScoped<IReasoningTraceStore, SqliteReasoningTraceStore>();
        services.AddScoped<ContextCompactionProviderFactory>();
        services.AddScoped<SqliteChatHistoryProvider>();
        services.AddScoped<IAgentSkillCatalog, FileAgentSkillCatalog>();
        services.AddScoped<IAgentRuntime, OpenAIAgentRuntime>();
        services.AddAgentPlatformBuiltinTools();
        return services;
    }

    private static int ReadInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key], out var value) ? value : fallback;

    private static bool ReadBool(IConfiguration section, string key, bool fallback)
        => bool.TryParse(section[key], out var value) ? value : fallback;

    private static ThinkingPolicyDto? ReadThinkingPolicy(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new ThinkingPolicyDto
        {
            Enabled = ReadNullableBool(section, "Enabled"),
            Mode = section["Mode"],
            Capture = section["Capture"],
            ExposeToClient = ReadNullableBool(section, "ExposeToClient"),
            MaxPreservedTokens = int.TryParse(section["MaxPreservedTokens"], out var maxTokens) ? maxTokens : null,
            RawRequestOptionsJson = ReadRawJsonElement(section.GetSection("RawRequestOptionsJson"))
        };
    }

    private static bool? ReadNullableBool(IConfiguration section, string key)
        => bool.TryParse(section[key], out var value) ? value : null;

    private static string? ReadRawJson(IConfigurationSection section)
        => ReadRawJsonElement(section) is { } element ? element.GetRawText() : null;

    private static JsonElement? ReadRawJsonElement(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        var children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            return string.IsNullOrWhiteSpace(section.Value)
                ? null
                : JsonDocument.Parse(section.Value).RootElement.Clone();
        }

        var values = children.ToDictionary(child => child.Key, child => ConvertJsonValue(child));
        return JsonSerializer.SerializeToElement(values);
    }

    private static object? ConvertJsonValue(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();
        if (children.Count > 0)
        {
            return children.ToDictionary(child => child.Key, child => ConvertJsonValue(child));
        }

        var value = section.Value;
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(value, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, out var doubleValue))
        {
            return doubleValue;
        }

        return value;
    }
}
