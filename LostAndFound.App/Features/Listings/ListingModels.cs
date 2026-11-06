namespace LostAndFound.App.Features.Listings;

public enum PostingType
{
    Lost,
    Found
}

public enum PostingStatus
{
    PendingReview,
    Approved,
    Rejected,
    Resolved,
    Cancelled,
    Deleted
}

public sealed record Category(int Id, string Name);

public sealed record CreatePostingRequest(
    PostingType Type,
    string Title,
    int CategoryId,
    DateTimeOffset OccurredAt,
    string Location,
    string Description,
    string Contact,
    string? ImageSourcePath,
    string? ImageAltText);

public sealed record PostingSummary(
    long Id,
    PostingType Type,
    string Title,
    int CategoryId,
    string CategoryName,
    DateTimeOffset OccurredAt,
    string Location,
    string Description,
    string? ImageRelativePath,
    string? ImageAltText,
    string Contact,
    long PublisherUserId,
    string PublisherName,
    PostingStatus Status,
    string? ReviewReason,
    DateTimeOffset CreatedAt);

public sealed record ListingResult(bool Succeeded, string Message, long? PostingId = null)
{
    public static ListingResult Success(string message, long? postingId = null) =>
        new(true, message, postingId);

    public static ListingResult Failure(string message) =>
        new(false, message);
}
