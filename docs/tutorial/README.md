# Rebuild the ASP.NET Core Agent Platform From Scratch

This tutorial plan is a guided development path for rebuilding the agent platform incrementally. It is written for a developer who wants to learn the architecture while building the backend, not just copy code.

By the end, the developer will have a complete ASP.NET Core Web API with EF Core SQLite persistence, Microsoft Agent Framework runtime integration, tools, middleware, skills, sessions, streaming, context compression, OpenAI and vLLM model support, and preserved thinking.

## Tutorial Style

Every module adds one working layer to the project. Every chapter solves one problem and leaves the project in a better, runnable state.

Use this pattern for every chapter:

- **Problem**: the small thing this chapter solves.
- **Intuition**: the simple idea behind the design.
- **Build**: the files or behavior to add.
- **Checkpoint**: how to know the chapter worked.

Keep explanations short. The goal is guided development, not a reference manual.

## Final Project Shape

The finished solution uses five projects:

- `AgentPlatform.Api`: HTTP endpoints, SSE streaming, app startup.
- `AgentPlatform.Core`: DTOs, service contracts, orchestration, policy resolution.
- `AgentPlatform.Infrastructure.Sqlite`: EF Core entities, SQLite stores, MAF runtime, chat history provider.
- `AgentPlatform.Agents`: trusted prebuilt agents and static tool, middleware, skill catalog.
- `AgentPlatform.Tests`: focused unit and integration tests.

## Module 1: Project Foundation

Goal: create the solution skeleton and establish clean boundaries.

### Chapter 1. Create The Solution Structure

**Problem**: Put API, domain logic, infrastructure, prebuilt agents, and tests in separate projects.

**Intuition**: Each project should have one reason to change. The API changes for HTTP concerns. Core changes for platform rules. Infrastructure changes for storage or provider details.

**Build**:

- Create the solution.
- Add the five projects.
- Add project references so dependencies point inward:
  - API references Core, Infrastructure, Agents.
  - Infrastructure references Core.
  - Agents references Core.
  - Tests reference all relevant projects.

**Checkpoint**: `dotnet build` succeeds with empty projects.

### Chapter 2. Add Package References

**Problem**: Bring in the framework capabilities needed by the backend.

**Intuition**: Packages are building blocks. Add only the blocks needed by the layer that uses them.

**Build**:

- API: ASP.NET Core OpenAPI support.
- Infrastructure: EF Core SQLite and Microsoft Agent Framework packages.
- Tests: xUnit and ASP.NET Core test host support.

**Checkpoint**: restore and build succeed after package installation.

### Chapter 3. Wire Dependency Injection

**Problem**: Let each project register its own services.

**Intuition**: Dependency injection is the plugboard. Core asks for contracts; Infrastructure provides implementations.

**Build**:

- Add `AddAgentPlatformCore()`.
- Add `AddAgentPlatformSqlite(...)`.
- Add `AddPrebuiltAgentCatalog()`.
- Call these from `Program.cs`.

**Checkpoint**: API starts and the container can resolve placeholder services.

## Module 2: Core Domain Contracts

Goal: define the stable shapes that the rest of the project will use.

### Chapter 1. Define Agent DTOs

**Problem**: Represent both trusted prebuilt agents and user-created config agents.

**Intuition**: DTOs are the public shape of a concept. They should be boring, clear, and frontend-friendly.

**Build**:

- Add `AgentDefinitionDto`.
- Add `CreateAgentRequest`.
- Add `UpdateAgentRequest`.
- Include source, instructions, model, selected tools, middleware, skills, and optional policies.

**Checkpoint**: tests can create DTOs for both code and user agents.

### Chapter 2. Define Catalog DTOs

**Problem**: Return everything the frontend can choose from.

**Intuition**: The catalog is the menu. The frontend should not invent agent, tool, skill, middleware, or model IDs.

**Build**:

- Add `CatalogResponse`.
- Add `CatalogItemDto`.
- Add context, thinking, and model capability DTOs.

**Checkpoint**: a catalog object can represent agents, tools, middleware, skills, context profiles, thinking modes, and models.

### Chapter 3. Define Run And Session DTOs

**Problem**: Describe streaming runs and conversation history.

**Intuition**: A run is temporary; a session is durable. The frontend streams a run, then rehydrates from a session.

**Build**:

