using System.Globalization;
using LostAndFound.App.Domain;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Features.Disputes;

public sealed class DisputeService
{
    private readonly SqliteDatabase _database;
    private readonly AuditService _auditService;

    public DisputeService(SqliteDatabase database, AuditService auditService)
    {
        _database = database;
        _auditService = auditService;
    }

    public DisputeResult OpenDispute(
        UserAccount user,
        long claimId,
        string reasonCategory,
        string description)
    {
        if (string.IsNullOrWhiteSpace(reasonCategory))
        {
            return DisputeResult.Failure("请选择争议原因。");
        }

        if (description.Trim().Length is < 5 or > 1_000)
        {
            return DisputeResult.Failure("争议说明长度必须为 5 至 1000 个字符。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (!IsParticipant(connection, transaction, claimId, user.Id))
        {
            transaction.Rollback();
            return DisputeResult.Failure("你不是该认领的参与者。");
        }

        if (DisputeExists(connection, transaction, claimId))
        {
            transaction.Rollback();
            return DisputeResult.Failure("该认领已经存在争议记录。");
        }

        var now = DateTimeOffset.UtcNow;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Disputes (
                    ClaimId,
                    OpenedByUserId,
                    ReasonCategory,
                    Description,
                    Status,
                    CreatedAtUtc,
                    UpdatedAtUtc)
                VALUES (
                    $claimId,
                    $openedByUserId,
                    $reasonCategory,
                    $description,
                    'Open',
                    $createdAtUtc,
                    $updatedAtUtc);
                """;
            insert.Parameters.AddWithValue("$claimId", claimId);
            insert.Parameters.AddWithValue("$openedByUserId", user.Id);
            insert.Parameters.AddWithValue("$reasonCategory", reasonCategory.Trim());
            insert.Parameters.AddWithValue("$description", description.Trim());
            insert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var updateClaim = connection.CreateCommand())
        {
            updateClaim.Transaction = transaction;
            updateClaim.CommandText = """
                UPDATE Claims
                SET
                    Status = 'Disputed',
                    ClosureReason = '双方已提交争议，等待管理员处理。',
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $claimId
                  AND Status IN ('Negotiating', 'Completed');
                """;
            updateClaim.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            updateClaim.Parameters.AddWithValue("$claimId", claimId);
            if (updateClaim.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return DisputeResult.Failure("当前认领状态不能提交争议。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            user.Id,
            "DisputeOpened",
            "Claim",
            claimId.ToString(CultureInfo.InvariantCulture),
            reasonCategory,
            now);
        transaction.Commit();
        return DisputeResult.Success("争议已提交，管理员处理后会通知双方。");
    }

    public IReadOnlyList<DisputeSummary> GetDisputes(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return [];
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.Id,
                d.ClaimId,
                p.Title AS PostingTitle,
                u.DisplayName AS OpenedByName,
                d.ReasonCategory,
                d.Status,
                d.CreatedAtUtc,
                d.UpdatedAtUtc
            FROM Disputes d
            JOIN Claims c ON c.Id = d.ClaimId
            JOIN Postings p ON p.Id = c.PostingId
            JOIN Users u ON u.Id = d.OpenedByUserId
            ORDER BY
                CASE d.Status WHEN 'Open' THEN 0 WHEN 'NeedMoreInfo' THEN 1 ELSE 2 END,
                d.UpdatedAtUtc DESC;
            """;

        using var reader = command.ExecuteReader();
        var disputes = new List<DisputeSummary>();
        while (reader.Read())
        {
            disputes.Add(new DisputeSummary(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetInt64(reader.GetOrdinal("ClaimId")),
                reader.GetString(reader.GetOrdinal("PostingTitle")),
                reader.GetString(reader.GetOrdinal("OpenedByName")),
                reader.GetString(reader.GetOrdinal("ReasonCategory")),
                Enum.Parse<DisputeStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("UpdatedAtUtc")))));
        }
        return disputes;
    }

    public DisputeDetails? GetDisputeDetails(UserAccount administrator, long disputeId)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    d.Id,
                    d.ClaimId,
                    d.ReasonCategory,
                    d.Description,
                    d.Status,
                    d.AdminConclusion,
                    d.CreatedAtUtc,
                    d.UpdatedAtUtc,
                    p.Title AS PostingTitle,
                    p.Type AS PostingType,
                    opener.DisplayName AS OpenedByName,
                    opener.CampusCardNumber AS OpenedByCard,
                    opener.Contact AS OpenedByContact,
                    publisher.DisplayName AS PublisherName,
                    publisher.CampusCardNumber AS PublisherCard,
                    publisher.Contact AS PublisherContact,
                    initiator.DisplayName AS InitiatorName,
                    initiator.CampusCardNumber AS InitiatorCard,
                    initiator.Contact AS InitiatorContact,
                    c.PublisherUserId AS PublisherUserId,
                    c.InitiatorUserId AS InitiatorUserId
                FROM Disputes d
                JOIN Claims c ON c.Id = d.ClaimId
                JOIN Postings p ON p.Id = c.PostingId
                JOIN Users opener ON opener.Id = d.OpenedByUserId
                JOIN Users publisher ON publisher.Id = c.PublisherUserId
                JOIN Users initiator ON initiator.Id = c.InitiatorUserId
                WHERE d.Id = $disputeId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$disputeId", disputeId);

