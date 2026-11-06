using System.Globalization;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Features.Claims;

public sealed class ClaimService
{
    private readonly SqliteDatabase _database;
    private readonly AuditService _auditService;
    private readonly ClaimCompletionCoordinator _completionCoordinator;

    public ClaimService(
        SqliteDatabase database,
        AuditService auditService,
        ClaimCompletionCoordinator completionCoordinator)
    {
        _database = database;
        _auditService = auditService;
        _completionCoordinator = completionCoordinator;
    }

    public ClaimResult StartClaim(
        UserAccount initiator,
        long postingId,
        string claimStatement)
    {
        if (initiator.Status != UserStatus.Active)
        {
            return ClaimResult.Failure("当前账号不能发起认领。");
        }

        var normalizedStatement = claimStatement.Trim();
        if (normalizedStatement.Length is < 10 or > 200)
        {
            return ClaimResult.Failure("认领说明长度必须为 10 至 200 个字符。");
        }

        using var connection = _database.OpenConnection();
        var posting = ReadClaimablePosting(connection, postingId);
        if (posting is null)
        {
            return ClaimResult.Failure("该信息不存在或已不处于公开状态。");
        }

        if (posting.Value.PublisherUserId == initiator.Id)
        {
            return ClaimResult.Failure("不能认领自己发布的信息。");
        }

        using var transaction = connection.BeginTransaction();
        if (HasOpenClaim(connection, transaction, initiator.Id, postingId))
        {
            transaction.Rollback();
            return ClaimResult.Failure("你已经有一条针对该信息的进行中认领。");
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Claims (
                    PostingId,
                    InitiatorUserId,
                    PublisherUserId,
                    Status,
                    ClaimStatement,
                    CreatedAtUtc,
                    UpdatedAtUtc)
                VALUES (
                    $postingId,
                    $initiatorUserId,
                    $publisherUserId,
                    'Negotiating',
                    $claimStatement,
                    $createdAtUtc,
                    $updatedAtUtc);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$postingId", postingId);
            command.Parameters.AddWithValue("$initiatorUserId", initiator.Id);
            command.Parameters.AddWithValue("$publisherUserId", posting.Value.PublisherUserId);
            command.Parameters.AddWithValue("$claimStatement", normalizedStatement);
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            var claimId = Convert.ToInt64(command.ExecuteScalar());

            InsertSystemMessage(
                connection,
                transaction,
                claimId,
                "匿名会话已创建。双方仅显示匿名编号，所有沟通仅供认领双方查看。",
                now);

            _auditService.Write(
                connection,
                transaction,
                initiator.Id,
                "ClaimStarted",
                "Claim",
                claimId.ToString(CultureInfo.InvariantCulture),
                null,
                now);

            transaction.Commit();
            return ClaimResult.Success("认领会话已创建。", claimId);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            transaction.Rollback();
            return ClaimResult.Failure("你已经有一条针对该信息的进行中认领。");
        }
    }

