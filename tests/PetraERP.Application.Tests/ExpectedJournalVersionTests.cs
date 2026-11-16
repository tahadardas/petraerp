using PetraERP.Application.GeneralLedger.Posting;

namespace PetraERP.Application.Tests;

public sealed class ExpectedJournalVersionTests
{
    [Fact]
    public void VersionIsOpaqueImmutableAndLosslesslyRoundTripsItsEightBytes()
    {
        byte[] input = [0, 1, 2, 3, 4, 5, 254, 255];
        var version = ExpectedJournalVersion.Create(input);
        input[0] = 9;
        var output = version.ToBytes();
        output[1] = 9;
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 254, 255 }, version.ToBytes());
        Assert.Equal("AAECAwQF/v8=", version.ToBase64());
        Assert.Equal(version, ExpectedJournalVersion.FromBase64(version.ToBase64()));
        Assert.NotEqual(version, ExpectedJournalVersion.Create(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void VersionMustContainExactlyEightBytes(int size) =>
        Assert.Throws<ArgumentException>(() => ExpectedJournalVersion.Create(new byte[size]));

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData(" AAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAB=")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("!!!!!!!!!!!=")]
    public void VersionRejectsNoncanonicalOrInvalidBase64(string encoded) =>
        Assert.Throws<FormatException>(() => ExpectedJournalVersion.FromBase64(encoded));

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void PersistedFingerprintRequiresAnExactSha256Digest(string encoded) =>
        Assert.Throws<FormatException>(() => PostingFingerprint.FromHex(encoded));
}
