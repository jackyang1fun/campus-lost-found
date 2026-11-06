using LostAndFound.App.Features.Listings;

namespace LostAndFound.App.Features.Claims;

public enum ClaimStatus
{
    Negotiating,
    Completed,
    Cancelled,
    Disputed,
    DisputeClosed
}

public sealed record ClaimSummary(
    long Id,
    long PostingId,
    string PostingTitle,
    PostingType PostingType,
    string MyAlias,
    string CounterpartyAlias,
    ClaimStatus Status,
    string? LastMessage,
    string? ClosureReason,
    DateTimeOffset UpdatedAt);

public sealed record ClaimMessage(
    long Id,
    string SenderAlias,
    string Content,
    bool IsSystem,
    DateTimeOffset SentAt);

public sealed record ClaimConversation(
    long ClaimId,
    long PostingId,
    string PostingTitle,
    PostingType PostingType,
    string MyAlias,
    string CounterpartyAlias,
    ClaimStatus Status,
    bool IsPublisher,
    bool CanSendMessage,
    bool CanComplete,
    bool CanCancel,
    string? ClosureReason,
    IReadOnlyList<ClaimMessage> Messages);

public sealed record ClaimResult(bool Succeeded, string Message, long? ClaimId = null)
{
    public static ClaimResult Success(string message, long? claimId = null) =>
        new(true, message, claimId);

    public static ClaimResult Failure(string message) =>
        new(false, message);
}

public static class ClaimText
{
    public static string Status(ClaimStatus status) =>
        status switch
        {
            ClaimStatus.Negotiating => "协商中",
            ClaimStatus.Completed => "已完成",
            ClaimStatus.Cancelled => "已取消",
            ClaimStatus.Disputed => "争议中",
            ClaimStatus.DisputeClosed => "争议已关闭",
            _ => status.ToString()
        };
}
