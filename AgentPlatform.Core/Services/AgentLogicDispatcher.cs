using AgentPlatform.Core.Runtime;

namespace AgentPlatform.Core.Services;

public sealed class AgentLogicDispatcher(
    IEnumerable<ICodeAgentLogic> codeAgentLogics,
    IAgentRuntime runtime,
    AgentRunEventSink eventSink) : IAgentLogicDispatcher
{
    public async IAsyncEnumerable<AgentLogicEvent> StreamAsync(
        AgentLogicContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _ = eventSink.BeginRun();
        var stream = ResolveStream(context, cancellationToken);

        await foreach (var streamEvent in stream.WithCancellation(cancellationToken))
        {
            foreach (var pendingEvent in eventSink.Drain())
            {
                yield return pendingEvent;
            }

            yield return streamEvent;
        }

        foreach (var pendingEvent in eventSink.Drain())
        {
            yield return pendingEvent;
        }
    }

    private IAsyncEnumerable<AgentLogicEvent> ResolveStream(
        AgentLogicContext context,
        CancellationToken cancellationToken)
    {
        var logic = string.Equals(context.Run.Agent.Source, "code", StringComparison.OrdinalIgnoreCase)
            ? codeAgentLogics.FirstOrDefault(candidate =>
                string.Equals(candidate.AgentId, context.Run.Agent.Id, StringComparison.OrdinalIgnoreCase))
            : null;

        return logic is null
            ? StreamRuntimeAsync(context, cancellationToken)
            : logic.StreamAsync(context, cancellationToken);
    }

    private async IAsyncEnumerable<AgentLogicEvent> StreamRuntimeAsync(
        AgentLogicContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var runtimeEvent in runtime.StreamAsync(
            context.Run,
            context.SerializedRuntimeSessionState,
            cancellationToken))
        {
            yield return AgentLogicEvent.FromRuntime(runtimeEvent);
        }
    }
}
