using LostAndFound.App.Features.Listings;

namespace LostAndFound.App.Ports;

public sealed record MatchScore(int Score, IReadOnlyList<string> Reasons);

public interface IMatchPolicy
{
    MatchScore Evaluate(PostingSummary target, PostingSummary candidate);
}
