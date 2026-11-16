using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Tests.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed class PhaseTwoAdministratorUpgradeSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ExistingAdministratorReceivesMissingPhaseTwoPermissionsWithoutGrantingOrdinaryRoles()
    {
        var token = TestContext.Current.CancellationToken;
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, token).ConfigureAwait(true);
            await context.GetService<IMigrator>().MigrateAsync("20261025210100_PurchaseApprovalStates", token).ConfigureAwait(true);
            var admin = await context.Roles.SingleAsync(r => r.Code == "ADMINISTRATOR", token).ConfigureAwait(true);
            var ordinary = new RoleRow { Id = Guid.NewGuid(), Code = "UPGRADE-CLERK", NameArabic = "موظف اختبار الترقية" };
            context.Roles.Add(ordinary);
            context.RolePermissions.Add(new RolePermissionRow { RoleId = ordinary.Id, PermissionCode = PermissionCatalog.AccountsView });
            await context.SaveChangesAsync(token).ConfigureAwait(true);
            await context.Database.ExecuteSqlRawAsync("""
                DELETE rp FROM [security].[RolePermissions] rp
                JOIN [security].[Roles] r ON r.[Id]=rp.[RoleId]
                WHERE r.[Code]=N'ADMINISTRATOR' AND rp.[PermissionCode] IN
                    (N'Inventory.Negative.Approve',N'Inventory.Negative.RequestApproval',
                     N'Patterns.Documents.ManualNumbering',N'System.CompanyOptions.Manage');
                """, token).ConfigureAwait(true);
            var before = await context.RolePermissions.CountAsync(p => p.RoleId == admin.Id, token).ConfigureAwait(true);
            await context.Database.MigrateAsync(token).ConfigureAwait(true);
            await context.Database.MigrateAsync(token).ConfigureAwait(true);
            // +10 from the phase-two upgrades, +1 for Treasury.BankReconciliations.Manage added by the bank reconciliation migration.
            Assert.Equal(before + 11, await context.RolePermissions.CountAsync(p => p.RoleId == admin.Id, token).ConfigureAwait(true));
            Assert.Single(await context.RolePermissions.Where(p => p.RoleId == ordinary.Id).ToListAsync(token).ConfigureAwait(true));
            Assert.Equal(PermissionCatalog.AccountsView, (await context.RolePermissions.SingleAsync(p => p.RoleId == ordinary.Id, token).ConfigureAwait(true)).PermissionCode);
            foreach (var permission in new[] { PermissionCatalog.CompanyOptionsManage, PermissionCatalog.PatternsManualNumbering,
                PermissionCatalog.InventoryNegativeRequest, PermissionCatalog.InventoryNegativeApprove,
                PermissionCatalog.PostingBatchesManage, PermissionCatalog.BankReconciliationsManage })
                Assert.True(await context.RolePermissions.AnyAsync(p => p.RoleId == admin.Id && p.PermissionCode == permission, token).ConfigureAwait(true));
        }
    }
}
