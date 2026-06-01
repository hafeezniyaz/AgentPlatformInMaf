using AgentPlatform.Core.Models;

namespace AgentPlatform.Core.Services;

internal sealed class EmptyAgentSkillCatalog : IAgentSkillCatalog
{
    public Task<IReadOnlyList<CatalogItemDto>> ListSkillsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogItemDto>>([]);
}
