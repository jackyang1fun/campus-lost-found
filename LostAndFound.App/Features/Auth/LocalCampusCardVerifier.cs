using System.Text.RegularExpressions;
using LostAndFound.App.Ports;

namespace LostAndFound.App.Features.Auth;

public sealed partial class LocalCampusCardVerifier : IIdentityVerifier
{
    public bool TryNormalize(string campusCardNumber, out string normalizedCampusCardNumber)
    {
        normalizedCampusCardNumber = (campusCardNumber ?? string.Empty).Trim().ToUpperInvariant();
        return CampusCardPattern().IsMatch(normalizedCampusCardNumber);
    }

    [GeneratedRegex("^[A-Z0-9-]{6,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex CampusCardPattern();
}
