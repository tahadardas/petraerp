using PetraERP.Application.Security;

namespace PetraERP.Application.Tests.Security;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("كلمة سر طويلة جداً", null)]
    [InlineData("correct horse battery", null)]
    [InlineData("", PasswordPolicyViolation.Required)]
    [InlineData("short", PasswordPolicyViolation.TooShort)]
    [InlineData("          ", PasswordPolicyViolation.WhitespaceOnly)]
    [InlineData("xx-Admin.User-xx", PasswordPolicyViolation.ContainsUserName)]
    [InlineData("valid\u0007password", PasswordPolicyViolation.InvalidCharacters)]
    public void PolicyAcceptsLongPassphrasesAndRejectsWeakInput(string password, PasswordPolicyViolation? expected)
    {
        Assert.Equal(expected, PasswordPolicy.Validate(password, "admin.user"));
    }

    [Fact]
    public void PolicyRejectsOverlongPasswordsAndMapsStableCodes()
    {
        Assert.Equal(PasswordPolicyViolation.TooLong, PasswordPolicy.Validate(new string('a', 129), "user"));
        Assert.Null(PasswordPolicy.Validate(new string('a', 128), "user"));
        Assert.Equal("security.password_policy.too_short", PasswordPolicy.ErrorCode(PasswordPolicyViolation.TooShort));
        Assert.Equal("security.password_policy.reuses_current", PasswordPolicy.ErrorCode(PasswordPolicyViolation.ReusesCurrentPassword));
    }

    [Fact]
    public void LoneSurrogatesAreRejectedButValidSupplementaryCharactersAreAccepted()
    {
        Assert.Equal(PasswordPolicyViolation.InvalidCharacters, PasswordPolicy.Validate("broken" + '\uD800' + "surrogate", "user"));
        Assert.Null(PasswordPolicy.Validate("passphrase 🏛 with emoji", "user"));
    }
}
