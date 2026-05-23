using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Runtime;
using AgentPlatform.Infrastructure.Sqlite.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Infrastructure.Sqlite;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentPlatformSqlite(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentPlatform")
            ?? "Data Source=agent-platform.db";

        services.AddDbContext<AgentPlatformDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUserAgentStore, SqliteUserAgentStore>();
        services.AddScoped<IConversationStore, SqliteConversationStore>();
        services.AddScoped<ContextCompactionProviderFactory>();
        services.AddScoped<SqliteChatHistoryProvider>();
        services.AddScoped<IAgentRuntime, OpenAIAgentRuntime>();
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
        });
        return services;
    }

    private static int ReadInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key], out var value) ? value : fallback;

    private static bool ReadBool(IConfiguration section, string key, bool fallback)
        => bool.TryParse(section[key], out var value) ? value : fallback;
}
