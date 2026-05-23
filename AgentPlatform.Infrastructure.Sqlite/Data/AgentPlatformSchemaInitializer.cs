using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public static class AgentPlatformSchemaInitializer
{
    public static async Task EnsureAgentPlatformSchemaAsync(
        this AgentPlatformDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (!dbContext.Database.IsSqlite())
        {
            return;
        }

        await AddColumnIfMissingAsync(dbContext, "UserAgents", "ContextPolicyJson", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "UserAgents", "ThinkingPolicyJson", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ContextPolicyJson", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ContextMode", "TEXT NOT NULL DEFAULT 'inFlight'", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ContextProfile", "TEXT NOT NULL DEFAULT 'balanced'", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "CompactedPromptSnapshotJson", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "LastCompactionStatsJson", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "LastCompactedAt", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ThinkingPolicyJson", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ThinkingMode", "TEXT NOT NULL DEFAULT 'disabled'", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ThinkingCapture", "TEXT NOT NULL DEFAULT 'opaque'", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ReasoningTraceCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "LastReasoningTokenEstimate", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ModelId", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ModelProvider", "TEXT NOT NULL DEFAULT 'openai'", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ModelBaseUrl", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ModelCompatibilityGroup", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(dbContext, "ChatSessions", "ModelContextWindowTokens", "INTEGER NULL", cancellationToken);
        await CreateReasoningTracesTableAsync(dbContext, cancellationToken);
    }

    private static async Task CreateReasoningTracesTableAsync(
        AgentPlatformDbContext dbContext,
        CancellationToken cancellationToken)
    {
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ReasoningTraces" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ReasoningTraces" PRIMARY KEY,
                "SessionId" TEXT NOT NULL,
                "MessageId" TEXT NULL,
                "TurnSequence" INTEGER NOT NULL,
                "Role" TEXT NOT NULL,
                "Model" TEXT NOT NULL,
                "ReasoningContentJson" TEXT NOT NULL,
                "TokenEstimate" INTEGER NOT NULL,
                "CaptureMode" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ReasoningTraces_SessionId_TurnSequence" ON "ReasoningTraces" ("SessionId", "TurnSequence");""",
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ReasoningTraces_SessionId_CreatedAt" ON "ReasoningTraces" ("SessionId", "CreatedAt");""",
            cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task AddColumnIfMissingAsync(
        AgentPlatformDbContext dbContext,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(dbContext, tableName, columnName, cancellationToken))
        {
            return;
        }

        // Identifiers and definitions are hard-coded by the caller above, not user input.
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition}",
            cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task<bool> ColumnExistsAsync(
        AgentPlatformDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
