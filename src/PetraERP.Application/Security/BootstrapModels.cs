namespace PetraERP.Application.Security;

/// <summary>
/// First-administrator installation input. Executed only from the server-side CLI; there is no
/// HTTP bootstrap endpoint. Optional company/branch values create the first organization when none exists.
/// </summary>
public sealed class BootstrapAdministratorCommand
{
    public BootstrapAdministratorCommand(string userName, string displayNameArabic, string? displayNameEnglish, string password,
        BootstrapOrganization? organization)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(displayNameArabic);
        ArgumentNullException.ThrowIfNull(password);
        UserName = userName;
        DisplayNameArabic = displayNameArabic;
        DisplayNameEnglish = displayNameEnglish;
        Password = password;
        Organization = organization;
    }

    public string UserName { get; }
    public string DisplayNameArabic { get; }
    public string? DisplayNameEnglish { get; }
    public string Password { get; }
    public BootstrapOrganization? Organization { get; }

    public override string ToString() => $"BootstrapAdministratorCommand({UserName})";
}

public sealed record BootstrapOrganization(
    string CompanyCode,
    string CompanyNameArabic,
    string? CompanyNameEnglish,
    string BaseCurrencyCode,
    string BaseCurrencyNameArabic,
    string BranchCode,
    string BranchNameArabic,
    string? BranchNameEnglish);

public sealed record BootstrapResult(Guid AdministratorUserId, Guid AdministratorRoleId, int CompanyScopeCount, int BranchScopeCount);

public interface ISecurityBootstrapper
{
    /// <summary>True when the one-time bootstrap has already completed or any user exists.</summary>
    Task<bool> IsCompletedAsync(CancellationToken cancellationToken);

    Task<BootstrapResult> BootstrapAsync(BootstrapAdministratorCommand command, CancellationToken cancellationToken);
}
