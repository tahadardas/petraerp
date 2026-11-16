using PetraERP.Application.Common;

namespace PetraERP.Application.Tests.Security;

public sealed class ConcurrencyTokenTests
{
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];
    private static readonly byte[] NextVersion = [0, 0, 0, 0, 0, 0, 0x07, 0xD2];
    private static readonly byte[] ShortVersion = [1, 2, 3];

    [Fact]
    public void CanonicalBase64RoundTripsAndMatchesTheStoredBytes()
    {
        var token = ConcurrencyToken.FromBytes(Version);
        var text = token.ToBase64();
        Assert.Equal(12, text.Length);
        Assert.True(ConcurrencyToken.TryParse(text, out var parsed));
        Assert.Equal(token, parsed);
        Assert.True(parsed.Matches(Version));
        Assert.False(parsed.Matches(NextVersion));
        Assert.False(parsed.Matches(ShortVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAB9E")]
    [InlineData("AAAAAAAAB9E==")]
    [InlineData("AAAAAAAAB9F=")]
    [InlineData("AAAAAAAAAAAAAAAA")]
    [InlineData("not-base64!!")]
    public void NonCanonicalOrMalformedVersionsAreRejected(string? text)
    {
        Assert.False(ConcurrencyToken.TryParse(text, out _));
    }
}
