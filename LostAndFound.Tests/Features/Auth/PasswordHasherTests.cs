using LostAndFound.App.Features.Auth;

namespace LostAndFound.Tests.Features.Auth;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_AcceptsCorrectPasswordAndRejectsWrongPassword()
    {
        const string password = "Campus#2026";
        var hash = PasswordHasher.Hash(password);

        Assert.True(PasswordHasher.Verify(password, hash));
        Assert.False(PasswordHasher.Verify("WrongPassword", hash));
    }

    [Fact]
    public void Hash_UsesUniqueSaltForTheSamePassword()
    {
        const string password = "SamePassword1";

        var first = PasswordHasher.Hash(password);
        var second = PasswordHasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(password, first, StringComparison.Ordinal);
    }
}
