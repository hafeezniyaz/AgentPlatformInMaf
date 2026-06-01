using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public IReadOnlyList<CatalogItemDto> Middleware { get; } =
    [
        new("logging", "Logging", "Records run events to the configured backend log.", "observability"),
        new("timing", "Timing", "Measures runtime duration and emits completion metadata.", "observability"),
        new("safety", "Safety", "Adds a safety instruction layer to the selected agent.", "policy")
    ];
}

public static class AgentsDependencyInjection
{
    public static IServiceCollection AddPrebuiltAgentCatalog(this IServiceCollection services)
    {
        var agentTypes = typeof(AgentsDependencyInjection).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(IPrebuiltAgentDefinition).IsAssignableFrom(type));

        foreach (var agentType in agentTypes)
        {
            services.AddSingleton(typeof(IPrebuiltAgentDefinition), agentType);
        }

        var logicTypes = typeof(AgentsDependencyInjection).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(ICodeAgentLogic).IsAssignableFrom(type));

        foreach (var logicType in logicTypes)
        {
            services.AddScoped(typeof(ICodeAgentLogic), logicType);
        }

        services.TryAddSingleton<IStaticCatalog, DefaultStaticCatalog>();
        return services;
    }
}