- Add `StreamRunRequest`.
- Add `RunStreamEvent`.
- Add payloads for text, reasoning, run started, completed, and error.
- Add session summary, details, message, and paged message DTOs.

**Checkpoint**: DTOs cover both streaming and sidebar history needs.

### Chapter 4. Define Service Interfaces

**Problem**: Separate platform behavior from storage and provider details.

**Intuition**: Core should define what needs to happen. Infrastructure decides how it happens.

**Build**:

- Add catalog, user-agent store, conversation store, reasoning trace store, model catalog, policy resolver, and runtime interfaces.
- Add `StoredSession` and `AgentRunSpec`.

**Checkpoint**: Core compiles without referencing EF Core, OpenAI, SQLite, or ASP.NET Core.

## Module 3: SQLite Database With EF Core

Goal: create durable storage for agents, sessions, messages, run events, and reasoning traces.

### Chapter 1. Add EF Entities

**Problem**: Persist the platform's main concepts.

**Intuition**: Entities are database versions of domain concepts. They may contain storage details that DTOs hide.

**Build**:

- Add `UserAgentEntity`.
- Add `ChatSessionEntity`.
- Add `ChatMessageEntity`.
- Add `RunEventEntity`.
- Add `ReasoningTraceEntity`.

**Checkpoint**: all entities have stable IDs, timestamps, and session relationships where needed.

### Chapter 2. Build `AgentPlatformDbContext`

**Problem**: Map entities to SQLite tables.

**Intuition**: `DbContext` is the database gateway. It knows which tables exist and how they are indexed.

**Build**:

- Add `DbSet` properties.
- Configure keys and indexes.
- Add max lengths for common IDs and category fields.

**Checkpoint**: EF can create the schema from the model.

### Chapter 3. Add Schema Initialization

**Problem**: Create and evolve the local SQLite schema.

**Intuition**: During early development, additive schema updates keep the tutorial easy to follow without requiring a migration lesson on day one.

**Build**:

- Add `EnsureAgentPlatformSchemaAsync`.
- Use `EnsureCreatedAsync`.
- Add missing columns when the schema evolves.
- Create the `ReasoningTraces` table and indexes if missing.

**Checkpoint**: running the API creates or updates the SQLite database.

### Chapter 4. Store JSON-Backed Selections

**Problem**: Persist selected tool, middleware, skill, context, and thinking configuration.

**Intuition**: Plugin-style selections change shape over time. JSON columns are useful snapshots for flexible configuration.

**Build**:

- Add a `JsonList` helper for selected ID arrays.
- Store policy snapshots as JSON.
- Keep visible messages normalized, not buried in JSON.

**Checkpoint**: a session row contains selection snapshots, while messages remain queryable.

## Module 4: User Agents And Prebuilt Agents

Goal: support trusted backend agents and user-created config agents in one catalog.

### Chapter 1. Add Trusted Prebuilt Agents

**Problem**: Register backend-owned C# agents.

**Intuition**: Prebuilt agents are safe defaults because their logic ships with the backend.

**Build**:

- Add `IPrebuiltAgentDefinition`.
- Add a general assistant.
- Add an agent-builder assistant.
- Give each agent default tools, middleware, skills, and allowed IDs.

**Checkpoint**: prebuilt agents are available from DI.

### Chapter 2. Add User-Created Config Agents

**Problem**: Let users define agents without uploading executable code.

**Intuition**: Config agents are safer than code agents. Users can change instructions and selections, but not run arbitrary backend code.

**Build**:

- Store name, description, instructions, model, tools, middleware, skills, and policies.
- Generate stable slug IDs.

**Checkpoint**: a user agent can be created and stored.

### Chapter 3. Implement Agent CRUD Storage

**Problem**: Create, update, and retrieve user agents from SQLite.

**Intuition**: The frontend needs durable configuration, not temporary memory.

**Build**:

- Implement `IUserAgentStore`.
- Serialize selection IDs and policies.
- Convert entities back into `AgentDefinitionDto`.

**Checkpoint**: tests can create, update, and fetch user agents.

### Chapter 4. Merge Code And User Agents In One Catalog

**Problem**: Return prebuilt and user agents consistently.

**Intuition**: The frontend should not need separate screens for code agents and user agents.

**Build**:

- Implement `AgentCatalogService`.
- Merge prebuilt descriptors with user-agent records.
- Preserve `source: "code"` and `source: "user"`.

