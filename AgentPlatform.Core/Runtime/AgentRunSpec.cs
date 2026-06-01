using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using System.Text.Json;

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
    ThinkingPolicyDto ThinkingPolicy,
    ResolvedModel ResolvedModel,
    string Message,
    string ConfigHash);

public sealed record RuntimeStreamEvent(
    string Event,
    object Data,
    string? SerializedSessionState = null,
    bool ExposeToClient = true);

public enum AgentMessageTreatment
{
    FinalAnswerDelta,
    VisibleProgress,
    ConversationAppend,
    StateUpdate,
    RunEvent,
    Diagnostic
}

public sealed record AgentLogicEvent(
    string Event,
    object Data,
    AgentMessageTreatment Treatment,
    bool ExposeToClient = true,
    string? SerializedRuntimeSessionState = null)
{
    public static AgentLogicEvent FromRuntime(RuntimeStreamEvent runtimeEvent)
        => new(
            runtimeEvent.Event,
            runtimeEvent.Data,
            GetTreatment(runtimeEvent.Event),
            runtimeEvent.ExposeToClient,
            runtimeEvent.SerializedSessionState);

    private static AgentMessageTreatment GetTreatment(string eventName)
        => eventName switch
        {
            "text.delta" => AgentMessageTreatment.FinalAnswerDelta,
            "agent.progress" => AgentMessageTreatment.VisibleProgress,
            "conversation.append" => AgentMessageTreatment.ConversationAppend,
            "agent.state.update" => AgentMessageTreatment.StateUpdate,
            _ => AgentMessageTreatment.RunEvent
        };
}

public sealed record ConversationAppendRequest(string Role, string Content);

public sealed record AgentStateUpdate(
    IReadOnlyDictionary<string, JsonElement> Values,
    bool Replace = false);

public sealed record AgentLogicContext(
    AgentRunSpec Run,
    StoredSession Session,
    IReadOnlyList<ChatMessageDto> RecentMessages,
    IReadOnlyDictionary<string, JsonElement> AgentState,
    string? SerializedRuntimeSessionState);

public interface ICodeAgentLogic
{
    string AgentId { get; }

    IAsyncEnumerable<AgentLogicEvent> StreamAsync(
        AgentLogicContext context,
        CancellationToken cancellationToken);
}

public interface IAgentLogicDispatcher
{
    IAsyncEnumerable<AgentLogicEvent> StreamAsync(
        AgentLogicContext context,
        CancellationToken cancellationToken);
}

public interface IAgentRunEventSink
{
    ValueTask EmitAsync(AgentLogicEvent streamEvent, CancellationToken cancellationToken);
}

public interface IAgentRuntime
{
    IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        AgentRunSpec run,
        string? serializedSessionState,
        CancellationToken cancellationToken);
}
