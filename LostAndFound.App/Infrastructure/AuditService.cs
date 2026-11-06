using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Infrastructure;

public sealed class AuditService
{
    public void Write(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? actorUserId,
        string action,
        string targetType,
        string targetId,
        string? reason,
        DateTimeOffset createdAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AuditLogs (
                ActorUserId,
                Action,
                TargetType,
                TargetId,
                Reason,
                CreatedAtUtc)
            VALUES (
                $actorUserId,
                $action,
                $targetType,
                $targetId,
                $reason,
                $createdAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$actorUserId",
            actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$targetType", targetType);
        command.Parameters.AddWithValue("$targetId", targetId);
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", createdAt.ToString("O"));
        command.ExecuteNonQuery();
    }
}
