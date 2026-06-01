using AgentPlatform.Core.Services;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPlatform.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentPlatformCore(this IServiceCollection services)
    {
        services.AddScoped<IAgentCatalogService, AgentCatalogService>();
        services.AddScoped<IContextPolicyResolver, ContextPolicyResolver>();
        services.AddScoped<IThinkingPolicyResolver, ThinkingPolicyResolver>();
        services.AddScoped<IModelCatalog, ModelCatalog>();
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();
        services.TryAddScoped<IAgentSkillCatalog, EmptyAgentSkillCatalog>();
        services.AddScoped<AgentRunEventSink>();
        services.AddScoped<IAgentRunEventSink>(services => services.GetRequiredService<AgentRunEventSink>());
        services.AddScoped<IAgentLogicDispatcher, AgentLogicDispatcher>();
        services.AddScoped<AgentRunOrchestrator>();
        return services;
    }
}
