using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Services;

public sealed class AgentCatalogService(
    IEnumerable<IPrebuiltAgentDefinition> prebuiltAgents,
    IUserAgentStore userAgentStore,
    IStaticCatalog staticCatalog,
    IOptions<AgentPlatformOptions> options,
    IContextPolicyResolver contextPolicyResolver,
    IThinkingPolicyResolver thinkingPolicyResolver,
    IModelCatalog modelCatalog) : IAgentCatalogService
{
    private readonly IReadOnlyList<IPrebuiltAgentDefinition> _prebuiltAgents = prebuiltAgents.ToList();

    public async Task<CatalogResponse> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var agents = _prebuiltAgents.Select(agent => agent.Descriptor)
            .Concat(await userAgentStore.ListAsync(cancellationToken))
            .OrderBy(agent => agent.Name)
            .ToList();

        return new CatalogResponse(
            agents,
            staticCatalog.Tools,
            staticCatalog.Middleware,
            staticCatalog.Skills,
            contextPolicyResolver.GetCapabilities(),
            thinkingPolicyResolver.GetCapabilities(),
            modelCatalog.ListModels());
    }

    public async Task<AgentDefinitionDto?> GetAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        var prebuilt = _prebuiltAgents.FirstOrDefault(agent => agent.Descriptor.Id == agentId)?.Descriptor;
        return prebuilt ?? await userAgentStore.GetAsync(agentId, cancellationToken);
    }

    public Task<AgentDefinitionDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken)
    {
        contextPolicyResolver.Resolve(request.ContextPolicy);
        ThinkingPolicyResolver.ValidateOverride(request.ThinkingPolicy);
        return userAgentStore.CreateAsync(request, options.Value.DefaultModel, cancellationToken);
    }

    public Task<AgentDefinitionDto?> UpdateAgentAsync(string agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        contextPolicyResolver.Resolve(request.ContextPolicy);
        ThinkingPolicyResolver.ValidateOverride(request.ThinkingPolicy);
        return userAgentStore.UpdateAsync(agentId, request, cancellationToken);
    }
}
