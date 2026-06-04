using AgentPlatform.Agents;
using AgentPlatform.Api;
using AgentPlatform.Core;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Infrastructure.Sqlite;
using AgentPlatform.Infrastructure.Sqlite.Data;

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

app.MapAgentPlatformApi("/api");

app.Run();

public partial class Program;
