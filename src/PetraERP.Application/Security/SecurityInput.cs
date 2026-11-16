using PetraERP.Application.Common;
using PetraERP.Domain.Primitives;

namespace PetraERP.Application.Security;

/// <summary>Shared validation of administrator and user input, mapped to stable request error codes.</summary>
public static class SecurityInput
{
    public const int MaximumReasonLength = 500;
    public const int MaximumSearchLength = 100;

    public static (string UserName, string NormalizedUserName) RequireLoginName(string? value, string field)
    {
        if (!LoginName.TryNormalize(value, out var userName, out var normalized))
        {
            throw new RequestValidationException("security.user_name_invalid",
                "Login names must be 3-64 ASCII letters, digits, '.', '_' or '-'.", field);
        }

        return (userName, normalized);
    }

    public static BilingualName RequireNames(string? arabic, string? english, string arabicField)
    {
        try
        {
            return BilingualName.Create(arabic!, english);
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException("request.name_invalid",
                "Names are required, at most 200 characters, and cannot contain control characters.", arabicField);
        }
    }

    public static void RequirePassword(string? password, string userName, string field)
    {
        if (PasswordPolicy.Validate(password, userName) is { } violation)
        {
            throw new RequestValidationException(PasswordPolicy.ErrorCode(violation), "The password does not meet the password policy.", field);
        }
    }

    public static string RequireReason(string? reason, string field)
    {
        try
        {
            var value = AuditReason.Create(reason!).Value;
            if (value.Length > MaximumReasonLength)
            {
                throw new ArgumentException("Reason is too long.", nameof(reason));
            }

            return value;
        }
        catch (ArgumentException)
        {
            throw new RequestValidationException("request.reason_invalid",
                "A reason of 1-500 characters without control characters is required.", field);
        }
    }

    public static string RequireRoleCode(string? value, string field)
    {
        var code = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 64 || !code.All(static character =>
                char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-'))
        {
            throw new RequestValidationException("security.role_code_invalid",
                "Role codes must be 2-64 ASCII letters, digits, '.', '_' or '-'.", field);
        }

        return code;
    }

    public static IReadOnlyList<string> RequirePermissionCodes(IEnumerable<string>? codes, string field)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in codes ?? [])
        {
            if (code is null || !PermissionCatalog.TryGet(code, out _))
            {
                throw new RequestValidationException("security.unknown_permission", "An unknown permission code was supplied.", field);
            }

            if (!result.Add(code))
            {
                throw new RequestValidationException("security.duplicate_permission", "A permission code was supplied more than once.", field);
            }
        }

        return result.ToArray();
    }

    public static string? OptionalSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var trimmed = search.Trim();
        if (trimmed.Length > MaximumSearchLength || trimmed.Any(char.IsControl))
        {
            throw new RequestValidationException("request.search_invalid", "Search text must be at most 100 characters.", "search");
        }

        return trimmed;
    }
}
