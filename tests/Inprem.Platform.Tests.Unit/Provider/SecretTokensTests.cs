using Inprem.Platform.Application.Provider;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Provider;

public class SecretTokensTests
{
    [Fact]
    public void Hash_Is_Lowercase_Hex_Sha256_As_The_Runbook_Computes_It()
    {
        // Same value as the runbook's PowerShell for "abc" (FIPS 180-2 test vector).
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecretTokens.Hash("abc"));
        Assert.Equal(SecretTokens.Hash("abc"), SecretTokens.Hash("  abc \n")); // pasted codes may carry spaces
    }

    [Fact]
    public void Matches_Compares_With_The_Stored_Hash_Only()
    {
        var stored = SecretTokens.Hash("CODE-1");
        Assert.True(SecretTokens.Matches("CODE-1", stored));
        Assert.True(SecretTokens.Matches("CODE-1", stored.ToUpperInvariant()));
        Assert.False(SecretTokens.Matches("CODE-2", stored));
        Assert.False(SecretTokens.Matches("CODE-1", null));
        Assert.False(SecretTokens.Matches("CODE-1", ""));
        Assert.False(SecretTokens.Matches(stored, stored)); // the hash itself is not the code
    }

    [Fact]
    public void New_Codes_And_Tokens_Are_Random_And_Well_Formed()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => SecretTokens.NewCode()).ToList();
        Assert.Equal(50, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-HJ-NP-Z2-9]{5}(-[A-HJ-NP-Z2-9]{5}){5}$", c));

        var tokens = Enumerable.Range(0, 50).Select(_ => SecretTokens.NewToken()).ToList();
        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]{43}$", t));
    }
}