**Checkpoint**: `/api/catalog` can return both agent types.

## Module 5: Tools, Middleware, And Skills Catalog

Goal: expose plug-and-play capabilities the frontend can select.

### Chapter 1. Define Tools

**Problem**: Expose callable backend capabilities.

**Intuition**: Tools are actions. The model asks for them when text alone is not enough.

**Build**:

- Add catalog items for clock, calculator, and weather.
- Later map these IDs to MAF `AITool` functions.

**Checkpoint**: tools appear in the catalog.

### Chapter 2. Define Middleware

**Problem**: Add run behavior such as safety, logging, and timing.

**Intuition**: Middleware changes how an agent runs. It is not the agent's knowledge.

**Build**:

- Add middleware catalog items.
- Start with safety, logging, and timing.

**Checkpoint**: middleware IDs can be selected and validated.

### Chapter 3. Define Skills

**Problem**: Add prompt/context packages that teach the agent a capability.

**Intuition**: Skills are knowledge and guidance. They belong in MAF context providers.

**Build**:

- Add a skill catalog item.
- Add a local `SKILL.md` file.
- Use skill metadata for dependencies.

**Checkpoint**: selected skills can be discovered by ID.

### Chapter 4. Add Skill-Required Tools

**Problem**: Automatically include tools required by selected skills.

**Intuition**: Choosing a skill should bring its dependencies, just like installing a package.

**Build**:

- Read `requiredTools` metadata.
- Expand selected tools before validation.

**Checkpoint**: selecting a skill injects its required tool IDs into the session configuration.

## Module 6: Public API Surface

Goal: expose the backend features through frontend-ready endpoints.

### Chapter 1. Build Catalog Endpoints

**Problem**: Let the frontend list available choices.

**Intuition**: The catalog endpoint is discovery. The frontend asks what is possible before building a run request.

**Build**:

- Add `GET /api/catalog`.
- Return agents, tools, middleware, skills, context capabilities, thinking capabilities, and models.

**Checkpoint**: browser or curl returns a complete catalog JSON document.

### Chapter 2. Build User Agent CRUD Endpoints

**Problem**: Let users create and edit config agents.

**Intuition**: Validation keeps dynamic configuration safe.

**Build**:

- Add `POST /api/agents`.
- Add `GET /api/agents/{agentId}`.
- Add `PUT /api/agents/{agentId}`.
- Return clear errors for invalid input.

**Checkpoint**: user agents can be managed through HTTP.

### Chapter 3. Build Session List And Detail Endpoints

**Problem**: Support sidebar history and click-to-rehydrate.

**Intuition**: Rehydration means the UI can rebuild its state from stored session data.

**Build**:

- Add `GET /api/sessions`.
- Add `GET /api/sessions/{sessionId}`.
- Include selected agent, tools, middleware, skills, policies, model metadata, and ordered messages.

**Checkpoint**: a frontend can load conversation summaries and then open one session.

### Chapter 4. Build Paged Messages And Archive Endpoints

**Problem**: Handle long conversations and hide old sessions.

**Intuition**: Pagination keeps large conversations cheap. Archive hides sessions without destroying history immediately.

**Build**:

- Add `GET /api/sessions/{sessionId}/messages`.
- Add `DELETE /api/sessions/{sessionId}` as archive.

**Checkpoint**: long histories can be paged and sessions can be hidden from the sidebar.

## Module 7: Conversation Persistence

Goal: make every conversation durable and restorable.

### Chapter 1. Create Sessions

**Problem**: Start a durable conversation with selected configuration.

**Intuition**: A session is a snapshot of how the agent was built for that conversation.

**Build**:

- Implement `CreateSessionAsync`.
- Store agent, title, selected IDs, policies, model metadata, and config hash.

**Checkpoint**: a run can create a session row.

### Chapter 2. Store Visible Chat Messages

**Problem**: Persist user and assistant messages for the frontend.

**Intuition**: Visible chat history should be clean and simple. It is for humans and UI rendering.

**Build**:

- Implement `AddMessageAsync`.
- Store role, content, sequence, and timestamp.
- Return messages ordered by sequence.

**Checkpoint**: session details return readable chat history.

### Chapter 3. Store Run Events

**Problem**: Keep streamed event history for debugging and replay.

**Intuition**: Run events are the backend trace of what happened during streaming.

**Build**:

- Implement `AddRunEventAsync`.
- Store event name, payload JSON, and timestamp.

