using AgentPlatform.Agents;
using AgentPlatform.Core;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Runtime;
using AgentPlatform.Infrastructure.Sqlite.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Agents.AI.Compaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Tests;

public sealed class AgentPlatformTests
{
    [Fact]
    public async Task Catalog_contains_prebuilt_and_user_agents()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var created = await catalog.CreateAgentAsync(
            new CreateAgentRequest("Research Helper", "Helps research", "Answer with sources when available.", null, ["clock"], ["safety"], ["agent-design"]),
            CancellationToken.None);

        var result = await catalog.GetCatalogAsync(CancellationToken.None);

        Assert.Contains(result.Agents, agent => agent.Id == "general-assistant" && agent.Source == "code");
        Assert.Contains(result.Agents, agent => agent.Id == created.Id && agent.Source == "user");
        Assert.Contains(result.Tools, tool => tool.Id == "clock");
        Assert.Contains(result.Skills, skill => skill.Id == "agent-design");
        Assert.Contains("inFlight", result.Context.SupportedModes);
        Assert.Contains("balanced", result.Context.SupportedProfiles);
        Assert.Equal("inFlight", result.Context.DefaultPolicy.Mode);
    }

    [Fact]
    public async Task User_agent_catalog_exposes_context_policy_override()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var created = await catalog.CreateAgentAsync(
            new CreateAgentRequest(
                "Persistent Agent",
                "Keeps a compact prompt",
                "Remember the durable context.",
                null,
                [],
                [],
                [],
                new ContextPolicyDto
                {
                    Mode = "persistedPrompt",
                    Profile = "cheapNoLlm",
                    SlidingWindowMaxTurns = 8
                }),
            CancellationToken.None);

        var result = await catalog.GetAgentAsync(created.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("persistedPrompt", result.ContextPolicy?.Mode);
        Assert.Equal("cheapNoLlm", result.ContextPolicy?.Profile);
        Assert.Equal(8, result.ContextPolicy?.SlidingWindowMaxTurns);
    }

    [Fact]
    public async Task Context_policy_resolver_merges_defaults_and_rejects_invalid_values()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var resolver = fixture.Services.GetRequiredService<IContextPolicyResolver>();

        var resolved = resolver.Resolve(new ContextPolicyDto
        {
            Mode = "persistedPrompt",
            Profile = "cheapNoLlm",
            SlidingWindowMaxTurns = 7
        });

        Assert.True(resolved.Enabled);
        Assert.Equal("persistedPrompt", resolved.Mode);
        Assert.Equal("cheapNoLlm", resolved.Profile);
        Assert.Equal(7, resolved.SlidingWindowMaxTurns);
        Assert.Equal(0x8000, resolved.TruncationTokenThreshold);
        Assert.Throws<AgentPlatformValidationException>(() => resolver.Resolve(new ContextPolicyDto { Mode = "missing" }));
    }

    [Fact]
    public void Balanced_compaction_factory_builds_expected_pipeline_without_openai_call()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OPENAI_API_KEY"] = "test-key" })
            .Build();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var factory = new ContextCompactionProviderFactory(
            configuration,
            Options.Create(new AgentPlatformOptions()),
            loggerFactory);

        var policy = new ContextPolicyResolver(Options.Create(new AgentPlatformOptions())).GetDefaultPolicy();

#pragma warning disable MAAI001
        var pipeline = Assert.IsType<PipelineCompactionStrategy>(factory.CreateStrategy(policy));
        Assert.Collection(
            pipeline.Strategies,
            strategy => Assert.IsType<ToolResultCompactionStrategy>(strategy),
            strategy => Assert.IsType<SummarizationCompactionStrategy>(strategy),
            strategy => Assert.IsType<SlidingWindowCompactionStrategy>(strategy),
            strategy => Assert.IsType<TruncationCompactionStrategy>(strategy));
