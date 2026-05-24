using System.Text.Json;
using AgentPlatform.Agents;
using AgentPlatform.Core;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite;
using AgentPlatform.Infrastructure.Sqlite.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("agents.prebuilt.json", optional: true, reloadOnChange: true);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});

builder.Services
    .Configure<PrebuiltAgentCatalogOptions>(builder.Configuration.GetSection("PrebuiltAgents"))
    .AddAgentPlatformCore()
    .AddPrebuiltAgentCatalog()
    .AddAgentPlatformSqlite(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AgentPlatformDbContext>();
    await dbContext.EnsureAgentPlatformSchemaAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

var api = app.MapGroup("/api");

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
        return Results.Created($"/api/agents/{created.Id}", created);
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

app.Run();

static async Task WriteSseAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
{
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public partial class Program;
