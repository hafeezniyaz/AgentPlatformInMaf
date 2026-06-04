# Agent Platform Console Client

API-only console frontend for testing the Agent Platform backend.

## Run

Start the backend API, then run:

```bash
dotnet run --project AgentPlatform.ConsoleClient -- --base-url http://localhost:5001
```

The base URL can also be set with `AGENT_PLATFORM_API_URL` or `API_BASE_URL`.

## Common Commands

```text
/catalog
/agents
/agents/{agentId}
/agents/create
/agents/update/{agentId}
/tools
/middleware
/skills
/models
/context
/thinking
/sessions
/sessions/{sessionId}
/sessions/{sessionId}/messages
/sessions/{sessionId}/delete
/run
/raw GET /api/catalog
/exit
```

Commands accept `key=value` arguments. Omit required values to be prompted interactively.
