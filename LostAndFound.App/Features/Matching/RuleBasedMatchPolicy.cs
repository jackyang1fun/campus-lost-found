using System.Text;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Ports;

namespace LostAndFound.App.Features.Matching;

public sealed class RuleBasedMatchPolicy : IMatchPolicy
{
    public MatchScore Evaluate(PostingSummary target, PostingSummary candidate)
    {
        var reasons = new List<string>();
        var score = 0;

        if (target.Type == candidate.Type)
        {
            return new MatchScore(0, ["信息类型相同，不构成失物与招领配对"]);
        }

        if (target.CategoryId == candidate.CategoryId)
        {
            score += 40;
            reasons.Add("物品分类相同");
        }

        var locationOverlap = FindOverlap(target.Location, candidate.Location);
        if (locationOverlap.Count > 0)
        {
            score += 25;
            reasons.Add($"地点关键词相近：{string.Join("、", locationOverlap.Take(2))}");
        }

        var targetText = $"{target.Title} {target.Description}";
        var candidateText = $"{candidate.Title} {candidate.Description}";
        var contentOverlap = FindOverlap(targetText, candidateText);
        if (contentOverlap.Count > 0)
        {
            score += 20;
            reasons.Add($"描述关键词重叠：{string.Join("、", contentOverlap.Take(3))}");
        }

        var timeDistance = (target.OccurredAt - candidate.OccurredAt).Duration();
        if (timeDistance <= TimeSpan.FromDays(1))
        {
            score += 20;
            reasons.Add("发生时间相差不超过 1 天");
        }
        else if (timeDistance <= TimeSpan.FromDays(3))
        {
            score += 12;
            reasons.Add("发生时间相差不超过 3 天");
        }
        else if (timeDistance <= TimeSpan.FromDays(7))
        {
            score += 5;
            reasons.Add("发生时间相差不超过 7 天");
        }

        return new MatchScore(score, reasons);
    }

    private static IReadOnlyList<string> FindOverlap(string first, string second)
    {
        var firstTokens = Tokenize(first);
        var secondTokens = Tokenize(second);
        return firstTokens
            .Intersect(secondTokens, StringComparer.Ordinal)
            .Where(token => token.Length >= 2)
            .OrderByDescending(token => token.Length)
            .ThenBy(token => token, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> Tokenize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            AddSegmentTokens(current.ToString(), tokens);
            current.Clear();
        }

        AddSegmentTokens(current.ToString(), tokens);
        return tokens;
    }

    private static void AddSegmentTokens(string segment, HashSet<string> tokens)
    {
        if (segment.Length == 0)
        {
            return;
        }

        tokens.Add(segment);
        if (!segment.Any(IsCjk))
        {
            return;
        }

        for (var index = 0; index < segment.Length - 1; index++)
        {
            tokens.Add(segment.Substring(index, 2));
        }
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u9fff';
}
