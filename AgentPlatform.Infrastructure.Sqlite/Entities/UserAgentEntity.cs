namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class UserAgentEntity
{
    public string Id { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string Description { get; set; } = "";

    public string Source { get; set; } = "user";

    public string Instructions { get; set; } = default!;

    public string Model { get; set; } = default!;

    public string ToolIdsJson { get; set; } = "[]";

    public string MiddlewareIdsJson { get; set; } = "[]";

    public string SkillIdsJson { get; set; } = "[]";

    public string? ContextPolicyJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