**Checkpoint**: every streamed run can leave an event trail.

### Chapter 4. Add Configuration Hash Checks

**Problem**: Avoid restoring incompatible agent state.

**Intuition**: A config hash is a fingerprint. If it changes, the old opaque session state may no longer be safe.

**Build**:

- Hash agent ID, model, tools, middleware, skills, context policy, and thinking policy.
- Reject incompatible session reuse.
- Allow model switching only through explicit model compatibility behavior.

**Checkpoint**: tests block unsafe reuse when selections change.

## Module 8: MAF Runtime And Streaming

Goal: execute agents through Microsoft Agent Framework and stream responses.

### Chapter 1. Build `AgentRunOrchestrator`

**Problem**: Connect catalog, validation, storage, policy resolution, and runtime.

**Intuition**: The orchestrator is the conductor. It does not play every instrument; it coordinates them.

**Build**:

- Resolve agent.
- Normalize selected tools, middleware, and skills.
- Expand skill-required tools.
- Resolve model, context policy, and thinking policy.
- Create or restore a session.

**Checkpoint**: invalid IDs fail before runtime execution.

### Chapter 2. Build OpenAI-Backed MAF Runtime

**Problem**: Create an `AIAgent` with tools, skills, chat history, and streaming.

**Intuition**: MAF is the engine. The backend configures the agent and lets MAF handle the run loop.

**Build**:

- Create an OpenAI `IChatClient`.
- Build tools from selected IDs.
- Build skill providers from selected skills.
- Attach `SqliteChatHistoryProvider`.

**Checkpoint**: a fake or real run can stream assistant text.

### Chapter 3. Emit SSE Events

**Problem**: Stream updates to the frontend.

**Intuition**: SSE is a simple stream of named events over HTTP.

**Build**:

- Add `POST /api/runs/stream`.
- Emit `run.started`, `text.delta`, `run.completed`, and `run.error`.

**Checkpoint**: curl or browser receives `text/event-stream` output.

### Chapter 4. Persist After Streaming

**Problem**: Turn a live stream into durable history.

**Intuition**: Streaming is temporary. Persistence makes it reloadable.

**Build**:

- Collect assistant deltas.
- Save user and assistant messages.
- Save serialized MAF session state.
- Update session preview.

**Checkpoint**: after a stream finishes, session detail shows the new messages.

## Module 9: MAF Chat History Provider

Goal: bridge SQLite history with MAF's session-aware context pipeline.

### Chapter 1. Build SQLite Chat History Provider

**Problem**: Load prior messages into MAF runs.

**Intuition**: `ChatHistoryProvider` is where durable storage becomes model context.

**Build**:

- Implement `ProvideChatHistoryAsync`.
- Read the current session ID from `AgentSession.StateBag`.
- Load ordered messages from SQLite.

**Checkpoint**: the second turn includes the first turn's history.

### Chapter 2. Store Compacted Prompt Snapshots

**Problem**: Support persisted prompt mode without losing full UI history.

**Intuition**: The model prompt and frontend transcript are related but not the same thing.

**Build**:

- Implement `StoreChatHistoryAsync` for persisted prompt mode.
- Store compacted prompt snapshots separately from `ChatMessages`.

**Checkpoint**: full chat history remains visible while compacted prompt state is stored separately.

### Chapter 3. Restore Serialized `AgentSession`

**Problem**: Continue framework-native agent state.

**Intuition**: Opaque session state belongs to MAF. Store and restore it, but do not hand-edit it.

**Build**:

- Serialize session after the run.
- Deserialize it on compatible future runs.
- Use config hash checks before restore.

**Checkpoint**: compatible session runs reuse serialized MAF state.

## Module 10: Context Management And Compression

Goal: keep long conversations usable without losing frontend history.

### Chapter 1. Add Context Policy Options

**Problem**: Configure in-flight and persisted prompt modes.

**Intuition**: Context policy is the rulebook for long conversations.

**Build**:

- Add global context options.
- Add per-agent context policy override.
- Add context metadata to sessions.

**Checkpoint**: catalog exposes context capabilities and default policy.

### Chapter 2. Build Policy Resolver

**Problem**: Merge global defaults and per-agent overrides.

**Intuition**: Defaults keep setup easy. Overrides let one agent behave differently.

**Build**:

- Implement `ContextPolicyResolver`.
- Validate supported modes and profiles.
- Fill missing values from defaults.

