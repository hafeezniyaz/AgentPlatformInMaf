using System.Text.RegularExpressions;
using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Stores;

public sealed partial class SqliteUserAgentStore(IAgentPlatformDbContext dbContext) : IUserAgentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AgentDefinitionDto>> ListAsync(CancellationToken cancellationToken)
        => await dbContext.UserAgents
            .OrderBy(agent => agent.Name)
            .Select(agent => ToDto(agent))
            .ToListAsync(cancellationToken);

    public async Task<AgentDefinitionDto?> GetAsync(string agentId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAgents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == agentId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<AgentDefinitionDto> CreateAsync(CreateAgentRequest request, string defaultModel, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var baseId = Slugify(request.Name);
        var id = baseId;
        var suffix = 1;
        while (await dbContext.UserAgents.AnyAsync(agent => agent.Id == id, cancellationToken))
        {
            id = $"{baseId}-{suffix++}";
        }

        var entity = new UserAgentEntity
        {
            Id = id,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Instructions = request.Instructions.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model.Trim(),
            ToolIdsJson = JsonList.Write(request.ToolIds),
            MiddlewareIdsJson = JsonList.Write(request.MiddlewareIds),
            SkillIdsJson = JsonList.Write(request.SkillIds),
            ContextPolicyJson = WriteContextPolicy(request.ContextPolicy),
            ThinkingPolicyJson = WriteThinkingPolicy(request.ThinkingPolicy),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.UserAgents.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<AgentDefinitionDto?> UpdateAsync(string agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAgents.FirstOrDefaultAsync(agent => agent.Id == agentId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.Instructions = request.Instructions.Trim();
        entity.Model = string.IsNullOrWhiteSpace(request.Model) ? entity.Model : request.Model.Trim();
        entity.ToolIdsJson = JsonList.Write(request.ToolIds);
        entity.MiddlewareIdsJson = JsonList.Write(request.MiddlewareIds);
        entity.SkillIdsJson = JsonList.Write(request.SkillIds);
        entity.ContextPolicyJson = WriteContextPolicy(request.ContextPolicy);
        entity.ThinkingPolicyJson = WriteThinkingPolicy(request.ThinkingPolicy);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    private static AgentDefinitionDto ToDto(UserAgentEntity entity)
        => new(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Source,
            entity.Instructions,
            entity.Model,
            JsonList.Read(entity.ToolIdsJson),
            JsonList.Read(entity.MiddlewareIdsJson),
            JsonList.Read(entity.SkillIdsJson),
            AllowedToolIds: [],
            AllowedMiddlewareIds: [],
            AllowedSkillIds: [],
            ReadContextPolicy(entity.ContextPolicyJson),
            entity.CreatedAt,
            entity.UpdatedAt,
            ReadThinkingPolicy(entity.ThinkingPolicyJson));

    private static string? WriteContextPolicy(ContextPolicyDto? contextPolicy)
        => contextPolicy is null ? null : JsonSerializer.Serialize(contextPolicy, JsonOptions);

    private static ContextPolicyDto? ReadContextPolicy(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ContextPolicyDto>(json, JsonOptions);

    private static string? WriteThinkingPolicy(ThinkingPolicyDto? thinkingPolicy)
        => thinkingPolicy is null ? null : JsonSerializer.Serialize(thinkingPolicy, JsonOptions);

    private static ThinkingPolicyDto? ReadThinkingPolicy(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ThinkingPolicyDto>(json, JsonOptions);

    private static string Slugify(string value)
    {
        var slug = NonAlphaNumeric().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"agent-{Guid.NewGuid():n}" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumeric();
}
