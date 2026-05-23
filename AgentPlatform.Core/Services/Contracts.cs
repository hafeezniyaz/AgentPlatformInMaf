using AgentPlatform.Core.Models;

namespace AgentPlatform.Core.Services;

public interface IPrebuiltAgentDefinition
{
    AgentDefinitionDto Descriptor { get; }
}

public interface IUserAgentStore
{
    Task<IReadOnlyList<AgentDefinitionDto>> ListAsync(CancellationToken cancellationToken);

    Task<AgentDefinitionDto?> GetAsync(string agentId, CancellationToken cancellationToken);

    Task<AgentDefinitionDto> CreateAsync(CreateAgentRequest request, string defaultModel, CancellationToken cancellationToken);

    Task<AgentDefinitionDto?> UpdateAsync(string agentId, UpdateAgentRequest request, CancellationToken cancellationToken);
}

public interface IStaticCatalog
{
    IReadOnlyList<CatalogItemDto> Tools { get; }

    IReadOnlyList<CatalogItemDto> Middleware { get; }

    IReadOnlyList<CatalogItemDto> Skills { get; }
}

public interface IAgentCatalogService
{
    Task<CatalogResponse> GetCatalogAsync(CancellationToken cancellationToken);

    Task<AgentDefinitionDto?> GetAgentAsync(string agentId, CancellationToken cancellationToken);

    Task<AgentDefinitionDto> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken);

    Task<AgentDefinitionDto?> UpdateAgentAsync(string agentId, UpdateAgentRequest request, CancellationToken cancellationToken);
}

public interface IContextPolicyResolver
{
    ContextCapabilitiesDto GetCapabilities();

    ContextPolicyDto GetDefaultPolicy();

    ContextPolicyDto Resolve(ContextPolicyDto? overridePolicy);
}

public interface IConversationStore
{
    Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(
        string? agentId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken);

    Task<SessionDetailsDto?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<PagedMessagesDto?> GetMessagesAsync(string sessionId, int limit, int offset, CancellationToken cancellationToken);

    Task<bool> ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<StoredSession?> GetStoredSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<StoredSession> CreateSessionAsync(
        string sessionId,
        string agentId,
        string agentName,
        string title,
        string configHash,
        IReadOnlyList<string> toolIds,
        IReadOnlyList<string> middlewareIds,
        IReadOnlyList<string> skillIds,
        ContextPolicyDto contextPolicy,
        CancellationToken cancellationToken);

    Task AddMessageAsync(string sessionId, string role, string content, CancellationToken cancellationToken);

    Task AddRunEventAsync(string sessionId, string eventName, object payload, CancellationToken cancellationToken);

    Task UpdateSessionAfterRunAsync(
        string sessionId,
        string preview,
        string? serializedSessionState,
        string? compactedPromptSnapshotJson,
        ContextCompactionStatsDto? compactionStats,
        CancellationToken cancellationToken);
}

public sealed record StoredSession(
    string SessionId,
    string AgentId,
    string AgentName,
    string ConfigHash,
    string? SerializedSessionState,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> MiddlewareIds,
    IReadOnlyList<string> SkillIds,
    ContextPolicyDto ContextPolicy,
    string ContextMode,
    string ContextProfile,
    string? CompactedPromptSnapshotJson,
    ContextCompactionStatsDto? LastCompactionStats);