**Checkpoint**: invalid context values throw clear validation errors.

### Chapter 3. Build Compaction Provider Factory

**Problem**: Create the balanced compaction pipeline.

**Intuition**: Compaction shrinks what the model sees, not necessarily what the user sees.

**Build**:

- Add tool-result compaction.
- Add summarization compaction.
- Add sliding-window compaction.
- Add truncation compaction.

**Checkpoint**: tests verify strategy ordering without calling OpenAI.

### Chapter 4. Register Compaction Through MAF

**Problem**: Ensure compression runs inside the tool-calling loop.

**Intuition**: Registering compaction on the chat client keeps MAF's runtime behavior intact.

**Build**:

- Use `ChatClientBuilder.UseAIContextProviders(...)`.
- Keep agent-level history storage separate.

**Checkpoint**: runtime builds agents with compaction provider when policy enables it.

## Module 11: Model Catalog And Model Switching

Goal: support approved OpenAI and vLLM models without trusting arbitrary client URLs.

### Chapter 1. Add Model Catalog Options

**Problem**: Configure models server-side.

**Intuition**: A model catalog is a safe list. The frontend chooses IDs, not raw provider endpoints.

**Build**:

- Add `AgentPlatform:Models`.
- Include model ID, provider, base URL, compatibility group, context window, and optional thinking policy.

**Checkpoint**: `/api/catalog` returns configured models.

### Chapter 2. Resolve Model Metadata Per Run

**Problem**: Turn a model ID into executable provider settings.

**Intuition**: Model resolution hides provider details from the frontend and orchestrator.

**Build**:

- Implement `IModelCatalog`.
- Resolve unknown models to OpenAI defaults or reject them based on tutorial choice.
- Pass resolved metadata into `AgentRunSpec`.

**Checkpoint**: runs include resolved model metadata.

### Chapter 3. Handle Model Switching

**Problem**: Continue visible chat while protecting incompatible framework state.

**Intuition**: Visible messages are portable. MAF session state and reasoning traces are provider-specific.

**Build**:

- Detect model changes on existing sessions.
- Reuse visible chat.
- Reset opaque MAF session state when needed.
- Replay reasoning only for compatible model groups.

**Checkpoint**: switching model does not leak incompatible hidden state.

### Chapter 4. Include Model Data In Session Hash

**Problem**: Prevent unsafe state reuse after provider or model changes.

**Intuition**: Compatibility must be explicit. A matching hash means safe restore.

**Build**:

- Add model ID, provider, compatibility group, context policy, and thinking policy to hash payload.

**Checkpoint**: tests prove model and policy changes affect the session hash.

## Module 12: Preserved Thinking For vLLM

Goal: support hidden reasoning for vLLM OpenAI-compatible models while keeping MAF as the agent engine.

### Chapter 1. Add Thinking Policy DTOs

**Problem**: Configure disabled, interleaved, and preserved thinking.

**Intuition**: Thinking policy controls hidden model reasoning, not visible chat.

**Build**:

- Add `ThinkingPolicyDto`.
- Add thinking capabilities.
- Add defaults to options.

**Checkpoint**: catalog returns supported thinking modes and captures.

### Chapter 2. Add Thinking Resolver

**Problem**: Resolve session, agent, model, and global thinking settings.

**Intuition**: Existing sessions should stay stable even if defaults change later.

**Build**:

- Implement resolver precedence: session, agent, model, global.
- Validate modes, capture values, max tokens, and raw JSON shape.

**Checkpoint**: tests prove precedence and validation.

### Chapter 3. Add Reasoning Trace Persistence

**Problem**: Store hidden reasoning separately from chat messages.

**Intuition**: Reasoning traces are backend replay data. They are not frontend conversation history.

**Build**:

- Implement `IReasoningTraceStore`.
- Save, load, prune, and count traces.
- Keep `ChatMessages` reasoning-free.

**Checkpoint**: session details show reasoning stats, not raw reasoning content.

### Chapter 4. Build vLLM `IChatClient` Adapter

**Problem**: Support OpenAI-compatible vLLM without bypassing MAF.

**Intuition**: The adapter translates provider HTTP details while MAF still handles agent behavior.

**Build**:

- Implement `IChatClient`.
- Send `/v1/chat/completions`.
- Merge raw thinking JSON at the root request body.
- Reject collisions with protected keys such as `model`, `messages`, `tools`, and `stream`.

