using AgentPlatform.Core.Models;

namespace AgentPlatform.Core.Runtime;

public sealed record AgentRunSpec(
    string SessionId,
    AgentDefinitionDto Agent,
    string Model,
    string Instructions,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> MiddlewareIds,
    IReadOnlyList<string> SkillIds,
    ContextPolicyDto ContextPolicy,
    string Message,
    string ConfigHash);

public sealed record RuntimeStreamEvent(
    string Event,
    object Data,
    string? SerializedSessionState = null);

public interface IAgentRuntime
{
    IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        AgentRunSpec run,
        string? serializedSessionState,
        CancellationToken cancellationToken);
}
