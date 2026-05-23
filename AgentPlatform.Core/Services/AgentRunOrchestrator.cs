using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;

namespace AgentPlatform.Core.Services;

public sealed class AgentRunOrchestrator(
    IAgentCatalogService catalogService,
    IStaticCatalog staticCatalog,
    IConversationStore conversationStore,
    IAgentRuntime runtime,
    IContextPolicyResolver contextPolicyResolver)
{
    public async IAsyncEnumerable<RunStreamEvent> StreamAsync(
        StreamRunRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = await catalogService.GetAgentAsync(request.AgentId, cancellationToken)
            ?? throw new AgentPlatformValidationException($"Unknown agent '{request.AgentId}'.");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new AgentPlatformValidationException("Message is required.");
        }

        var toolIds = NormalizeSelection(request.ToolIds, agent.ToolIds);
        var middlewareIds = NormalizeSelection(request.MiddlewareIds, agent.MiddlewareIds);
        var skillIds = NormalizeSkillSelection(request.SkillIds, agent.SkillIds, staticCatalog.Skills.Select(item => item.Id));
        toolIds = ExpandToolsForSkills(toolIds, skillIds, staticCatalog.Skills);

        ValidateKnown("tool", toolIds, staticCatalog.Tools.Select(item => item.Id));
        ValidateKnown("middleware", middlewareIds, staticCatalog.Middleware.Select(item => item.Id));
        ValidateKnown("skill", skillIds, staticCatalog.Skills.Select(item => item.Id));
        ValidateAllowed("tool", toolIds, agent.AllowedToolIds);
        ValidateAllowed("middleware", middlewareIds, agent.AllowedMiddlewareIds);
        ValidateAllowed("skill", skillIds, agent.AllowedSkillIds);

        var model = string.IsNullOrWhiteSpace(request.Model) ? agent.Model : request.Model!;
        var instructions = BuildInstructions(agent.Instructions, middlewareIds);
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString("n") : request.SessionId!;
        var storedSession = await conversationStore.GetStoredSessionAsync(sessionId, cancellationToken);
        var resolvedContextPolicy = storedSession?.ContextPolicy ?? contextPolicyResolver.Resolve(agent.ContextPolicy);
        var configHash = ComputeConfigHash(agent.Id, model, toolIds, middlewareIds, skillIds, resolvedContextPolicy);

        if (storedSession is null)
        {
            storedSession = await conversationStore.CreateSessionAsync(
                sessionId,
                agent.Id,
                agent.Name,
                CreateTitle(request.Message),
                configHash,
                toolIds,
                middlewareIds,
                skillIds,
                resolvedContextPolicy,
                cancellationToken);
        }
        else if (!string.Equals(storedSession.ConfigHash, configHash, StringComparison.Ordinal))
        {
            throw new AgentPlatformValidationException("The session was created with a different agent configuration and cannot be restored for this run.");
        }

        var runSpec = new AgentRunSpec(
            sessionId,
            agent,
            model,
            instructions,
            toolIds,
            middlewareIds,
            skillIds,
            resolvedContextPolicy,
            request.Message,
            configHash);
        var assistantMessage = new StringBuilder();

        var started = new RunStreamEvent(
            "run.started",
            sessionId,
            new RunStartedPayload(agent.Id, model, toolIds, middlewareIds, skillIds, resolvedContextPolicy),
            DateTimeOffset.UtcNow);
        await conversationStore.AddRunEventAsync(sessionId, started.Event, started.Data, cancellationToken);
        yield return started;

        string? serializedSessionState = storedSession.SerializedSessionState;

        await foreach (var runtimeEvent in runtime.StreamAsync(runSpec, storedSession.SerializedSessionState, cancellationToken))
        {
            if (runtimeEvent.Event == "text.delta" && runtimeEvent.Data is TextDeltaPayload delta)
            {
                assistantMessage.Append(delta.Text);
            }

            if (!string.IsNullOrWhiteSpace(runtimeEvent.SerializedSessionState))
            {
                serializedSessionState = runtimeEvent.SerializedSessionState;
            }

            var outgoing = new RunStreamEvent(runtimeEvent.Event, sessionId, runtimeEvent.Data, DateTimeOffset.UtcNow);
            await conversationStore.AddRunEventAsync(sessionId, outgoing.Event, outgoing.Data, cancellationToken);
            yield return outgoing;
        }

        var finalText = assistantMessage.ToString();
        await conversationStore.AddMessageAsync(sessionId, "user", request.Message, cancellationToken);
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            await conversationStore.AddMessageAsync(sessionId, "assistant", finalText, cancellationToken);
        }

        await conversationStore.UpdateSessionAfterRunAsync(
            sessionId,
            CreatePreview(finalText),
            serializedSessionState,
            compactedPromptSnapshotJson: null,
            compactionStats: null,
            cancellationToken);
    }

    private static IReadOnlyList<string> NormalizeSelection(IReadOnlyList<string>? requested, IReadOnlyList<string> defaults)
        => (requested is { Count: > 0 } ? requested : defaults)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> NormalizeSkillSelection(
        IReadOnlyList<string>? requested,
        IReadOnlyList<string> defaults,
        IEnumerable<string> allSkillIds)
    {
        var selected = NormalizeSelection(requested, defaults);
        return selected.Contains("*", StringComparer.OrdinalIgnoreCase)
            ? allSkillIds.Order(StringComparer.OrdinalIgnoreCase).ToList()
            : selected;
    }

    private static IReadOnlyList<string> ExpandToolsForSkills(
        IReadOnlyList<string> selectedToolIds,
        IReadOnlyList<string> selectedSkillIds,
        IReadOnlyList<CatalogItemDto> skills)
    {
        var expanded = selectedToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills.Where(skill => selectedSkillIds.Contains(skill.Id, StringComparer.OrdinalIgnoreCase)))
        {
            if (skill.Metadata?.TryGetValue("requiredTools", out var requiredTools) is true)
            {
                foreach (var toolId in requiredTools.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    expanded.Add(toolId);
                }
            }
        }

        return expanded.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ValidateKnown(string type, IReadOnlyList<string> selected, IEnumerable<string> known)
    {
        var knownSet = known.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = selected.Where(id => !knownSet.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new AgentPlatformValidationException($"Unknown {type} id(s): {string.Join(", ", unknown)}.");
        }
    }

    private static void ValidateAllowed(string type, IReadOnlyList<string> selected, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return;
        }

        var allowedSet = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unsupported = selected.Where(id => !allowedSet.Contains(id)).ToList();
        if (unsupported.Count > 0)
        {
            throw new AgentPlatformValidationException($"Unsupported {type} id(s) for this agent: {string.Join(", ", unsupported)}.");
        }
    }

    private static string BuildInstructions(string baseInstructions, IReadOnlyList<string> middlewareIds)
    {
        if (!middlewareIds.Contains("safety", StringComparer.OrdinalIgnoreCase))
        {
            return baseInstructions;
        }

        return baseInstructions + Environment.NewLine + Environment.NewLine +
            "Safety middleware: refuse requests that ask for harmful, illegal, credential-stealing, or privacy-invasive actions. Explain the refusal briefly and offer a safe alternative.";
    }

    public static string ComputeConfigHash(
        string agentId,
        string model,
        IReadOnlyList<string> toolIds,
        IReadOnlyList<string> middlewareIds,
        IReadOnlyList<string> skillIds,
        ContextPolicyDto contextPolicy)
    {
        var payload = JsonSerializer.Serialize(new
        {
            agentId,
            model,
            tools = toolIds.Order(StringComparer.OrdinalIgnoreCase),
            middleware = middlewareIds.Order(StringComparer.OrdinalIgnoreCase),
            skills = skillIds.Order(StringComparer.OrdinalIgnoreCase),
            context = contextPolicy
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string CreateTitle(string message)
        => CreatePreview(message, 80);

    private static string CreatePreview(string message, int maxLength = 160)
    {
        var trimmed = message.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public sealed class AgentPlatformValidationException(string message) : Exception(message);
