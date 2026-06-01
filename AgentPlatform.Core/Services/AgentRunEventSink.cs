using AgentPlatform.Core.Runtime;

namespace AgentPlatform.Core.Services;

public sealed class AgentRunEventSink : IAgentRunEventSink
{
    private readonly object _gate = new();
    private readonly Queue<AgentLogicEvent> _events = new();
    private int _activeRuns;

    public ValueTask EmitAsync(AgentLogicEvent streamEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_activeRuns == 0)
            {
                throw new InvalidOperationException("Agent run events can only be emitted during an active agent run.");
            }

            _events.Enqueue(streamEvent);
        }

        return ValueTask.CompletedTask;
    }

    internal IDisposable BeginRun()
    {
        lock (_gate)
        {
            _activeRuns++;
        }

        return new ActiveRun(this);
    }

    internal IReadOnlyList<AgentLogicEvent> Drain()
    {
        lock (_gate)
        {
            if (_events.Count == 0)
            {
                return [];
            }

            var drained = _events.ToList();
            _events.Clear();
            return drained;
        }
    }

    private void EndRun()
    {
        lock (_gate)
        {
            _activeRuns = Math.Max(0, _activeRuns - 1);
            if (_activeRuns == 0)
            {
                _events.Clear();
            }
        }
    }

    private sealed class ActiveRun(AgentRunEventSink sink) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sink.EndRun();
        }
    }
}
