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

public interface IThinkingPolicyResolver
{
    ThinkingCapabilitiesDto GetCapabilities();

    ThinkingPolicyDto GetDefaultPolicy();

    ThinkingPolicyDto Resolve(
        ThinkingPolicyDto? sessionPolicy,
        ThinkingPolicyDto? agentPolicy,
        ThinkingPolicyDto? modelPolicy);
}

public interface IModelCatalog
{
    IReadOnlyList<ModelDefinitionDto> ListModels();

    ResolvedModel Resolve(string? modelId);
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
        ThinkingPolicyDto thinkingPolicy,
        ResolvedModel model,
        CancellationToken cancellationToken);

    Task AddMessageAsync(string sessionId, string role, string content, CancellationToken cancellationToken);

    Task AddRunEventAsync(string sessionId, string eventName, object payload, CancellationToken cancellationToken);

    Task UpdateSessionConfigurationAsync(
        string sessionId,
        string configHash,
        ThinkingPolicyDto thinkingPolicy,
        ResolvedModel model,
        CancellationToken cancellationToken);

    Task UpdateSessionAfterRunAsync(
        string sessionId,
        string preview,
        string? serializedSessionState,
        string? compactedPromptSnapshotJson,
        ContextCompactionStatsDto? compactionStats,
        CancellationToken cancellationToken);
}

public interface IReasoningTraceStore
{
    Task<IReadOnlyList<ReasoningTraceDto>> GetForSessionAsync(
        string sessionId,
        string? compatibilityGroup,
        int limit,
        CancellationToken cancellationToken);

    Task SaveAsync(ReasoningTraceWriteDto trace, CancellationToken cancellationToken);

    Task PruneOrCompactAsync(string sessionId, ThinkingPolicyDto policy, CancellationToken cancellationToken);
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
    ContextCompactionStatsDto? LastCompactionStats,
    ThinkingPolicyDto ThinkingPolicy,
    string ThinkingMode,
    string ThinkingCapture,
    int ReasoningTraceCount,
    int LastReasoningTokenEstimate,
    ResolvedModel Model);
