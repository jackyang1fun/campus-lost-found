using LostAndFound.App.Features.Listings;
using LostAndFound.App.Infrastructure;
using LostAndFound.App.Ports;

namespace LostAndFound.App.Features.Matching;

public sealed record MatchSuggestion(
    PostingSummary Candidate,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed class MatchingService
{
    private const int MaximumSuggestions = 10;
    private readonly ListingService _listingService;
    private readonly IMatchPolicy _matchPolicy;
    private readonly SqliteDatabase _database;

    public MatchingService(
        ListingService listingService,
        IMatchPolicy matchPolicy,
        SqliteDatabase database)
    {
        _listingService = listingService;
        _matchPolicy = matchPolicy;
        _database = database;
    }

    public IReadOnlyList<MatchSuggestion> GetSuggestions(
        PostingSummary target,
        long? viewerUserId = null)
    {
        var ignoredCandidateIds = viewerUserId.HasValue
            ? GetIgnoredCandidateIds(viewerUserId.Value, target.Id)
            : new HashSet<long>();

        return _listingService
            .GetPublicPostings(searchTerm: null, categoryId: null)
            .Where(candidate => candidate.Id != target.Id)
            .Where(candidate => !ignoredCandidateIds.Contains(candidate.Id))
            .Select(candidate =>
            {
                var score = _matchPolicy.Evaluate(target, candidate);
                return new MatchSuggestion(candidate, score.Score, score.Reasons);
            })
            .Where(suggestion => suggestion.Reasons.Count >= 2 && suggestion.Score >= 40)
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenByDescending(suggestion => suggestion.Candidate.CreatedAt)
            .Take(MaximumSuggestions)
            .ToArray();
    }

    public void IgnoreSuggestion(long userId, long targetPostingId, long candidatePostingId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO IgnoredMatches (
                UserId, TargetPostingId, CandidatePostingId, CreatedAtUtc)
            VALUES (
                $userId, $targetPostingId, $candidatePostingId, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$targetPostingId", targetPostingId);
        command.Parameters.AddWithValue("$candidatePostingId", candidatePostingId);
        command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private HashSet<long> GetIgnoredCandidateIds(long userId, long targetPostingId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CandidatePostingId
            FROM IgnoredMatches
            WHERE UserId = $userId
              AND TargetPostingId = $targetPostingId;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$targetPostingId", targetPostingId);

        using var reader = command.ExecuteReader();
        var result = new HashSet<long>();
        while (reader.Read())
        {
            result.Add(reader.GetInt64(0));
        }
        return result;
    }
}
