namespace LostAndFound.App.Ports;

public interface IIdentityVerifier
{
    bool TryNormalize(string campusCardNumber, out string normalizedCampusCardNumber);
}
