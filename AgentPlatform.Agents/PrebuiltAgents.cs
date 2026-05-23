using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Agents;

public sealed class GeneralAssistantAgent : IPrebuiltAgentDefinition
{
    public AgentDefinitionDto Descriptor { get; } = new(
        Id: "general-assistant",
        Name: "General Assistant",
        Description: "A balanced assistant for everyday chat, planning, and tool-assisted answers.",
        Source: "code",
        Instructions: "You are a helpful agentic assistant. Be concise, accurate, and clear. Use tools only when they materially improve the answer.",
        Model: "gpt-4o-mini",
        ToolIds: ["clock", "calculator"],
        MiddlewareIds: ["logging", "safety", "timing"],
        SkillIds: [],
        AllowedToolIds: ["clock", "calculator", "weather"],
        AllowedMiddlewareIds: ["logging", "safety", "timing"],
        AllowedSkillIds: [],
        ContextPolicy: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);
}

public sealed class AgentBuilderAgent : IPrebuiltAgentDefinition
{
    public AgentDefinitionDto Descriptor { get; } = new(
        Id: "agent-builder",
        Name: "Agent Builder",
        Description: "Helps users design focused agents, tool sets, middleware policies, and skills.",
        Source: "code",
        Instructions: "You help users design agents. Ask for goals, responsibilities, tools, safety boundaries, and success criteria. Return implementable agent configurations.",
        Model: "gpt-4o-mini",
        ToolIds: ["clock"],
        MiddlewareIds: ["logging", "safety"],
        SkillIds: [],
        AllowedToolIds: ["clock", "calculator"],
        AllowedMiddlewareIds: ["logging", "safety", "timing"],
        AllowedSkillIds: [],
        ContextPolicy: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);
}

public sealed class DefaultStaticCatalog : IStaticCatalog
{
    public IReadOnlyList<CatalogItemDto> Tools { get; } =
    [
        new("clock", "Clock", "Returns the current UTC time.", "utility"),
        new("calculator", "Calculator", "Evaluates simple arithmetic expressions.", "utility"),
        new("weather", "Weather", "Returns a stub weather report for testing tool selection.", "demo")
    ];

    public IReadOnlyList<CatalogItemDto> Middleware { get; } =
    [
        new("logging", "Logging", "Records run events to the configured backend log.", "observability"),
        new("timing", "Timing", "Measures runtime duration and emits completion metadata.", "observability"),
        new("safety", "Safety", "Adds a safety instruction layer to the selected agent.", "policy")
    ];

    public IReadOnlyList<CatalogItemDto> Skills { get; } =
    [
        new(
            "agent-design",
            "Agent Design",
            "Guides an agent through designing focused agent roles, tool choices, middleware, and success criteria.",
            "authoring",
            new Dictionary<string, string>
            {
                ["requiredTools"] = "clock"
            })
    ];
}

public static class AgentsDependencyInjection
{
    public static IServiceCollection AddPrebuiltAgentCatalog(this IServiceCollection services)
    {
        services.AddSingleton<IPrebuiltAgentDefinition, GeneralAssistantAgent>();
        services.AddSingleton<IPrebuiltAgentDefinition, AgentBuilderAgent>();
        services.AddSingleton<IStaticCatalog, DefaultStaticCatalog>();
        return services;
    }
}