            DisputeSummary summary;
            DisputeUserIdentity publisherIdentity;
            DisputeUserIdentity initiatorIdentity;
            string description;
            string? conclusion;
            long publisherUserId;
            long initiatorUserId;

            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    transaction.Rollback();
                    return null;
                }

                summary = new DisputeSummary(
                    reader.GetInt64(reader.GetOrdinal("Id")),
                    reader.GetInt64(reader.GetOrdinal("ClaimId")),
                    reader.GetString(reader.GetOrdinal("PostingTitle")),
                    reader.GetString(reader.GetOrdinal("OpenedByName")),
                    reader.GetString(reader.GetOrdinal("ReasonCategory")),
                    Enum.Parse<DisputeStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                    ParseTimestamp(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))),
                    ParseTimestamp(reader.GetString(reader.GetOrdinal("UpdatedAtUtc"))));

                publisherIdentity = new DisputeUserIdentity(
                    reader.GetString(reader.GetOrdinal("PublisherName")),
                    reader.GetString(reader.GetOrdinal("PublisherCard")),
                    reader.GetString(reader.GetOrdinal("PublisherContact")));
                initiatorIdentity = new DisputeUserIdentity(
                    reader.GetString(reader.GetOrdinal("InitiatorName")),
                    reader.GetString(reader.GetOrdinal("InitiatorCard")),
                    reader.GetString(reader.GetOrdinal("InitiatorContact")));
                description = reader.GetString(reader.GetOrdinal("Description"));
                conclusion = reader.IsDBNull(reader.GetOrdinal("AdminConclusion"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("AdminConclusion"));
                publisherUserId = reader.GetInt64(reader.GetOrdinal("PublisherUserId"));
                initiatorUserId = reader.GetInt64(reader.GetOrdinal("InitiatorUserId"));
            }

            var messages = ReadMessages(
                connection,
                transaction,
                summary.ClaimId,
                publisherUserId,
                initiatorUserId);

            _auditService.Write(
                connection,
                transaction,
                administrator.Id,
                "DisputeSensitiveAccess",
                "Dispute",
                disputeId.ToString(CultureInfo.InvariantCulture),
                "管理员处理争议",
                DateTimeOffset.UtcNow);
            transaction.Commit();

            return new DisputeDetails(
                summary,
                initiatorIdentity,
                publisherIdentity,
                description,
                conclusion,
                messages);
        }
    }

    public DisputeResult ResolveDispute(
        UserAccount administrator,
        long disputeId,
        bool needMoreInformation,
        string conclusion)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return DisputeResult.Failure("只有管理员可以处理争议。");
        }

        if (!needMoreInformation && conclusion.Trim().Length < 5)
        {
            return DisputeResult.Failure("关闭争议时，处理结论至少需要 5 个字符。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var status = needMoreInformation ? "NeedMoreInfo" : "Closed";

        using (var updateDispute = connection.CreateCommand())
        {
            updateDispute.Transaction = transaction;
            updateDispute.CommandText = """
                UPDATE Disputes
                SET
                    Status = $status,
                    AdminConclusion = $conclusion,
                    ClosedByUserId = $closedByUserId,
                    UpdatedAtUtc = $now,
                    ClosedAtUtc = CASE WHEN $status = 'Closed' THEN $now ELSE NULL END
                WHERE Id = $disputeId
                  AND Status IN ('Open', 'NeedMoreInfo');
                """;
            updateDispute.Parameters.AddWithValue("$status", status);
            updateDispute.Parameters.AddWithValue(
                "$conclusion",
                string.IsNullOrWhiteSpace(conclusion) ? DBNull.Value : conclusion.Trim());
            updateDispute.Parameters.AddWithValue("$closedByUserId", administrator.Id);
            updateDispute.Parameters.AddWithValue("$now", now.ToString("O"));
            updateDispute.Parameters.AddWithValue("$disputeId", disputeId);
            if (updateDispute.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return DisputeResult.Failure("争议不存在或已经处理。");
            }
        }

        if (!needMoreInformation)
        {
            using var updateClaim = connection.CreateCommand();
            updateClaim.Transaction = transaction;
            updateClaim.CommandText = """
                UPDATE Claims
                SET
                    Status = 'DisputeClosed',
                    ClosureReason = $conclusion,
                    UpdatedAtUtc = $now,
                    ClosedAtUtc = $now
                WHERE Id = (SELECT ClaimId FROM Disputes WHERE Id = $disputeId);
                """;
            updateClaim.Parameters.AddWithValue(
                "$conclusion",
                $"争议已处理：{conclusion.Trim()}");
            updateClaim.Parameters.AddWithValue("$now", now.ToString("O"));
            updateClaim.Parameters.AddWithValue("$disputeId", disputeId);
            updateClaim.ExecuteNonQuery();
        }

        _auditService.Write(
            connection,
            transaction,
            administrator.Id,
            needMoreInformation ? "DisputeNeedMoreInfo" : "DisputeClosed",
            "Dispute",
            disputeId.ToString(CultureInfo.InvariantCulture),
            conclusion,
            now);
        transaction.Commit();
        return DisputeResult.Success(needMoreInformation ? "已要求双方补充说明。" : "争议已关闭。");
    }

    private static bool IsParticipant(
        SqliteConnection connection,
        SqliteTransaction transaction,
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

    private static bool DisputeExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long claimId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Disputes WHERE ClaimId = $claimId);";
        command.Parameters.AddWithValue("$claimId", claimId);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static IReadOnlyList<ClaimMessage> ReadMessages(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long claimId,
        long publisherUserId,
        long initiatorUserId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
            var senderAlias = "系统";
            if (!isSystem && senderId.HasValue)
            {
                senderAlias = senderId == publisherUserId
                    ? "发布者"
                    : senderId == initiatorUserId ? "认领人" : "未知参与者";
            }

            messages.Add(new ClaimMessage(
                reader.GetInt64(reader.GetOrdinal("Id")),
                senderAlias,
                reader.GetString(reader.GetOrdinal("Content")),
                isSystem,
                ParseTimestamp(reader.GetString(reader.GetOrdinal("SentAtUtc")))));
        }
        return messages;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
