namespace PetraERP.Application.Security;

public enum PasswordPolicyViolation
{
    Required = 1,
    TooShort = 2,
    TooLong = 3,
    WhitespaceOnly = 4,
    ContainsUserName = 5,
    InvalidCharacters = 6,
    ReusesCurrentPassword = 7,
}
