using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Organizations;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>Company lock is deliberately coarse and shared with commercial posting. Never lock stock before company.</summary>
internal static class InventoryWriteScope
{
    internal static async Task LockAsync(DbContext db, Guid company, Guid branch, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Inventory writes require an explicit SERIALIZABLE transaction.");
        await LockCompanyAsync(db, company, token).ConfigureAwait(false);
        if (!await db.Set<BranchRow>().FromSqlInterpolated($"SELECT * FROM [system].[Branches] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={branch} AND [CompanyId]={company}")
                .AsNoTracking().AnyAsync(b => b.IsActive, token).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.scope_invalid", "الفرع غير فعال أو خارج الشركة / Branch is inactive or outside company scope.");
    }

    internal static async Task LockCompanyAsync(DbContext db, Guid company, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Inventory writes require an explicit SERIALIZABLE transaction.");
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r=sp_getapplock @Resource=N'PetraERP.Security.Administration',@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @r<0 THROW 51010,N'Cannot acquire security lock.',1;
            """, token).ConfigureAwait(false);
        if (!await db.Set<CompanyRow>().FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={company}")
                .AsNoTracking().AnyAsync(c => c.IsActive, token).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.scope_invalid", "الشركة أو الفرع غير فعال أو خارج النطاق / Company or branch is inactive or outside scope.");
    }

    internal static async Task ValidateWarehouseAsync(DbContext db, Guid company, Guid branch, Guid warehouse, CancellationToken token)
    {
        if (!await db.Set<WarehouseRow>().AsNoTracking().AnyAsync(w => w.Id == warehouse && w.CompanyId == company && w.BranchId == branch && w.IsActive, token).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.warehouse_scope", "المستودع غير فعال أو لا يتبع الشركة والفرع / Warehouse must be active in this company and branch.", "warehouseId");
    }
}