    public IReadOnlyList<ClaimSummary> GetClaimsForUser(long userId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.Id,
                c.PostingId,
                p.Title AS PostingTitle,
                p.Type AS PostingType,
                c.InitiatorUserId,
                c.PublisherUserId,
                c.Status,
                c.ClosureReason,
                c.UpdatedAtUtc,
                (
                    SELECT Content
                    FROM ClaimMessages m
                    WHERE m.ClaimId = c.Id
                      AND m.IsSystem = 0
                    ORDER BY m.SentAtUtc DESC, m.Id DESC
                    LIMIT 1
                ) AS LastMessage
            FROM Claims c
            JOIN Postings p ON p.Id = c.PostingId
            WHERE c.InitiatorUserId = $userId
               OR c.PublisherUserId = $userId
            ORDER BY c.UpdatedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$userId", userId);

        using var reader = command.ExecuteReader();
        var claims = new List<ClaimSummary>();
        while (reader.Read())
        {
            var postingType = Enum.Parse<PostingType>(
                reader.GetString(reader.GetOrdinal("PostingType")));
            var initiatorId = reader.GetInt64(reader.GetOrdinal("InitiatorUserId"));
            var publisherId = reader.GetInt64(reader.GetOrdinal("PublisherUserId"));
            var isPublisher = publisherId == userId;
            var status = Enum.Parse<ClaimStatus>(reader.GetString(reader.GetOrdinal("Status")));

            claims.Add(new ClaimSummary(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetInt64(reader.GetOrdinal("PostingId")),
                reader.GetString(reader.GetOrdinal("PostingTitle")),
                postingType,
                AliasFor(postingType, isPublisher: isPublisher),
                AliasFor(postingType, isPublisher: !isPublisher),
                status,
                reader.IsDBNull(reader.GetOrdinal("LastMessage"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("LastMessage")),
                reader.IsDBNull(reader.GetOrdinal("ClosureReason"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ClosureReason")),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("UpdatedAtUtc")))));
        }

        return claims;
    }

    public ClaimConversation? GetConversation(UserAccount user, long claimId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                c.Id,
                c.PostingId,
                p.Title AS PostingTitle,
                p.Type AS PostingType,
                c.InitiatorUserId,
                c.PublisherUserId,
                c.Status,
                c.ClosureReason,
                c.ClaimStatement
            FROM Claims c
            JOIN Postings p ON p.Id = c.PostingId
            WHERE c.Id = $claimId
              AND (c.InitiatorUserId = $userId OR c.PublisherUserId = $userId)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$claimId", claimId);
        command.Parameters.AddWithValue("$userId", user.Id);

        long publisherUserId;
        long initiatorUserId;
        long postingId;
        ClaimStatus status;
        PostingType postingType;
        string posterTitle;
        string? closureReason;
        string claimStatement;

        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            publisherUserId = reader.GetInt64(reader.GetOrdinal("PublisherUserId"));
            initiatorUserId = reader.GetInt64(reader.GetOrdinal("InitiatorUserId"));
            postingId = reader.GetInt64(reader.GetOrdinal("PostingId"));
            status = Enum.Parse<ClaimStatus>(reader.GetString(reader.GetOrdinal("Status")));
            postingType = Enum.Parse<PostingType>(
                reader.GetString(reader.GetOrdinal("PostingType")));
            posterTitle = reader.GetString(reader.GetOrdinal("PostingTitle"));
            closureReason = reader.IsDBNull(reader.GetOrdinal("ClosureReason"))
                ? null
                : reader.GetString(reader.GetOrdinal("ClosureReason"));
            claimStatement = reader.IsDBNull(reader.GetOrdinal("ClaimStatement"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("ClaimStatement"));
        }

        var isPublisher = user.Id == publisherUserId;
        var messages = ReadMessages(
            connection,
            claimId,
            publisherUserId,
            initiatorUserId,
            postingType);

        return new ClaimConversation(
            claimId,
            postingId,
            posterTitle,
            postingType,
            claimStatement,
            AliasFor(postingType, isPublisher),
            AliasFor(postingType, !isPublisher),
            status,
            isPublisher,
            CanSendMessage: status == ClaimStatus.Negotiating,
            CanComplete: isPublisher && status == ClaimStatus.Negotiating,
            CanCancel: status == ClaimStatus.Negotiating,
            closureReason,
            messages);
    }

    public ClaimResult SendMessage(UserAccount sender, long claimId, string content)
    {
        var normalizedContent = content.Trim();
        if (normalizedContent.Length is < 1 or > 1_000)
        {
            return ClaimResult.Failure("消息长度必须为 1 至 1000 个字符。");
        }

        using var connection = _database.OpenConnection();
        if (!IsParticipant(connection, claimId, sender.Id))
        {
            return ClaimResult.Failure("你不是该认领的参与者。");
        }

        using var transaction = connection.BeginTransaction();
        if (!IsNegotiating(connection, transaction, claimId))
        {
            transaction.Rollback();
            return ClaimResult.Failure("当前会话已结束，不能继续发送消息。");
        }

        var now = DateTimeOffset.UtcNow;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ClaimMessages (
                    ClaimId,
                    SenderUserId,
                    Content,
                    IsSystem,
                    SentAtUtc)
                VALUES (
                    $claimId,
                    $senderUserId,
                    $content,
                    0,
                    $sentAtUtc);
                """;
            insert.Parameters.AddWithValue("$claimId", claimId);
            insert.Parameters.AddWithValue("$senderUserId", sender.Id);
            insert.Parameters.AddWithValue("$content", normalizedContent);
            insert.Parameters.AddWithValue("$sentAtUtc", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = """
                UPDATE Claims
                SET UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $claimId;
                """;
            touch.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            touch.Parameters.AddWithValue("$claimId", claimId);
            touch.ExecuteNonQuery();
        }

        transaction.Commit();
        return ClaimResult.Success("消息已发送。", claimId);
    }

    public ClaimResult CancelClaim(UserAccount user, long claimId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (!IsParticipant(connection, transaction, claimId, user.Id))
        {
            transaction.Rollback();
            return ClaimResult.Failure("你不是该认领的参与者。");
        }

        if (!IsNegotiating(connection, transaction, claimId))
        {
            transaction.Rollback();
            return ClaimResult.Failure("当前认领不能取消。");
        }

        var now = DateTimeOffset.UtcNow;
        using (var cancel = connection.CreateCommand())
        {
            cancel.Transaction = transaction;
            cancel.CommandText = """
                UPDATE Claims
                SET
                    Status = 'Cancelled',
                    ClosureReason = '认领双方之一主动取消了认领。',
                    UpdatedAtUtc = $now,
                    ClosedAtUtc = $now
                WHERE Id = $claimId
                  AND Status = 'Negotiating';
                """;
            cancel.Parameters.AddWithValue("$now", now.ToString("O"));
            cancel.Parameters.AddWithValue("$claimId", claimId);
            cancel.ExecuteNonQuery();
        }

        InsertSystemMessage(
            connection,
            transaction,
            claimId,
            "该认领会话已由参与者取消。",
            now);
        _auditService.Write(
            connection,
            transaction,
            user.Id,
            "ClaimCancelled",
            "Claim",
            claimId.ToString(CultureInfo.InvariantCulture),
            null,
            now);

        transaction.Commit();
        return ClaimResult.Success("认领已取消。", claimId);
    }

    public ClaimResult CompleteClaim(UserAccount publisher, long claimId) =>
        _completionCoordinator.TryComplete(claimId, publisher.Id);

    private static (long PublisherUserId, PostingStatus Status)? ReadClaimablePosting(
        SqliteConnection connection,
        long postingId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PublisherUserId, Status
            FROM Postings
            WHERE Id = $postingId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$postingId", postingId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var status = Enum.Parse<PostingStatus>(
            reader.GetString(reader.GetOrdinal("Status")));
        return status == PostingStatus.Approved
            ? (reader.GetInt64(reader.GetOrdinal("PublisherUserId")), status)
            : null;
    }

    private static bool HasOpenClaim(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long initiatorUserId,
        long postingId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Claims
                WHERE PostingId = $postingId
                  AND InitiatorUserId = $initiatorUserId
                  AND Status IN ('Negotiating', 'Disputed'));
            """;
        command.Parameters.AddWithValue("$postingId", postingId);
        command.Parameters.AddWithValue("$initiatorUserId", initiatorUserId);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static bool IsParticipant(
        SqliteConnection connection,
        long claimId,
        long userId) =>
        IsParticipant(
            connection,
            transaction: null,
            claimId: claimId,
            userId: userId);

    private static bool IsParticipant(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long claimId,
        long userId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Claims
                WHERE Id = $claimId
                  AND (InitiatorUserId = $userId OR PublisherUserId = $userId));
            """;
        command.Parameters.AddWithValue("$claimId", claimId);
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static bool IsNegotiating(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long claimId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Claims
                WHERE Id = $claimId
                  AND Status = 'Negotiating');
            """;
        command.Parameters.AddWithValue("$claimId", claimId);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static IReadOnlyList<ClaimMessage> ReadMessages(
        SqliteConnection connection,
        long claimId,
        long publisherUserId,
        long initiatorUserId,
        PostingType postingType)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SenderUserId, Content, IsSystem, SentAtUtc
            FROM ClaimMessages
            WHERE ClaimId = $claimId
            ORDER BY SentAtUtc, Id;
            """;
        command.Parameters.AddWithValue("$claimId", claimId);

        using var reader = command.ExecuteReader();
        var messages = new List<ClaimMessage>();
        while (reader.Read())
        {
            var isSystem = reader.GetInt32(reader.GetOrdinal("IsSystem")) == 1;
            var senderId = reader.IsDBNull(reader.GetOrdinal("SenderUserId"))
                ? (long?)null
                : reader.GetInt64(reader.GetOrdinal("SenderUserId"));
            var senderAlias = isSystem || senderId is null
                ? "系统"
                : AliasFor(postingType, isPublisher: senderId == publisherUserId);

            messages.Add(new ClaimMessage(
                reader.GetInt64(reader.GetOrdinal("Id")),
                senderAlias,
                reader.GetString(reader.GetOrdinal("Content")),
                isSystem,
                ParseTimestamp(reader.GetString(reader.GetOrdinal("SentAtUtc")))));
        }

        return messages;
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

    private static string AliasFor(PostingType postingType, bool isPublisher) =>
        postingType == PostingType.Lost
            ? isPublisher ? "失主A" : "拾获人B"
            : isPublisher ? "拾获人A" : "失主B";

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
