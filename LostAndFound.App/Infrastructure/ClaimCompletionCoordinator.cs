using LostAndFound.App.Features.Claims;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Infrastructure;

public sealed class ClaimCompletionCoordinator
{
    private readonly SqliteDatabase _database;
    private readonly AuditService _auditService;

    public ClaimCompletionCoordinator(
        SqliteDatabase database,
        AuditService auditService)
    {
        _database = database;
        _auditService = auditService;
    }

    public ClaimResult TryComplete(long claimId, long publisherUserId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var claim = ReadClaim(connection, transaction, claimId);
            if (claim is null)
            {
                transaction.Rollback();
                return ClaimResult.Failure("认领记录不存在。");
            }

            if (claim.Value.PublisherUserId != publisherUserId)
            {
                transaction.Rollback();
                return ClaimResult.Failure("只有信息发布者可以确认交接完成。");
            }

            if (claim.Value.Status != ClaimStatus.Negotiating)
            {
                transaction.Rollback();
                return ClaimResult.Failure("当前认领不处于可完成状态。");
            }

            var now = DateTimeOffset.UtcNow;
            using (var completeClaim = connection.CreateCommand())
            {
                completeClaim.Transaction = transaction;
                completeClaim.CommandText = """
                    UPDATE Claims
                    SET
                        Status = 'Completed',
                        UpdatedAtUtc = $now,
                        ClosedAtUtc = $now
                    WHERE Id = $claimId
                      AND Status = 'Negotiating';
                    """;
                completeClaim.Parameters.AddWithValue("$now", now.ToString("O"));
                completeClaim.Parameters.AddWithValue("$claimId", claimId);
                if (completeClaim.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return ClaimResult.Failure("认领状态已发生变化，请刷新后重试。");
                }
            }

            using (var resolvePosting = connection.CreateCommand())
            {
                resolvePosting.Transaction = transaction;
                resolvePosting.CommandText = """
                    UPDATE Postings
                    SET
                        Status = 'Resolved',
                        Version = Version + 1,
                        UpdatedAtUtc = $now
                    WHERE Id = $postingId
                      AND Status = 'Approved';
                    """;
                resolvePosting.Parameters.AddWithValue("$now", now.ToString("O"));
                resolvePosting.Parameters.AddWithValue("$postingId", claim.Value.PostingId);
                if (resolvePosting.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return ClaimResult.Failure("关联信息已发生变化，无法完成认领。");
                }
            }

            using (var closeOtherClaims = connection.CreateCommand())
            {
                closeOtherClaims.Transaction = transaction;
                closeOtherClaims.CommandText = """
                    UPDATE Claims
                    SET
                        Status = 'Cancelled',
                        ClosureReason = '物品已由其他认领完成交接。',
                        UpdatedAtUtc = $now,
                        ClosedAtUtc = $now
                    WHERE PostingId = $postingId
                      AND Id <> $claimId
                      AND Status = 'Negotiating';
                    """;
                closeOtherClaims.Parameters.AddWithValue("$now", now.ToString("O"));
                closeOtherClaims.Parameters.AddWithValue("$postingId", claim.Value.PostingId);
                closeOtherClaims.Parameters.AddWithValue("$claimId", claimId);
                closeOtherClaims.ExecuteNonQuery();
            }

            InsertSystemMessage(
                connection,
                transaction,
                claimId,
                "信息发布者已确认交接完成，该认领已关闭。",
                now);

            _auditService.Write(
                connection,
                transaction,
                publisherUserId,
                "ClaimCompleted",
                "Claim",
                claimId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                now);

            transaction.Commit();
            return ClaimResult.Success("交接已确认，认领和物品状态已更新。", claimId);
        }
        catch (SqliteException)
        {
            transaction.Rollback();
            return ClaimResult.Failure("保存认领完成状态失败，请重试。");
        }
    }

    private static ClaimParties? ReadClaim(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long claimId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT PostingId, PublisherUserId, Status
            FROM Claims
            WHERE Id = $claimId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$claimId", claimId);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ClaimParties(
                reader.GetInt64(reader.GetOrdinal("PostingId")),
                reader.GetInt64(reader.GetOrdinal("PublisherUserId")),
                Enum.Parse<ClaimStatus>(reader.GetString(reader.GetOrdinal("Status"))))
            : null;
    }

    private static void InsertSystemMessage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long claimId,
        string content,
        DateTimeOffset sentAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ClaimMessages (
                ClaimId,
                SenderUserId,
                Content,
                IsSystem,
                SentAtUtc)
            VALUES (
                $claimId,
                NULL,
                $content,
                1,
                $sentAtUtc);
            """;
        command.Parameters.AddWithValue("$claimId", claimId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$sentAtUtc", sentAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private readonly record struct ClaimParties(
        long PostingId,
        long PublisherUserId,
        ClaimStatus Status);
}
