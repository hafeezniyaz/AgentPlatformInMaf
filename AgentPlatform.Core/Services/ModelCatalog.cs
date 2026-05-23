using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Services;

public sealed class ModelCatalog(IOptions<AgentPlatformOptions> options) : IModelCatalog
{
    public IReadOnlyList<ModelDefinitionDto> ListModels()
    {
        var configured = options.Value.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(ToDto)
            .ToList();

        if (!configured.Any(model => string.Equals(model.Id, options.Value.DefaultModel, StringComparison.OrdinalIgnoreCase)))
        {
            configured.Insert(0, new ModelDefinitionDto(
                options.Value.DefaultModel,
                "openai",
                null,
                options.Value.DefaultModel,
                null,
                null));
        }

        return configured;
    }

    public ResolvedModel Resolve(string? modelId)
    {
        var id = string.IsNullOrWhiteSpace(modelId) ? options.Value.DefaultModel : modelId.Trim();
        var configured = options.Value.Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
        if (configured is null)
        {
            return new ResolvedModel(id, "openai", null, id, null, null);
        }

        var provider = string.IsNullOrWhiteSpace(configured.Provider) ? "openai" : configured.Provider.Trim();
        return new ResolvedModel(
            configured.Id,
            provider,
            configured.BaseUrl,
            string.IsNullOrWhiteSpace(configured.CompatibilityGroup) ? configured.Id : configured.CompatibilityGroup!,
            configured.ContextWindowTokens,
            configured.ThinkingPolicy);
    }

    private static ModelDefinitionDto ToDto(ModelOptions model)
        => new(
            model.Id,
            string.IsNullOrWhiteSpace(model.Provider) ? "openai" : model.Provider,
            model.BaseUrl,
            string.IsNullOrWhiteSpace(model.CompatibilityGroup) ? model.Id : model.CompatibilityGroup,
            model.ContextWindowTokens,
            model.ThinkingPolicy);
}
