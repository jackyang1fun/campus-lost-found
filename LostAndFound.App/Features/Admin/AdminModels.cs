using LostAndFound.App.Domain;

namespace LostAndFound.App.Features.Admin;

public sealed record AdminUserRow(
    long Id,
    string CampusCardNumber,
    string DisplayName,
    string Contact,
    UserRole Role,
    UserStatus Status,
    DateTimeOffset CreatedAt);

public sealed record AuditLogRow(
    long Id,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record AdminStats(
    int TotalUsers,
    int PendingPostings,
    int PublicPostings,
    int OpenDisputes);

public sealed record AdminActionResult(bool Succeeded, string Message)
{
    public static AdminActionResult Success(string message) => new(true, message);

    public static AdminActionResult Failure(string message) => new(false, message);
}
