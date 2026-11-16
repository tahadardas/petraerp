using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Inventory;

public sealed partial class SqlInventoryDocumentService
{
    /// <summary>
    /// The inventory endpoints carry no per-operation permission policy (one route serves transfers, adjustments and counts),
    /// so no write operation is bound before the service runs and every call - even a read - used to fail with
    /// "write_scope_required". The service now binds the operation that matches the document kind: transfers are authorized as
    /// ManageTransfers, every other kind as ManageAdjustments; reads use ManageTransfers and never save. The binding is built
    /// from the already-authenticated company, branch and user, the central authority still verifies the user's permission for
    /// that exact operation on every save, and a context that is already bound (a module or a batch) is left untouched.
    /// </summary>
    private async Task BindScopeAsync(InventoryDocumentScope scope, InventoryDocumentKind? kind, Guid? documentId, CancellationToken token)
    {
        if (db is not AccountingDbContext annual || annual.BoundOperation is not null) return;
        if (kind is null && documentId is { } id)
        {
            var stored = await db.InventoryDocuments.AsNoTracking().Where(d => d.Id == id && d.CompanyId == scope.CompanyId)
                .Select(d => (int?)d.Kind).SingleOrDefaultAsync(token).ConfigureAwait(false);
            if (stored is { } value) kind = (InventoryDocumentKind)value;
        }

        var operation = kind is null or InventoryDocumentKind.Transfer ? AccountingOperation.ManageTransfers : AccountingOperation.ManageAdjustments;
        var fiscalYear = await db.FiscalYears.AsNoTracking().Select(y => y.Id).SingleAsync(token).ConfigureAwait(false);
        annual.BindWriteScope(new AccountingWriteScope(scope.CompanyId, fiscalYear, scope.UserId, operation, scope.BranchId));
    }
}