**Checkpoint**: tests prove raw request JSON is merged correctly.

### Chapter 5. Capture And Replay Reasoning

**Problem**: Read `reasoning` and `reasoning_content`, then replay preserved state.

**Intuition**: Hidden reasoning is model-specific context. Replay it only when compatibility is explicit.

**Build**:

- Convert streamed reasoning to `TextReasoningContent`.
- Store reasoning traces after runs.
- Reinject reasoning into assistant messages for preserved mode.

**Checkpoint**: preserved mode stores reasoning and future runs can load it.

### Chapter 6. Gate Debug Reasoning SSE

**Problem**: Expose reasoning only when explicitly enabled.

**Intuition**: Hidden reasoning should stay hidden by default for safety and clean UX.

**Build**:

- Emit reasoning events internally.
- Forward `reasoning.started`, `reasoning.delta`, and `reasoning.completed` only when `ExposeToClient = true`.

**Checkpoint**: tests prove reasoning events are suppressed by default and emitted in debug mode.

## Module 13: Tests And Verification

Goal: prove each platform layer works and prevent regressions.

### Chapter 1. Test Catalog And User Agent CRUD

**Problem**: Prove the frontend can discover and manage agents.

**Intuition**: Tests are executable API contracts.

**Build**:

- Test prebuilt agents.
- Test user agent create and update.
- Test catalog tools, middleware, skills, context, thinking, and models.

**Checkpoint**: catalog tests pass.

### Chapter 2. Test Sessions And Chat History

**Problem**: Prove sidebar history and rehydration work.

**Intuition**: Ordered messages are the foundation of reliable UI restore.

**Build**:

- Test session creation.
- Test ordered messages.
- Test session details shape.
- Test archived-session filtering.

**Checkpoint**: session tests pass.

### Chapter 3. Test Policy Resolution And Hash Behavior

**Problem**: Prove context, thinking, and model configuration are stable.

**Intuition**: State bugs often hide in configuration inheritance. Test the inheritance.

**Build**:

- Test context resolver.
- Test thinking resolver.
- Test config hash mismatch.
- Test stored session policy snapshots.

**Checkpoint**: invalid reuse is blocked.

### Chapter 4. Test vLLM Request And Reasoning Behavior

**Problem**: Prove vLLM-specific behavior without requiring a live vLLM server.

**Intuition**: Fake streaming tests make provider behavior safe to test locally.

**Build**:

- Test raw JSON root merge.
- Test protected-key rejection.
- Test reasoning trace storage.
- Test hidden visible history.
- Test optional reasoning SSE.

**Checkpoint**: vLLM behavior tests pass without network calls.

### Chapter 5. Manual Smoke Test

**Problem**: Run the finished backend like a frontend would.

**Intuition**: Smoke tests catch wiring mistakes that unit tests can miss.

**Build**:

- Run the API.
- Call `/api/catalog`.
- Create a user agent.
- Start a streaming run.
- Open session details.
- Optionally test with real OpenAI or vLLM credentials.

**Checkpoint**: a full conversation can be created, streamed, listed, and rehydrated.

## Suggested Writing Order

Write the full tutorial in the same order as the modules. Do not introduce a concept before the project has a reason to need it.

Recommended rhythm:

1. Explain the small problem.
2. Add the smallest useful code.
3. Run a quick build or test.
4. Explain what became possible.
5. Move to the next chapter.

## Final Outcome Checklist

The final project should include:

- ASP.NET Core Web API endpoints.
- EF Core SQLite persistence.
- trusted prebuilt agents.
- user-created config agents.
- tools, middleware, and skills.
- skill-required tool expansion.
- frontend-ready catalog and session APIs.
- SSE streaming runs.
- MAF-native chat history and session restore.
- configurable context compression.
- model catalog for OpenAI and vLLM.
- preserved thinking stored separately from visible chat history.
- tests covering catalog, agents, sessions, policies, streaming, vLLM request merge, and reasoning trace behavior.

## Assumptions

- The tutorial targets .NET 10.
- The tutorial uses the Microsoft Agent Framework packages already chosen by the project.
- User-created agents are configuration records, not uploaded executable code.
- vLLM endpoints are configured server-side through `AgentPlatform:Models`.
- Real OpenAI and vLLM smoke tests are optional because they require credentials or a running local model server.
