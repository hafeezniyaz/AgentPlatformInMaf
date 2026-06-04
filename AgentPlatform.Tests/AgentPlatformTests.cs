using AgentPlatform.Agents;
using AgentPlatform.Core;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using AgentPlatform.Infrastructure.Sqlite.Runtime;
using AgentPlatform.Infrastructure.Sqlite.Stores;
using AgentPlatform.Infrastructure.Sqlite.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Agents.AI.Compaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
        var skill = Assert.Single(result.Skills, skill => skill.Id == "agent-design");
        Assert.Equal("Agent Design", skill.Name);
        Assert.Equal("clock", skill.Metadata?["allowedTools"]);
        Assert.Contains("inFlight", result.Context.SupportedModes);
        Assert.Contains("balanced", result.Context.SupportedProfiles);
        Assert.Equal("inFlight", result.Context.DefaultPolicy.Mode);
        Assert.Contains("preserved", result.Thinking.SupportedModes);
        Assert.Contains("opaque", result.Thinking.SupportedCaptures);
        Assert.Contains(result.Models, model => model.Id == "test-model");
    }

    [Fact]
    public async Task Prebuilt_agent_catalog_auto_discovers_code_agents()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var discovered = fixture.Services.GetServices<IPrebuiltAgentDefinition>().ToList();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var result = await catalog.GetCatalogAsync(CancellationToken.None);

        Assert.Contains(discovered, agent => agent.Descriptor.Id == "general-assistant");
        Assert.Contains(discovered, agent => agent.Descriptor.Id == "agent-builder");
        Assert.Contains(result.Agents, agent => agent.Id == "general-assistant" && agent.Source == "code");
        Assert.Contains(result.Agents, agent => agent.Id == "agent-builder" && agent.Source == "code");
    }

    [Fact]
    public async Task Prebuilt_agent_config_overrides_descriptor_fields()
    {
        await using var fixture = await TestFixture.CreateAsync(prebuiltOptions =>
        {
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig
            {
                Id = "general-assistant",
                Name = "Configured Assistant",
                Description = "Configured description",
                Instructions = "Configured instructions",
                Model = "configured-model",
                ToolIds = ["weather"],
                MiddlewareIds = ["logging"],
                SkillIds = ["agent-design"],
                AllowedToolIds = ["clock", "weather"],
                AllowedMiddlewareIds = ["logging"],
                AllowedSkillIds = ["agent-design"],
                ContextPolicy = new ContextPolicyDto { Mode = "persistedPrompt", Profile = "cheapNoLlm" },
                ThinkingPolicy = new ThinkingPolicyDto { Enabled = true, Mode = "preserved", Capture = "full" }
            });
        });
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var agent = await catalog.GetAgentAsync("general-assistant", CancellationToken.None);

        Assert.NotNull(agent);
        Assert.Equal("general-assistant", agent.Id);
        Assert.Equal("code", agent.Source);
        Assert.Equal("Configured Assistant", agent.Name);
        Assert.Equal("Configured description", agent.Description);
        Assert.Equal("Configured instructions", agent.Instructions);
        Assert.Equal("configured-model", agent.Model);
        Assert.Equal(["weather"], agent.ToolIds);
        Assert.Equal(["logging"], agent.MiddlewareIds);
        Assert.Equal(["agent-design"], agent.SkillIds);
        Assert.Equal(["clock", "weather"], agent.AllowedToolIds);
        Assert.Equal("persistedPrompt", agent.ContextPolicy?.Mode);
        Assert.Equal("preserved", agent.ThinkingPolicy?.Mode);
    }

    [Fact]
    public async Task Prebuilt_agent_config_missing_fields_inherit_descriptor_values()
    {
        await using var fixture = await TestFixture.CreateAsync(prebuiltOptions =>
        {
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig
            {
                Id = "general-assistant",
                Name = "Renamed Assistant"
            });
        });
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var agent = await catalog.GetAgentAsync("general-assistant", CancellationToken.None);

        Assert.NotNull(agent);
        Assert.Equal("Renamed Assistant", agent.Name);
        Assert.Equal("gpt-4o-mini", agent.Model);
        Assert.Equal(["clock", "calculator"], agent.ToolIds);
        Assert.Contains("helpful agentic assistant", agent.Instructions);
    }

    [Fact]
    public async Task Disabled_prebuilt_agent_is_hidden_from_catalog_and_lookup()
    {
        await using var fixture = await TestFixture.CreateAsync(prebuiltOptions =>
        {
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig
            {
                Id = "general-assistant",
                Enabled = false
            });
        });
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();

        var result = await catalog.GetCatalogAsync(CancellationToken.None);
        var agent = await catalog.GetAgentAsync("general-assistant", CancellationToken.None);

        Assert.DoesNotContain(result.Agents, item => item.Id == "general-assistant");
        Assert.Null(agent);
        Assert.Contains(result.Agents, item => item.Id == "agent-builder");
    }

    [Fact]
    public async Task Duplicate_prebuilt_agent_ids_fail_clearly()
    {
        await using var fixture = await TestFixture.CreateAsync(
            configureServices: services => services.AddSingleton<IPrebuiltAgentDefinition>(
                new TestPrebuiltAgent("general-assistant")));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IAgentCatalogService>());

        Assert.Contains("Duplicate prebuilt agent id 'general-assistant'", ex.Message);
    }

    [Fact]
    public async Task Unknown_prebuilt_agent_config_id_fails_clearly()
    {
        await using var fixture = await TestFixture.CreateAsync(prebuiltOptions =>
        {
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig
            {
                Id = "missing-agent",
                Name = "Missing Agent"
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IAgentCatalogService>());

        Assert.Contains("does not match a discovered code agent", ex.Message);
    }

    [Fact]
    public async Task Duplicate_prebuilt_agent_config_ids_fail_clearly()
    {
        await using var fixture = await TestFixture.CreateAsync(prebuiltOptions =>
        {
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig { Id = "general-assistant", Name = "One" });
            prebuiltOptions.Agents.Add(new PrebuiltAgentConfig { Id = "GENERAL-ASSISTANT", Name = "Two" });
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IAgentCatalogService>());

        Assert.Contains("Duplicate prebuilt agent config id", ex.Message);
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
    public async Task Thinking_policy_resolver_merges_defaults_and_rejects_invalid_values()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var resolver = fixture.Services.GetRequiredService<IThinkingPolicyResolver>();

        using var raw = JsonDocument.Parse("""{"enable_thinking":true}""");
        var resolved = resolver.Resolve(
            sessionPolicy: null,
            agentPolicy: new ThinkingPolicyDto
            {
                Enabled = true,
                Mode = "preserved",
                Capture = "full",
                RawRequestOptionsJson = raw.RootElement
            },
            modelPolicy: null);

        Assert.True(resolved.Enabled);
        Assert.Equal("preserved", resolved.Mode);
        Assert.Equal("full", resolved.Capture);
        Assert.Equal(24000, resolved.MaxPreservedTokens);
        Assert.Equal(JsonValueKind.Object, resolved.RawRequestOptionsJson?.ValueKind);
        Assert.Throws<AgentPlatformValidationException>(() => resolver.Resolve(null, new ThinkingPolicyDto { Mode = "missing" }, null));
        Assert.Throws<AgentPlatformValidationException>(() => resolver.Resolve(null, new ThinkingPolicyDto { Enabled = false, Mode = "preserved" }, null));
    }

    [Fact]
    public void Vllm_raw_request_options_merge_at_root_and_reject_protected_keys()
    {
        using var raw = JsonDocument.Parse("""{"enable_thinking":true,"preserve_thinking":true}""");
        var body = VllmOpenAICompatibleChatClient.BuildRequestBody(
            "qwen3.6-plus",
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
            null,
            new ThinkingPolicyDto { RawRequestOptionsJson = raw.RootElement });

        Assert.True(((JsonElement)body["enable_thinking"]!).GetBoolean());
        Assert.True(((JsonElement)body["preserve_thinking"]!).GetBoolean());

        using var invalid = JsonDocument.Parse("""{"model":"override"}""");
        Assert.Throws<AgentPlatformValidationException>(() =>
            VllmOpenAICompatibleChatClient.BuildRequestBody(
                "qwen3.6-plus",
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
                null,
                new ThinkingPolicyDto { RawRequestOptionsJson = invalid.RootElement }));
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
    public async Task Tool_registry_catalog_includes_di_registered_tools()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();

        var tools = registry.ListTools();

        Assert.Contains(tools, tool => tool.Id == "clock" && tool.Category == "utility");
        Assert.Contains(tools, tool => tool.Id == "calculator" && tool.Category == "utility");
        Assert.Contains(tools, tool => tool.Id == "weather" && tool.Category == "demo");
    }

    [Fact]
    public async Task Tool_registry_resolves_only_selected_tools_from_active_scope()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();

        var tools = registry.ResolveTools(["clock", "calculator"], scope.ServiceProvider);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "clock");
        Assert.Contains(tools, tool => tool.Name == "calculator");
        Assert.DoesNotContain(tools, tool => tool.Name == "weather");
    }

    [Fact]
    public async Task Tool_can_use_injected_downstream_service()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();
        var calculator = Assert.IsAssignableFrom<AIFunction>(registry.ResolveTools(["calculator"], scope.ServiceProvider).Single());

        var result = await calculator.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["expression"] = "2 + 3 * 4" }),
            CancellationToken.None);

        Assert.Equal("14", ConvertToolResult(result));
    }

    [Fact]
    public async Task Tool_method_can_resolve_scoped_services_from_invocation_context()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();
        var weather = Assert.IsAssignableFrom<AIFunction>(registry.ResolveTools(["weather"], scope.ServiceProvider).Single());

        var result = await weather.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["location"] = "Riyadh" })
            {
                Services = scope.ServiceProvider
            },
            CancellationToken.None);

        Assert.Contains("Riyadh", ConvertToolResult(result));
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
            new ThinkingPolicyDto { Enabled = false, Mode = "disabled", Capture = "opaque", ExposeToClient = false, MaxPreservedTokens = 24000 },
            new ResolvedModel("test-model", "openai", null, "test-model", null, null),
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
    public async Task Preserved_thinking_stores_reasoning_without_leaking_into_chat_history()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();
        var traces = fixture.Services.GetRequiredService<IReasoningTraceStore>();

        var agent = await catalog.CreateAgentAsync(
            new CreateAgentRequest(
                "Thinking Agent",
                "Uses hidden reasoning",
                "Answer briefly.",
                null,
                [],
                [],
                [],
                null,
                new ThinkingPolicyDto { Enabled = true, Mode = "preserved", Capture = "full", ExposeToClient = false }),
            CancellationToken.None);

        var events = new List<RunStreamEvent>();
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, agent.Id, null, null, null, "think privately", null),
            CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        var sessionId = Assert.Single(events, item => item.Event == "run.started").SessionId;
        var session = await store.GetSessionAsync(sessionId, CancellationToken.None);
        var savedTraces = await traces.GetForSessionAsync(sessionId, null, 10, CancellationToken.None);

        Assert.DoesNotContain(events, item => item.Event == "reasoning.delta");
        Assert.NotNull(session);
        Assert.Equal(2, session.Messages.Count);
        Assert.DoesNotContain(session.Messages, message => message.Content.Contains("private reasoning", StringComparison.OrdinalIgnoreCase));
        Assert.Single(savedTraces);
        Assert.Contains("private reasoning", savedTraces[0].ReasoningContentJson);
        Assert.Equal(1, session.ReasoningTraceCount);
    }

    [Fact]
    public async Task Expose_to_client_true_streams_reasoning_events()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();

        var agent = await catalog.CreateAgentAsync(
            new CreateAgentRequest(
                "Debug Thinking Agent",
                "Streams reasoning",
                "Answer briefly.",
                null,
                [],
                [],
                [],
                null,
                new ThinkingPolicyDto { Enabled = true, Mode = "preserved", Capture = "full", ExposeToClient = true }),
            CancellationToken.None);

        var events = new List<RunStreamEvent>();
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, agent.Id, null, null, null, "show debug thinking", null),
            CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, item => item.Event == "reasoning.started");
        Assert.Contains(events, item => item.Event == "reasoning.delta");
        Assert.Contains(events, item => item.Event == "reasoning.completed");
    }

    [Fact]
    public async Task Selected_skills_do_not_inject_allowed_tools_into_session_configuration()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["weather"], [], ["agent-design"], "Design an agent", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(["weather"], session.ToolIds);
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
            new StreamRunRequest(null, "general-assistant", ["weather"], [], ["*"], "Use any relevant skill", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Contains("agent-design", session.SkillIds);
        Assert.Equal(["weather"], session.ToolIds);
    }

    [Fact]
    public async Task Existing_session_empty_selections_reuse_stored_selection_snapshot()
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

        RunStartedPayload? continuedRun = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(sessionId, "general-assistant", [], [], [], "Continue the design", null),
            CancellationToken.None))
        {
            if (streamEvent.Event == "run.started")
            {
                continuedRun = Assert.IsType<RunStartedPayload>(streamEvent.Data);
            }
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.NotNull(continuedRun);
        Assert.Contains("agent-design", session.SkillIds);
        Assert.Contains("clock", session.ToolIds);
        Assert.Contains("agent-design", continuedRun.SkillIds);
        Assert.Contains("clock", continuedRun.ToolIds);
        Assert.Equal(4, session.Messages.Count);
    }

    [Fact]
    public async Task Existing_session_null_selections_reuse_stored_selection_snapshot()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["weather"], ["logging"], ["agent-design"], "Design an agent", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        RunStartedPayload? continuedRun = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(sessionId, "general-assistant", null, null, null, "Continue the design", null),
            CancellationToken.None))
        {
            if (streamEvent.Event == "run.started")
            {
                continuedRun = Assert.IsType<RunStartedPayload>(streamEvent.Data);
            }
        }

        var session = await store.GetSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.NotNull(continuedRun);
        Assert.Equal(["agent-design"], session.SkillIds);
        Assert.Equal(["weather"], session.ToolIds);
        Assert.Equal(["logging"], continuedRun.MiddlewareIds);
        Assert.Equal(["weather"], continuedRun.ToolIds);
        Assert.DoesNotContain("calculator", continuedRun.ToolIds);
    }

    [Fact]
    public async Task Model_switch_with_omitted_selections_reuses_stored_selection_snapshot()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        string? sessionId = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "general-assistant", ["weather"], ["logging"], ["agent-design"], "Design an agent", null),
            CancellationToken.None))
        {
            sessionId ??= streamEvent.SessionId;
        }

        RunStartedPayload? continuedRun = null;
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(sessionId, "general-assistant", null, null, null, "Continue with another model", "alternate-model"),
            CancellationToken.None))
        {
            if (streamEvent.Event == "run.started")
            {
                continuedRun = Assert.IsType<RunStartedPayload>(streamEvent.Data);
            }
        }

        var stored = await store.GetStoredSessionAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.NotNull(continuedRun);
        Assert.Equal("alternate-model", stored.Model.Id);
        Assert.Equal(["agent-design"], stored.SkillIds);
        Assert.Equal(["weather"], stored.ToolIds);
        Assert.Equal(["weather"], continuedRun.ToolIds);
    }

    [Fact]
    public async Task Code_agent_logic_routes_message_treatments_and_persists_state()
    {
        await using var fixture = await TestFixture.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<IPrebuiltAgentDefinition>(new TestPrebuiltAgent("logic-agent"));
            services.AddScoped<ICodeAgentLogic, TestCodeAgentLogic>();
        });
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();
        var store = fixture.Services.GetRequiredService<IConversationStore>();

        var firstEvents = new List<RunStreamEvent>();
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, "logic-agent", [], [], [], "First logic request", null),
            CancellationToken.None))
        {
            firstEvents.Add(streamEvent);
        }

        var sessionId = Assert.Single(firstEvents, item => item.Event == "run.started").SessionId;
        var progressEvents = firstEvents.Where(item => item.Event == "agent.progress").ToList();
        Assert.Equal(2, progressEvents.Count);
        Assert.Contains(progressEvents, item => Assert.IsType<LogicProgressPayload>(item.Data).Message == "sink-progress");
        Assert.Contains(progressEvents, item => Assert.IsType<LogicProgressPayload>(item.Data).Message == "direct-progress");
        Assert.Contains(firstEvents, item => item.Event == "text.delta" && Assert.IsType<TextDeltaPayload>(item.Data).Text.Contains("without state"));
        Assert.DoesNotContain(firstEvents, item => item.Event == "conversation.append");
        Assert.DoesNotContain(firstEvents, item => item.Event == "agent.state.update");

        var firstSession = await store.GetSessionAsync(sessionId, CancellationToken.None);
        var firstStored = await store.GetStoredSessionAsync(sessionId, CancellationToken.None);

        Assert.NotNull(firstSession);
        Assert.NotNull(firstStored);
        Assert.Contains(firstSession.Messages, message => message.Content == "Durable note from logic.");
        Assert.Contains(firstSession.Messages, message => message.Content == "Logic answer without state.");
        Assert.DoesNotContain(firstSession.Messages, message => message.Content.Contains("progress", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("""{"logic":"ok"}""", firstStored.SerializedSessionState);
        using (var state = JsonDocument.Parse(firstStored.AgentStateJson))
        {
            Assert.True(state.RootElement.TryGetProperty("turnMessageCount", out var count));
            Assert.Equal(0, count.GetInt32());
        }

        var secondEvents = new List<RunStreamEvent>();
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(sessionId, "logic-agent", [], [], [], "Second logic request", null),
            CancellationToken.None))
        {
            secondEvents.Add(streamEvent);
        }

        var secondProgress = secondEvents
            .Where(item => item.Event == "agent.progress")
            .Select(item => Assert.IsType<LogicProgressPayload>(item.Data))
            .ToList();

        Assert.NotEmpty(secondProgress);
        Assert.All(secondProgress, payload => Assert.True(payload.HadState));
        Assert.All(secondProgress, payload => Assert.True(payload.RecentMessages >= 3));
        Assert.Contains(secondEvents, item => item.Event == "text.delta" && Assert.IsType<TextDeltaPayload>(item.Data).Text.Contains("with state"));
    }

    [Fact]
    public async Task User_agent_with_matching_logic_id_uses_default_runtime()
    {
        await using var fixture = await TestFixture.CreateAsync(configureServices: services =>
        {
            services.AddScoped<ICodeAgentLogic, TestCodeAgentLogic>();
        });
        var catalog = fixture.Services.GetRequiredService<IAgentCatalogService>();
        var orchestrator = fixture.Services.GetRequiredService<AgentRunOrchestrator>();

        var userAgent = await catalog.CreateAgentAsync(
            new CreateAgentRequest(
                "Logic Agent",
                "User agent with the same id as test logic.",
                "Answer normally.",
                null,
                [],
                [],
                []),
            CancellationToken.None);

        var events = new List<RunStreamEvent>();
        await foreach (var streamEvent in orchestrator.StreamAsync(
            new StreamRunRequest(null, userAgent.Id, [], [], [], "Hello user agent", null),
            CancellationToken.None))
        {
            events.Add(streamEvent);
        }

        Assert.Equal("logic-agent", userAgent.Id);
        Assert.Contains(events, item => item.Event == "text.delta" && Assert.IsType<TextDeltaPayload>(item.Data).Text == "Echo: Hello user agent");
        Assert.DoesNotContain(events, item => item.Event == "agent.progress");
    }

    [Fact]
    public async Task Existing_dbcontext_registration_uses_external_sqlite_context()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.Configure<AgentPlatformOptions>(options =>
        {
            options.DefaultModel = "test-model";
            options.SkillsPath = FindSkillsPath();
        });
        services.AddDbContext<TestAppDbContext>(options => options.UseSqlite(connection));
        services.AddAgentPlatformCore();
        services.AddAgentPlatformSqliteWithDbContext<TestAppDbContext>();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<TestAppDbContext>().Database.EnsureCreatedAsync();

        using var scope = provider.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var appDbContext = scopedServices.GetRequiredService<TestAppDbContext>();
        var agentPlatformDbContext = scopedServices.GetRequiredService<IAgentPlatformDbContext>();

        Assert.Same(appDbContext, agentPlatformDbContext);
        Assert.NotNull(scopedServices.GetRequiredService<IUserAgentStore>());
        Assert.NotNull(scopedServices.GetRequiredService<IConversationStore>());
        Assert.NotNull(scopedServices.GetRequiredService<IReasoningTraceStore>());
        Assert.NotNull(scopedServices.GetRequiredService<SqliteChatHistoryProvider>());
        Assert.NotNull(scopedServices.GetRequiredService<IAgentRuntime>());
        AssertHasSessionCascadeForeignKey<ChatMessageEntity>(appDbContext);
        AssertHasSessionCascadeForeignKey<RunEventEntity>(appDbContext);
        AssertHasSessionCascadeForeignKey<ReasoningTraceEntity>(appDbContext);
        AssertHasUniqueIndex<ChatMessageEntity>(appDbContext, nameof(ChatMessageEntity.SessionId), nameof(ChatMessageEntity.Sequence));
        AssertHasUniqueIndex<ReasoningTraceEntity>(appDbContext, nameof(ReasoningTraceEntity.SessionId), nameof(ReasoningTraceEntity.TurnSequence));

        var userAgentStore = scopedServices.GetRequiredService<IUserAgentStore>();
        var createdAgent = await userAgentStore.CreateAsync(
            new CreateAgentRequest(
                "External DB Agent",
                "Uses the host app database",
                "Answer from the host app context.",
                null,
                [],
                [],
                []),
            "test-model",
            CancellationToken.None);

        Assert.Equal(1, await appDbContext.UserAgents.CountAsync(CancellationToken.None));
        Assert.Equal(createdAgent.Id, await appDbContext.UserAgents.Select(agent => agent.Id).SingleAsync(CancellationToken.None));

        var conversationStore = scopedServices.GetRequiredService<IConversationStore>();
        var storedSession = await conversationStore.CreateSessionAsync(
            "external-session",
            createdAgent.Id,
            createdAgent.Name,
            "External session",
            "hash",
            [],
            [],
            [],
            new ContextPolicyDto { Enabled = true, Mode = "inFlight", Profile = "balanced", SummarizerModel = "test-model" },
            new ThinkingPolicyDto { Enabled = false, Mode = "disabled", Capture = "opaque", ExposeToClient = false, MaxPreservedTokens = 24000 },
            new ResolvedModel("test-model", "openai", null, "test-model", null, null),
            CancellationToken.None);

        await conversationStore.AddMessageAsync(storedSession.SessionId, "user", "Hello from the existing app database.", CancellationToken.None);

        Assert.Equal(1, await appDbContext.ChatSessions.CountAsync(CancellationToken.None));
        Assert.Equal(1, await appDbContext.ChatMessages.CountAsync(CancellationToken.None));
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

        public static async Task<TestFixture> CreateAsync(
            Action<PrebuiltAgentCatalogOptions>? configurePrebuiltAgents = null,
            Action<IServiceCollection>? configureServices = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.Configure<AgentPlatformOptions>(options =>
            {
                options.DefaultModel = "test-model";
                options.SkillsPath = FindSkillsPath();
            });
            if (configurePrebuiltAgents is not null)
            {
                services.Configure(configurePrebuiltAgents);
            }

            services.AddDbContext<AgentPlatformDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IAgentPlatformDbContext>(provider => provider.GetRequiredService<AgentPlatformDbContext>());
            services.AddScoped<IUserAgentStore, SqliteUserAgentStore>();
            services.AddScoped<IConversationStore, SqliteConversationStore>();
            services.AddScoped<IReasoningTraceStore, SqliteReasoningTraceStore>();
            services.AddPrebuiltAgentCatalog();
            services.AddAgentPlatformBuiltinTools();
            services.AddAgentPlatformCore();
            services.AddScoped<IAgentSkillCatalog, FileAgentSkillCatalog>();
            services.AddSingleton<IAgentRuntime, FakeAgentRuntime>();
            configureServices?.Invoke(services);

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

    private sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options) :
        DbContext(options),
        IAgentPlatformDbContext
    {
        public DbSet<UserAgentEntity> UserAgents => Set<UserAgentEntity>();

        public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

        public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

        public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();

        public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyAgentPlatformModel();
    }

    private static string FindSkillsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "skills");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath("skills");
    }

    private static void AssertHasSessionCascadeForeignKey<TEntity>(DbContext dbContext)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(ChatSessionEntity) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
    }

    private static void AssertHasUniqueIndex<TEntity>(DbContext dbContext, params string[] propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static string ConvertToolResult(object? result)
    {
        if (result is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "";
        }

        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private sealed record LogicProgressPayload(string Message, int RecentMessages, bool HadState);

    private sealed class TestCodeAgentLogic(IAgentRunEventSink eventSink) : ICodeAgentLogic
    {
        public string AgentId => "logic-agent";

        public async IAsyncEnumerable<AgentLogicEvent> StreamAsync(
            AgentLogicContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await eventSink.EmitAsync(
                new AgentLogicEvent(
                    "agent.progress",
                    new LogicProgressPayload("sink-progress", context.RecentMessages.Count, context.AgentState.ContainsKey("turnMessageCount")),
                    AgentMessageTreatment.VisibleProgress),
                cancellationToken);

            yield return new AgentLogicEvent(
                "agent.progress",
                new LogicProgressPayload("direct-progress", context.RecentMessages.Count, context.AgentState.ContainsKey("turnMessageCount")),
                AgentMessageTreatment.VisibleProgress);

            yield return new AgentLogicEvent(
                "conversation.append",
                new ConversationAppendRequest("assistant", "Durable note from logic."),
                AgentMessageTreatment.ConversationAppend,
                ExposeToClient: false);

            yield return new AgentLogicEvent(
                "agent.state.update",
                new AgentStateUpdate(new Dictionary<string, JsonElement>
                {
                    ["turnMessageCount"] = JsonSerializer.SerializeToElement(context.RecentMessages.Count),
                    ["hadState"] = JsonSerializer.SerializeToElement(context.AgentState.ContainsKey("turnMessageCount"))
                }),
                AgentMessageTreatment.StateUpdate,
                ExposeToClient: false);

            var answer = context.AgentState.ContainsKey("turnMessageCount")
                ? "Logic answer with state."
                : "Logic answer without state.";
            yield return new AgentLogicEvent(
                "text.delta",
                new TextDeltaPayload(answer),
                AgentMessageTreatment.FinalAnswerDelta);
            yield return new AgentLogicEvent(
                "run.completed",
                new RunCompletedPayload(""),
                AgentMessageTreatment.RunEvent,
                SerializedRuntimeSessionState: """{"logic":"ok"}""");
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
            if (run.ThinkingPolicy.Enabled is true)
            {
                yield return new RuntimeStreamEvent(
                    "reasoning.started",
                    new { },
                    ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
                yield return new RuntimeStreamEvent(
                    "reasoning.delta",
                    new ReasoningDeltaPayload("private reasoning"),
                    ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
                yield return new RuntimeStreamEvent(
                    "reasoning.completed",
                    new { },
                    ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
            }

            yield return new RuntimeStreamEvent("text.delta", new TextDeltaPayload($"Echo: {run.Message}"));
            yield return new RuntimeStreamEvent("run.completed", new RunCompletedPayload(""), "{\"state\":\"ok\"}");
        }
    }

    private sealed class TestPrebuiltAgent(string id) : IPrebuiltAgentDefinition
    {
        public AgentDefinitionDto Descriptor { get; } = new(
            Id: id,
            Name: "Duplicate Agent",
            Description: "A duplicate test agent.",
            Source: "code",
            Instructions: "Test instructions.",
            Model: "test-model",
            ToolIds: [],
            MiddlewareIds: [],
            SkillIds: [],
            AllowedToolIds: [],
            AllowedMiddlewareIds: [],
            AllowedSkillIds: [],
            ContextPolicy: null,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch);
    }
}
