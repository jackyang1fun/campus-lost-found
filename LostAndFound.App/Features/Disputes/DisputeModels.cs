using LostAndFound.App.Features.Claims;

namespace LostAndFound.App.Features.Disputes;

public enum DisputeStatus
{
    Open,
    NeedMoreInfo,
    Closed
}

public sealed record DisputeSummary(
    long Id,
    long ClaimId,
    string PostingTitle,
    string OpenedByDisplayName,
    string ReasonCategory,
    DisputeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DisputeUserIdentity(
    string DisplayName,
    string CampusCardNumber,
    string Contact);

public sealed record DisputeDetails(
    DisputeSummary Summary,
    DisputeUserIdentity Initiator,
    DisputeUserIdentity Publisher,
    string Description,
    string? AdminConclusion,
    IReadOnlyList<ClaimMessage> Messages);

public sealed record DisputeResult(bool Succeeded, string Message)
{
    public static DisputeResult Success(string message) => new(true, message);

    public static DisputeResult Failure(string message) => new(false, message);
}

public static class DisputeText
{
    public static string Status(DisputeStatus status) =>
        status switch
        {
            DisputeStatus.Open => "待处理",
            DisputeStatus.NeedMoreInfo => "待补充",
            DisputeStatus.Closed => "已关闭",
            _ => status.ToString()
        };
}
