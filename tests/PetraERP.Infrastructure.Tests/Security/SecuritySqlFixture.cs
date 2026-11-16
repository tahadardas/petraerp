using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests.Security;

/// <summary>Deterministic token issuer for storage-level tests; the API host uses the real JWT issuer.</summary>
internal sealed class TestTokenIssuer : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(Guid userId, Guid sessionId, DateTimeOffset issuedAtUtc) =>
        new("test-access-" + sessionId.ToString("N"), issuedAtUtc.AddMinutes(10));
}

internal static class SecuritySqlFixture
{
    internal const string AdminUser = "admin";
    internal const string AdminPassword = "Petra-Secure-2026-طويلة";

    internal static readonly Pbkdf2PasswordHasher Hasher = new(AuthenticationSettings.Default);

    internal static async Task<BootstrapResult> BootstrapAsync(PetraDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(true);
        var bootstrapper = new SqlSecurityBootstrapper(context, Hasher, TimeProvider.System);
        return await bootstrapper.BootstrapAsync(new BootstrapAdministratorCommand(AdminUser, "مدير النظام", "Administrator",
            AdminPassword, new BootstrapOrganization("PETRA", "شركة البتراء", "Petra Co.", "JOD", "دينار أردني", "HQ",
                "المقر الرئيسي", "Head office")), cancellationToken).ConfigureAwait(true);
    }

    internal static SqlAuthenticationService Authentication(PetraDbContext context, AuthenticationSettings? settings = null) =>
        new(context, Hasher, new TestTokenIssuer(), settings ?? AuthenticationSettings.Default, TimeProvider.System);

    internal static SqlSecurityAdministrationService Administration(PetraDbContext context) =>
        new(context, Hasher, TimeProvider.System);

    internal static LoginCommand Login(string userName, string password) =>
        new(userName, password, ClientKind.Desktop, "TEST-PC");

    internal static ClientContext Client { get; } = ClientContext.Create("127.0.0.1", "PetraERP.Tests", "sql-security-test");
}
