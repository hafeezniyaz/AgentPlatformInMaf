using AgentPlatform.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentPlatformCore(this IServiceCollection services)
    {
        services.AddScoped<IAgentCatalogService, AgentCatalogService>();
        services.AddScoped<IContextPolicyResolver, ContextPolicyResolver>();
        services.AddScoped<IThinkingPolicyResolver, ThinkingPolicyResolver>();
        services.AddScoped<IModelCatalog, ModelCatalog>();
        services.AddScoped<AgentRunOrchestrator>();
        return services;
    }
}
