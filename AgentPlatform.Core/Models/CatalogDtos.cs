namespace AgentPlatform.Core.Models;

public sealed record CatalogResponse(
    IReadOnlyList<AgentDefinitionDto> Agents,
    IReadOnlyList<CatalogItemDto> Tools,
    IReadOnlyList<CatalogItemDto> Middleware,
    IReadOnlyList<CatalogItemDto> Skills,
    ContextCapabilitiesDto Context,
    ThinkingCapabilitiesDto Thinking,
    IReadOnlyList<ModelDefinitionDto> Models);

public sealed record CatalogItemDto(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyDictionary<string, string>? Metadata = null);
