namespace PetraERP.Application.Security;

public sealed class AuthenticationOutcome
{
    private AuthenticationOutcome(SessionTokens? tokens, AuthenticationFailure? failure,
        DateTimeOffset? lockoutEndUtc, PasswordPolicyViolation? violation)
    {
        Tokens = tokens;
        Failure = failure;
        LockoutEndUtc = lockoutEndUtc;
        PolicyViolation = violation;
    }

    public bool Succeeded => Tokens is not null;
    public SessionTokens? Tokens { get; }
    public AuthenticationFailure? Failure { get; }
    public DateTimeOffset? LockoutEndUtc { get; }
    public PasswordPolicyViolation? PolicyViolation { get; }

    public static AuthenticationOutcome Success(SessionTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new AuthenticationOutcome(tokens, null, null, null);
    }

    public static AuthenticationOutcome Fail(AuthenticationFailure failure, DateTimeOffset? lockoutEndUtc = null) =>
        new(null, failure, lockoutEndUtc, null);

    public static AuthenticationOutcome PolicyFailure(PasswordPolicyViolation violation) =>
        new(null, AuthenticationFailure.PasswordPolicyViolation, null, violation);
}
