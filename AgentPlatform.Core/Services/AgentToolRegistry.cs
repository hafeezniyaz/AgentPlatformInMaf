using AgentPlatform.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPlatform.Core.Services;

public sealed class AgentToolRegistry(IServiceProvider serviceProvider) : IAgentToolRegistry
{
    public IReadOnlyList<CatalogItemDto> ListTools()
        => GetDefinitions(serviceProvider)
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tool => new CatalogItemDto(
                tool.Id,
                tool.Name,
                tool.Description,
                tool.Category,
                tool.Metadata))
            .ToList();

    public void ValidateKnown(IReadOnlyList<string> toolIds)
    {
        var definitions = GetDefinitionMap(serviceProvider);
        var unknown = toolIds.Where(id => !definitions.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new AgentPlatformValidationException($"Unknown tool id(s): {string.Join(", ", unknown)}.");
        }
    }

    public IReadOnlyList<AITool> ResolveTools(IReadOnlyList<string> toolIds, IServiceProvider services)
    {
        var definitions = GetDefinitionMap(services);
        var tools = new List<AITool>(toolIds.Count);

        foreach (var toolId in toolIds)
        {
            if (!definitions.TryGetValue(toolId, out var definition))
            {
                throw new AgentPlatformValidationException($"Unknown tool id(s): {toolId}.");
            }

            tools.Add(definition.CreateTool(services));
        }

        return tools;
    }

    private static Dictionary<string, IAgentToolDefinition> GetDefinitionMap(IServiceProvider services)
    {
        var map = new Dictionary<string, IAgentToolDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in GetDefinitions(services))
        {
            if (!map.TryAdd(definition.Id, definition))
            {
                throw new AgentPlatformValidationException($"Duplicate tool id '{definition.Id}' is registered.");
            }
        }

        return map;
    }

    private static IReadOnlyList<IAgentToolDefinition> GetDefinitions(IServiceProvider services)
        => services.GetServices<IAgentToolDefinition>().ToList();
}
