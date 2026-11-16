using PetraERP.Application.Security;

namespace PetraERP.Application.Tests.Security;

public sealed class LoginNameTests
{
    [Theory]
    [InlineData("  Admin.User  ", "Admin.User", "ADMIN.USER")]
    [InlineData("a_b-c", "a_b-c", "A_B-C")]
    public void ValidNamesAreTrimmedAndNormalizedToInvariantUpperCase(string input, string userName, string normalized)
    {
        Assert.True(LoginName.TryNormalize(input, out var actualName, out var actualNormalized));
        Assert.Equal(userName, actualName);
        Assert.Equal(normalized, actualNormalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ab")]
    [InlineData("مستخدم")]
    [InlineData("user name")]
    [InlineData("аdmin")]
    [InlineData("user@domain")]
    public void ConfusableUnicodeSpacesAndShortNamesAreRejected(string? input)
    {
        Assert.False(LoginName.TryNormalize(input, out _, out _));
    }

    [Fact]
    public void LookupNormalizationIsBoundedForAudit()
    {
        Assert.Equal(64, LoginName.NormalizeForLookup(new string('x', 500)).Length);
        Assert.Equal(string.Empty, LoginName.NormalizeForLookup(null));
    }
}
