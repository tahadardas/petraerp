namespace PetraERP.Application.Security;

/// <summary>
/// Password acceptance rules aligned with NIST SP 800-63B: a minimum length, a generous maximum,
/// no forced composition rules, and rejection of passwords that contain the login name.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;
    public const int MaximumLength = 128;

    public static PasswordPolicyViolation? Validate(string? password, string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        if (string.IsNullOrEmpty(password))
        {
            return PasswordPolicyViolation.Required;
        }

        if (password.Length < MinimumLength)
        {
            return PasswordPolicyViolation.TooShort;
        }

        if (password.Length > MaximumLength)
        {
            return PasswordPolicyViolation.TooLong;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return PasswordPolicyViolation.WhitespaceOnly;
        }

        if (userName.Length > 0 && password.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            return PasswordPolicyViolation.ContainsUserName;
        }

        if (!HasValidSurrogates(password))
        {
            return PasswordPolicyViolation.InvalidCharacters;
        }

        foreach (var character in password)
        {
            if (char.IsControl(character))
            {
                return PasswordPolicyViolation.InvalidCharacters;
            }
        }

        return null;
    }

    /// <summary>Stable API error code for a violation, for example <c>security.password_policy.too_short</c>.</summary>
    public static string ErrorCode(PasswordPolicyViolation violation) => violation switch
    {
        PasswordPolicyViolation.Required => "security.password_policy.required",
        PasswordPolicyViolation.TooShort => "security.password_policy.too_short",
        PasswordPolicyViolation.TooLong => "security.password_policy.too_long",
        PasswordPolicyViolation.WhitespaceOnly => "security.password_policy.whitespace_only",
        PasswordPolicyViolation.ContainsUserName => "security.password_policy.contains_user_name",
        PasswordPolicyViolation.InvalidCharacters => "security.password_policy.invalid_characters",
        PasswordPolicyViolation.ReusesCurrentPassword => "security.password_policy.reuses_current",
        _ => "security.password_policy.invalid",
    };

    private static bool HasValidSurrogates(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
