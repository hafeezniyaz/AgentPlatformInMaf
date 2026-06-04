using System.Text.Json;
using AgentPlatform.Core;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;

namespace AgentPlatform.Api;

public static class AgentPlatformApiEndpointExtensions
{
    public static RouteGroupBuilder MapAgentPlatformApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api")
    {
        var api = endpoints.MapGroup(prefix);
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? ""
            : prefix.TrimEnd('/');

        api.MapGet("/catalog", async (IAgentCatalogService catalog, CancellationToken cancellationToken)
            => Results.Ok(await catalog.GetCatalogAsync(cancellationToken)));

        api.MapPost("/agents", async (
            CreateAgentRequest request,
            IAgentCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) ||
                string.IsNullOrWhiteSpace(request.Instructions))
            {
                return Results.BadRequest(new { error = "Agent name and instructions are required." });
            }

            try
            {
                var created = await catalog.CreateAgentAsync(request, cancellationToken);
                return Results.Created($"{normalizedPrefix}/agents/{created.Id}", created);
            }
            catch (AgentPlatformValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/agents/{agentId}", async (
            string agentId,
            IAgentCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            var agent = await catalog.GetAgentAsync(agentId, cancellationToken);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        api.MapPut("/agents/{agentId}", async (
            string agentId,
            UpdateAgentRequest request,
            IAgentCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await catalog.UpdateAgentAsync(agentId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (AgentPlatformValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/sessions", async (
            string? agentId,
            int? limit,
            string? cursor,
            IConversationStore store,
            CancellationToken cancellationToken) =>
        {
            var sessions = await store.ListSessionsAsync(agentId, limit ?? 50, cursor, cancellationToken);
            return Results.Ok(sessions);
        });

        api.MapGet("/sessions/{sessionId}", async (
            string sessionId,
            IConversationStore store,
            CancellationToken cancellationToken) =>
        {
            var session = await store.GetSessionAsync(sessionId, cancellationToken);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        api.MapGet("/sessions/{sessionId}/messages", async (
            string sessionId,
            int? limit,
            int? offset,
            IConversationStore store,
            CancellationToken cancellationToken) =>
        {
            var messages = await store.GetMessagesAsync(sessionId, limit ?? 100, offset ?? 0, cancellationToken);
            return messages is null ? Results.NotFound() : Results.Ok(messages);
        });

        api.MapDelete("/sessions/{sessionId}", async (
            string sessionId,
            IConversationStore store,
            CancellationToken cancellationToken) =>
        {
            var archived = await store.ArchiveSessionAsync(sessionId, cancellationToken);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/runs/stream", async (
            StreamRunRequest request,
            AgentRunOrchestrator orchestrator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.ContentType = "text/event-stream";

            try
            {
                await foreach (var streamEvent in orchestrator.StreamAsync(request, cancellationToken))
                {
                    await WriteSseAsync(httpContext.Response, streamEvent.Event, streamEvent, cancellationToken);
                }
            }
            catch (AgentPlatformValidationException ex)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await WriteSseAsync(
                    httpContext.Response,
                    "run.error",
                    new RunStreamEvent("run.error", request.SessionId ?? "", new RunErrorPayload(ex.Message), DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        });

        return api;
    }

    private static async Task WriteSseAsync(
        HttpResponse response,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
