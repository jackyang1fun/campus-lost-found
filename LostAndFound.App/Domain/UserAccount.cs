namespace LostAndFound.App.Domain;

public enum UserRole
{
    User,
    Administrator
}

public enum UserStatus
{
    Active,
    Deactivated,
    Banned
}

public sealed record UserAccount(
    long Id,
    string CampusCardNumber,
    string DisplayName,
    string Contact,
    UserRole Role,
    UserStatus Status);