#pragma warning restore MAAI001
    }

    [Fact]
    public async Task Stream_rejects_unknown_tool_ids()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();

        var ex = await Assert.ThrowsAsync<AgentPlatformValidationException>(async () =>
        {
            await foreach (var _ in orchestrator.StreamAsync(
                new StreamRunRequest(null, "general-assistant", ["missing-tool"], [], [], "hello", null),
                CancellationToken.None))
            {
            }
        });

        Assert.Contains("Unknown tool", ex.Message);
    }

    [Fact]
    public async Task Session_details_return_ordered_chat_history_for_rehydration()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["clock"], ["safety"], [], "Hello there", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        Assert.NotNull(sessionId);
        var session = await store.GetSessionAsync(sessionId, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("general-assistant", session.AgentId);
        Assert.Equal(["clock"], session.ToolIds);
        Assert.Equal(["safety"], session.MiddlewareIds);
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal("user", session.Messages[0].Role);
        Assert.Equal("assistant", session.Messages[1].Role);
    }

    [Fact]
    public async Task Config_hash_mismatch_blocks_session_reuse()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["clock"], [], [], "First", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var ex = await Assert.ThrowsAsync<AgentPlatformValidationException>(async () =>
        {
            await foreach (var _ in orchestrator.StreamAsync(
                new StreamRunRequest(sessionId, "general-assistant", ["calculator"], [], [], "Second", null),
                CancellationToken.None))
            {
            }
        });

        Assert.Contains("different agent configuration", ex.Message);
    }

    [Fact]
    public async Task Existing_sessions_continue_using_stored_context_policy_after_agent_update()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        var created = await catalog.CreateAgentAsync(
            new CreateAgentRequest(
                "Session Policy Agent",
                "Tests stored policy snapshots",
                "Answer briefly.",
                null,
                ["clock"],
                [],
                [],
                new ContextPolicyDto { Mode = "persistedPrompt", Profile = "cheapNoLlm", SlidingWindowMaxTurns = 3 }),
            CancellationToken.None);

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, created.Id, null, null, null, "First", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        await catalog.UpdateAgentAsync(
            created.Id,
            new UpdateAgentRequest(
                "Session Policy Agent",
                "Tests stored policy snapshots",
                "Answer briefly.",
                null,
                ["clock"],
                [],
                [],
                new ContextPolicyDto { Mode = "inFlight", Profile = "balanced" }),
            CancellationToken.None);

        await foreach (var _ in orchestrator.StreamAsync(
            new StreamRunRequest(sessionId, created.Id, null, null, null, "Second", null),
            CancellationToken.None))
        {
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("persistedPrompt", session.ContextMode);
        Assert.Equal("persistedPrompt", session.ContextPolicy.Mode);
        Assert.Equal(4, session.Messages.Count);
    }

    [Fact]
    public async Task InFlight_context_keeps_full_history_without_compacted_prompt_snapshot()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["clock"], [], [], "Hello there", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var stored = await store.GetStoredSessionAsync(sessionId!, CancellationToken.None);
        var details = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.NotNull(details);
        Assert.Equal("inFlight", stored.ContextMode);
        Assert.Null(stored.CompactedPromptSnapshotJson);
        Assert.Null(stored.LastCompactionStats);
        Assert.Equal(2, details.Messages.Count);
    }

    [Fact]
    public async Task Persisted_prompt_snapshot_is_separate_from_frontend_chat_history()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var store = fixture.Services.GetRequiredService<IConversationStore>();
        var contextPolicy = new ContextPolicyDto
        {
            Enabled = true,
            Mode = "persistedPrompt",
            Profile = "cheapNoLlm",
            SummarizerModel = "gpt-4o-mini",
            ToolResultTokenThreshold = 512,
            ToolResultMinimumPreservedGroups = 2,
            SummarizationTokenThreshold = 1280,
            SummarizationMinimumPreservedGroups = 4,
            SlidingWindowMaxTurns = 4,
            SlidingWindowMinimumPreservedTurns = 1,
            TruncationTokenThreshold = 32768,
            TruncationMinimumPreservedGroups = 32
        };

        var stored = await store.CreateSessionAsync(
            "snapshot-session",
            "general-assistant",
            "General Assistant",
            "Snapshot test",
            "hash",
            ["clock"],
            [],
            [],
            contextPolicy,
            CancellationToken.None);
        await store.AddMessageAsync(stored.SessionId, "user", "Full user message", CancellationToken.None);
        await store.AddMessageAsync(stored.SessionId, "assistant", "Full assistant response", CancellationToken.None);

        var stats = new ContextCompactionStatsDto(10, 3, "persistedPrompt", "cheapNoLlm", DateTimeOffset.UtcNow);
        await store.UpdateSessionAfterRunAsync(
            stored.SessionId,
            "Full assistant response",
            serializedSessionState: null,
            compactedPromptSnapshotJson: "[]",
            compactionStats: stats,
            CancellationToken.None);

        var rehydrated = await store.GetSessionAsync(stored.SessionId, CancellationToken.None);
        var refreshed = await store.GetStoredSessionAsync(stored.SessionId, CancellationToken.None);

        Assert.NotNull(rehydrated);
        Assert.NotNull(refreshed);
        Assert.Equal(2, rehydrated.Messages.Count);
        Assert.Equal("Full user message", rehydrated.Messages[0].Content);
        Assert.Equal("[]", refreshed.CompactedPromptSnapshotJson);
        Assert.Equal(3, rehydrated.LastCompactionStats?.CompactedMessageCount);
    }

    [Fact]
    public async Task Selected_skills_inject_required_tools_into_session_configuration()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", [], [], ["agent-design"], "Design an agent", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Contains("clock", session.ToolIds);
        Assert.Equal(["agent-design"], session.SkillIds);
    }

    [Fact]
    public async Task Skill_wildcard_allows_agent_to_access_all_catalog_skills()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", [], [], ["*"], "Use any relevant skill", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Contains("agent-design", session.SkillIds);
        Assert.Contains("clock", session.ToolIds);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFixture(ServiceProvider services, SqliteConnection connection)
        {
            Services = services;
            _connection = connection;
        }

        public ServiceProvider Services { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.Configure<AgentPlatformOptions>(options =>
            {
                options.DefaultModel = "test-model";
                options.SkillsPath = "skills";
            });

            services.AddDbContext<AgentPlatformDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IUserAgentStore, SqliteUserAgentStore>();
            services.AddScoped<IConversationStore, SqliteConversationStore>();
            services.AddSingleton<IPrebuiltAgentDefinition, GeneralAssistantAgent>();
            services.AddSingleton<IPrebuiltAgentDefinition, AgentBuilderAgent>();
            services.AddSingleton<IStaticCatalog, DefaultStaticCatalog>();
            services.AddAgentPlatformCore();
            services.AddSingleton<IAgentRuntime, FakeAgentRuntime>();

            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<AgentPlatformDbContext>().EnsureAgentPlatformSchemaAsync();
            return new TestFixture(provider, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeAgentRuntime : IAgentRuntime
    {
        public async IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
            AgentRunSpec run,
            string? serializedSessionState,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new RuntimeStreamEvent("text.delta", new TextDeltaPayload($"Echo: {run.Message}"));
            yield return new RuntimeStreamEvent("run.completed", new RunCompletedPayload(""), "{\"state\":\"ok\"}");
        }
    }
}
