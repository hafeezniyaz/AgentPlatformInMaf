using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Services;

public sealed class AgentCatalogService(
    IEnumerable<IPrebuiltAgentDefinition> prebuiltAgents,
    IUserAgentStore userAgentStore,
    IStaticCatalog staticCatalog,
    IAgentToolRegistry toolRegistry,
    IOptions<AgentPlatformOptions> options,
    IOptions<PrebuiltAgentCatalogOptions> prebuiltAgentOptions,
    IContextPolicyResolver contextPolicyResolver,
    IThinkingPolicyResolver thinkingPolicyResolver,
    IModelCatalog modelCatalog) : IAgentCatalogService
{
    private readonly IReadOnlyList<AgentDefinitionDto> _prebuiltAgents =
        BuildPrebuiltAgentDescriptors(prebuiltAgents.Select(agent => agent.Descriptor), prebuiltAgentOptions.Value);

    public async Task<CatalogResponse> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var agents = _prebuiltAgents
            .Concat(await userAgentStore.ListAsync(cancellationToken))
            .OrderBy(agent => agent.Name)
            .ToList();

        return new CatalogResponse(
            agents,
            toolRegistry.ListTools(),
            staticCatalog.Middleware,
            staticCatalog.Skills,
            contextPolicyResolver.GetCapabilities(),
            thinkingPolicyResolver.GetCapabilities(),
            modelCatalog.ListModels());
    }

    public async Task<AgentDefinitionDto?> GetAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        var prebuilt = _prebuiltAgents.FirstOrDefault(agent => string.Equals(agent.Id, agentId, StringComparison.OrdinalIgnoreCase));
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

    public static IReadOnlyList<AgentDefinitionDto> BuildPrebuiltAgentDescriptors(
        IEnumerable<AgentDefinitionDto> discoveredAgents,
        PrebuiltAgentCatalogOptions options)
    {
        var discovered = discoveredAgents.ToList();
        var duplicateDiscovered = FindDuplicateId(discovered.Select(agent => agent.Id));
        if (duplicateDiscovered is not null)
        {
            throw new InvalidOperationException($"Duplicate prebuilt agent id '{duplicateDiscovered}' was discovered.");
        }

        if (options.Agents.Any(agent => string.IsNullOrWhiteSpace(agent.Id)))
        {
            throw new InvalidOperationException("Prebuilt agent config entries must include an id.");
        }

        var configEntries = options.Agents.ToList();
        var duplicateConfig = FindDuplicateId(configEntries.Select(agent => agent.Id));
        if (duplicateConfig is not null)
        {
            throw new InvalidOperationException($"Duplicate prebuilt agent config id '{duplicateConfig}' was found.");
        }

        var discoveredIds = discovered.Select(agent => agent.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownConfig = configEntries.FirstOrDefault(agent => !discoveredIds.Contains(agent.Id));
        if (unknownConfig is not null)
        {
            throw new InvalidOperationException($"Prebuilt agent config id '{unknownConfig.Id}' does not match a discovered code agent.");
        }

        var configById = configEntries.ToDictionary(agent => agent.Id, StringComparer.OrdinalIgnoreCase);
        return discovered
            .Where(agent => !configById.TryGetValue(agent.Id, out var config) || config.Enabled is not false)
            .Select(agent => configById.TryGetValue(agent.Id, out var config)
                ? ApplyOverride(agent, config)
                : agent)
            .ToList();
    }

    private static AgentDefinitionDto ApplyOverride(AgentDefinitionDto descriptor, PrebuiltAgentConfig config)
    {
        var overridden = descriptor with
        {
            Source = "code",
            Name = string.IsNullOrWhiteSpace(config.Name) ? descriptor.Name : config.Name,
            Description = config.Description ?? descriptor.Description,
            Instructions = string.IsNullOrWhiteSpace(config.Instructions) ? descriptor.Instructions : config.Instructions,
            Model = string.IsNullOrWhiteSpace(config.Model) ? descriptor.Model : config.Model,
            ToolIds = config.ToolIds ?? descriptor.ToolIds,
            MiddlewareIds = config.MiddlewareIds ?? descriptor.MiddlewareIds,
            SkillIds = config.SkillIds ?? descriptor.SkillIds,
            AllowedToolIds = config.AllowedToolIds ?? descriptor.AllowedToolIds,
            AllowedMiddlewareIds = config.AllowedMiddlewareIds ?? descriptor.AllowedMiddlewareIds,
            AllowedSkillIds = config.AllowedSkillIds ?? descriptor.AllowedSkillIds,
            ContextPolicy = config.ContextPolicy ?? descriptor.ContextPolicy,
            ThinkingPolicy = config.ThinkingPolicy ?? descriptor.ThinkingPolicy
        };

        if (string.IsNullOrWhiteSpace(overridden.Name) || string.IsNullOrWhiteSpace(overridden.Instructions))
        {
            throw new InvalidOperationException($"Prebuilt agent '{descriptor.Id}' must have a name and instructions.");
        }

        return overridden;
    }

    private static string? FindDuplicateId(IEnumerable<string> ids)
        => ids.Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
}
