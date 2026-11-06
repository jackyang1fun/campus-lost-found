using LostAndFound.App.Features.Listings;
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

    public MatchingService(
        ListingService listingService,
        IMatchPolicy matchPolicy)
    {
        _listingService = listingService;
        _matchPolicy = matchPolicy;
    }

    public IReadOnlyList<MatchSuggestion> GetSuggestions(PostingSummary target)
    {
        return _listingService
            .GetPublicPostings(searchTerm: null, categoryId: null)
            .Where(candidate => candidate.Id != target.Id)
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
}
