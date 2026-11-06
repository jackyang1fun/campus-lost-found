using LostAndFound.App.Domain;

namespace LostAndFound.App.Features.Auth;

public sealed record RegistrationRequest(
    string CampusCardNumber,
    string DisplayName,
    string Contact,
    string Password,
    bool PrivacyAccepted);

public sealed record LoginRequest(string CampusCardNumber, string Password);

public sealed record AuthResult(bool Succeeded, string Message, UserAccount? User = null)
{
    public static AuthResult Success(UserAccount user, string message) =>
        new(true, message, user);

    public static AuthResult Failure(string message) =>
        new(false, message);
}
