using System.Globalization;
using LostAndFound.App.Domain;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Features.Admin;

public sealed class AdminService
{
    private readonly SqliteDatabase _database;
    private readonly AuditService _auditService;

    public AdminService(SqliteDatabase database, AuditService auditService)
    {
        _database = database;
        _auditService = auditService;
    }

    public IReadOnlyList<AdminUserRow> GetUsers(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return [];
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CampusCardNumber, DisplayName, Contact, Role, Status, CreatedAtUtc
            FROM Users
            ORDER BY CreatedAtUtc DESC;
            """;

        using var reader = command.ExecuteReader();
        var users = new List<AdminUserRow>();
        while (reader.Read())
        {
            users.Add(new AdminUserRow(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetString(reader.GetOrdinal("CampusCardNumber")),
                reader.GetString(reader.GetOrdinal("DisplayName")),
                reader.GetString(reader.GetOrdinal("Contact")),
                Enum.Parse<UserRole>(reader.GetString(reader.GetOrdinal("Role"))),
                Enum.Parse<UserStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("CreatedAtUtc")))));
        }
        return users;
    }

    public AdminActionResult SetUserStatus(
        UserAccount administrator,
        long targetUserId,
        UserStatus status,
        string reason)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return AdminActionResult.Failure("只有管理员可以管理用户。");
        }

        if (administrator.Id == targetUserId && status != UserStatus.Active)
        {
            return AdminActionResult.Failure("管理员不能封禁或停用自己的账号。");
        }

        if (status != UserStatus.Active && string.IsNullOrWhiteSpace(reason))
        {
            return AdminActionResult.Failure("停用或封禁用户时必须填写原因。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Users
                SET Status = $status
                WHERE Id = $userId;
                """;
            update.Parameters.AddWithValue("$status", status.ToString());
            update.Parameters.AddWithValue("$userId", targetUserId);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return AdminActionResult.Failure("用户不存在。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            administrator.Id,
            status == UserStatus.Active ? "UserReactivated" : "UserStatusChanged",
            "User",
            targetUserId.ToString(CultureInfo.InvariantCulture),
            reason,
            DateTimeOffset.UtcNow);
        transaction.Commit();
        return AdminActionResult.Success(
            status == UserStatus.Active ? "用户已恢复为正常状态。" : "用户状态已更新。");
    }

    public IReadOnlyList<AuditLogRow> GetRecentAuditLogs(
        UserAccount administrator,
        int limit = 200)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return [];
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                a.Id,
                COALESCE(u.DisplayName, '系统') AS ActorName,
                a.Action,
                a.TargetType,
                a.TargetId,
                a.Reason,
                a.CreatedAtUtc
            FROM AuditLogs a
            LEFT JOIN Users u ON u.Id = a.ActorUserId
            ORDER BY a.CreatedAtUtc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        using var reader = command.ExecuteReader();
        var logs = new List<AuditLogRow>();
        while (reader.Read())
        {
            logs.Add(new AuditLogRow(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetString(reader.GetOrdinal("ActorName")),
                reader.GetString(reader.GetOrdinal("Action")),
                reader.GetString(reader.GetOrdinal("TargetType")),
                reader.GetString(reader.GetOrdinal("TargetId")),
                reader.IsDBNull(reader.GetOrdinal("Reason"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("Reason")),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("CreatedAtUtc")))));
        }
        return logs;
    }

    public AdminStats GetStats(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return new AdminStats(0, 0, 0, 0);
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM Users) AS TotalUsers,
                (SELECT COUNT(*) FROM Postings WHERE Status = 'PendingReview') AS PendingPostings,
                (SELECT COUNT(*) FROM Postings WHERE Status = 'Approved') AS PublicPostings,
                (SELECT COUNT(*) FROM Disputes WHERE Status IN ('Open', 'NeedMoreInfo')) AS OpenDisputes;
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        return new AdminStats(
            Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("TotalUsers"))),
            Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("PendingPostings"))),
            Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("PublicPostings"))),
            Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("OpenDisputes"))));
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
