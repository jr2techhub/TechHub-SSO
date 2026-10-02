using TechHub.SSO.Api.Services;
using Xunit;

namespace TechHub.SSO.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_And_Verify_RoundTrips()
    {
        var hash = PasswordHasher.Hash("Sup3rSecret!");
        Assert.True(PasswordHasher.Verify("Sup3rSecret!", hash));
    }

    [Fact]
    public void Verify_Fails_For_Wrong_Password()
    {
        var hash = PasswordHasher.Hash("Sup3rSecret!");
        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_Is_Salted_Produces_Different_Digests()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Fact]
    public void Verify_Returns_False_On_Malformed_Hash()
    {
        Assert.False(PasswordHasher.Verify("x", "not-a-valid-hash"));
        Assert.False(PasswordHasher.Verify("x", ""));
    }
}
