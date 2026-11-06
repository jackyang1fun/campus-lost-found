using LostAndFound.App.Features.Auth;

namespace LostAndFound.Tests.Features.Auth;

public sealed class LocalCampusCardVerifierTests
{
    private readonly LocalCampusCardVerifier _verifier = new();

    [Theory]
    [InlineData(" card-2026001 ", "CARD-2026001")]
    [InlineData("A12345", "A12345")]
    public void TryNormalize_AcceptsSupportedCampusCardNumbers(
        string input,
        string expected)
    {
        var accepted = _verifier.TryNormalize(input, out var normalized);

        Assert.True(accepted);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("CARD 2026")]
    [InlineData("CARD_2026")]
    public void TryNormalize_RejectsUnsupportedCampusCardNumbers(string input)
    {
        Assert.False(_verifier.TryNormalize(input, out _));
    }
}
