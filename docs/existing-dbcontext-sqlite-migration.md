# Existing ASP.NET SQLite DbContext Migration Guide

This is the migration path for a real ASP.NET app that already has a SQLite `DbContext` and EF Core migrations.

The existing app `AppDbContext` should own the Agent Platform schema. Do not introduce a second Agent Platform `DbContext` in the app.

## 1. What to copy

Copy the reusable Agent Platform code into the existing ASP.NET solution with clear folders and namespaces, for example:

```text
AgentPlatform/Core
AgentPlatform/Infrastructure/Sqlite
AgentPlatform/Agents
```

Copy:

- Core models, runtime contracts, services, and orchestrator
- SQLite entity classes
- `IAgentPlatformDbContext`
- `AgentPlatformModelBuilderExtensions`
- SQLite stores and runtime classes
- SQLite DI extensions, including `AddAgentPlatformSqliteWithDbContext<TDbContext>()`
- API endpoint mapping extension if you want the provided HTTP routes
- Prebuilt agents only if your app needs the bundled code/config agents

Do not copy or use in the external app:

- `AgentPlatformDbContext`
- `AgentPlatformSchemaInitializer`
- sample API startup code that calls `EnsureAgentPlatformSchemaAsync()`
- `AddAgentPlatformSqlite(configuration)`, because it registers a separate Agent Platform context

Those files remain useful only for this repo's standalone sample API and compatibility tests.

## 2. Add packages

Add the packages the copied infrastructure needs if they are not already present:

```powershell
Install-Package Microsoft.EntityFrameworkCore.Sqlite
Install-Package Microsoft.EntityFrameworkCore.Design
Install-Package Microsoft.Agents.AI.OpenAI
```

Only add OpenAPI support if the host app uses the Agent Platform API routes with OpenAPI:

```powershell
Install-Package Microsoft.AspNetCore.OpenApi
```

## 3. Update AppDbContext

Make the app's existing context implement `IAgentPlatformDbContext`. This example assumes the app context is named `AppDbContext`.

```csharp
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext, IAgentPlatformDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserAgentEntity> UserAgents => Set<UserAgentEntity>();

    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();

    public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyAgentPlatformModel();
    }
}
```

`SaveChangesAsync` and `Database` are already inherited from EF Core `DbContext`, so you do not add separate save methods.

Keep the table names unchanged unless you intentionally customize the model mapping:

- `UserAgents`
- `ChatSessions`
- `ChatMessages`
- `RunEvents`
- `ReasoningTraces`

## 4. Register services

Keep the existing SQLite registration owned by the host app:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
```

Then register Agent Platform against that same context:

```csharp
builder.Services
    .AddAgentPlatformCore()
    .AddAgentPlatformSqliteWithDbContext<AppDbContext>();
```

If you are using bundled prebuilt agents, also register them:

```csharp
builder.Configuration.AddJsonFile("agents.prebuilt.json", optional: true, reloadOnChange: true);

builder.Services
    .Configure<PrebuiltAgentCatalogOptions>(builder.Configuration.GetSection("PrebuiltAgents"))
    .AddPrebuiltAgentCatalog();
```

The important part is that `AddAgentPlatformSqliteWithDbContext<AppDbContext>()` does not call `UseSqlite` and does not register a new DbContext. It maps Agent Platform stores to the existing scoped `AppDbContext`.

## 5. Map API routes

If you want to expose the provided Agent Platform endpoints, add:

```csharp
app.MapAgentPlatformApi("/api");
```

This maps:

- `GET /api/catalog`
- `POST /api/agents`
- `GET /api/agents/{agentId}`
- `PUT /api/agents/{agentId}`
- `GET /api/sessions`
- `GET /api/sessions/{sessionId}`
- `GET /api/sessions/{sessionId}/messages`
- `DELETE /api/sessions/{sessionId}`
- `POST /api/runs/stream`

## 6. Create the Visual Studio EF migration

In Visual Studio:

1. Set the ASP.NET app as the Startup Project.
2. Open Package Manager Console.
3. Set Default Project to the project that contains `AppDbContext` and migrations.
4. Run:

```powershell
Add-Migration AddAgentPlatformEntities -Context AppDbContext
```

Review the generated migration before applying it. It should create only the five Agent Platform tables and their indexes. It should not recreate existing app tables, create a second SQLite connection string, or introduce a second migration history table.

For production-grade schema integrity, confirm the migration includes:

- primary keys on all five Agent Platform tables
- foreign keys from `ChatMessages`, `RunEvents`, and `ReasoningTraces` to `ChatSessions`
- cascade delete from `ChatSessions` to dependent rows
- unique index on `ChatMessages(SessionId, Sequence)`
- unique index on `ReasoningTraces(SessionId, TurnSequence)`
- no foreign key from `ChatSessions.AgentId` to `UserAgents.Id`
- no foreign key from `ReasoningTraces.MessageId` to `ChatMessages.Id`
- non-unique lookup indexes on session/update or session/created timestamps

There is intentionally no foreign key from `ChatSessions.AgentId` to `UserAgents.Id`. Sessions can belong to prebuilt/code agents that are registered in code/config and are not stored as rows in `UserAgents`.

There is intentionally no foreign key from `ReasoningTraces.MessageId` to `ChatMessages.Id`. The runtime can store `null` or provider response IDs there, not only local chat message IDs.

## 7. Existing manual data warning

If this is a fresh install, skip this section.

If the target database already contains manually-created Agent Platform tables/data, clean up duplicates before applying the migration. The production schema makes these combinations unique:

- `ChatMessages(SessionId, Sequence)`
- `ReasoningTraces(SessionId, TurnSequence)`

If duplicates exist, decide which rows to keep, archive/export the others if needed, then apply the migration.

## 8. Apply the migration

Apply the migration from Package Manager Console:

```powershell
Update-Database -Context AppDbContext
```

For the external ASP.NET app, do not call:

```csharp
EnsureCreatedAsync();
EnsureAgentPlatformSchemaAsync();
```

The external app should let EF migrations own schema creation and schema evolution.

## 9. Verify

Build the host app from Visual Studio or Package Manager Console:

```powershell
dotnet build
```

Smoke test the API if you mapped the provided endpoints:

```http
GET /api/catalog
POST /api/agents
GET /api/sessions
POST /api/runs/stream
```

Confirm rows are written into the same SQLite database used by the rest of the app:

- creating an agent writes to `UserAgents`
- starting a run writes to `ChatSessions`
- messages write to `ChatMessages`
- stream events write to `RunEvents`
- reasoning traces write to `ReasoningTraces` when thinking preservation is enabled
