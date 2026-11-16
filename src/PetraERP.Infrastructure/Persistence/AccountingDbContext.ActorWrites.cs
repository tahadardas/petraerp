using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Persistence;

public sealed partial class AccountingDbContext
{
    private AccountingWriteScope? writeScope;
    private bool provisioningWrite;
    private bool writeFailed;

    /// <summary>The operation bound for this unit of work, or null before binding. Lets the posting service require the
    /// correct permission for a composed operation (e.g. FX revaluation) that posts a plain journal with no document.</summary>
    internal AccountingOperation? BoundOperation => writeScope?.Operation;

    /// <summary>The fiscal year this unit of work is bound to, or null before binding.</summary>
    internal Guid? BoundFiscalYearId => writeScope?.FiscalYearId;

    public void BindWriteScope(AccountingWriteScope scope) => Bind(scope, provisioning: false);
    internal void BindProvisioningScope(AccountingWriteScope scope) => Bind(scope, provisioning: true);

    private void Bind(AccountingWriteScope scope, bool provisioning)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ThrowIfWriteFailed();
        if (provisioning != (scope.Operation == AccountingOperation.ProvisionWorkspace)) throw AccountingOperationPolicy.Denied();
        if (writeScope is not null && (scope != writeScope || provisioning != provisioningWrite))
            throw new InvalidOperationException("An accounting context cannot change its write identity or workspace.");
        writeScope = scope;
        provisioningWrite = provisioning;
    }

    private async Task VerifyWriteActorAsync(CancellationToken cancellationToken)
    {
        ThrowIfWriteFailed();
        var scope = writeScope ?? throw new BusinessRuleException("accounting.write_scope_required", "A server-authorized company/year/actor write scope is required.");
        var authority = actorAuthority ?? throw new BusinessRuleException("accounting.authority_required", "The central actor authority is required for accounting writes.");
        if (Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Accounting writes require a SERIALIZABLE local transaction.");
        if (ChangeTracker.Entries<ActorReferenceRow>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new BusinessRuleException("accounting.actor_immutable", "Actor references are created only from verified central identities and cannot be edited or deleted.");

        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            AccountingMutationPolicy.Validate(entry, scope.Operation, provisioningWrite);
            if (entry.Metadata.FindProperty("CompanyId") is not null && entry.Property("CompanyId").CurrentValue is Guid company && company != scope.CompanyId)
                throw ScopeMismatch();
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty("CompanyId") is not null
                && entry.Property("CompanyId").IsModified) throw ScopeMismatch();
            if (entry.Entity is Organizations.CompanyRow companyRow && companyRow.Id != scope.CompanyId) throw ScopeMismatch();
            if (entry.Entity is AccountingCalendars.FiscalYearRow year && year.Id != scope.FiscalYearId) throw ScopeMismatch();
            foreach (var name in FiscalIdProperties)
                if (entry.Metadata.FindProperty(name) is not null)
                {
                    var property = entry.Property(name);
                    if (property.CurrentValue is Guid fiscal && fiscal != scope.FiscalYearId
                        || entry.State == EntityState.Modified && property.IsModified && property.OriginalValue is Guid
                            && !Equals(property.OriginalValue, property.CurrentValue)) throw ScopeMismatch();
                }
            if (!provisioningWrite && entry.Metadata.FindProperty("BranchId") is not null
                && entry.Property("BranchId").CurrentValue is Guid branch && branch != scope.BranchId) throw ScopeMismatch();
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty("BranchId") is not null
                && entry.Property("BranchId").IsModified) throw ScopeMismatch();
            foreach (var key in entry.Metadata.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType == typeof(ActorReferenceRow)))
            {
                var property = entry.Property(key.Properties[^1].Name);
                if (entry.State == EntityState.Modified && property.IsModified && property.OriginalValue is Guid oldActor
                    && !Equals(property.OriginalValue, property.CurrentValue))
                    throw new BusinessRuleException("accounting.actor_immutable", $"Historical actor '{oldActor}' cannot be replaced.");
                if (entry.State == EntityState.Added || (entry.State == EntityState.Modified && property.IsModified))
                    if (property.CurrentValue is Guid actor && actor != scope.ActorUserId) throw ScopeMismatch();
            }
        }

        var verified = await authority.VerifyAsync(scope, provisioningWrite, cancellationToken).ConfigureAwait(false);
        if (verified.Id != scope.ActorUserId) throw ScopeMismatch();
        await VerifyJournalLineParentsAsync(scope, cancellationToken).ConfigureAwait(false);
        await VerifyDocumentLineParentsAsync(scope, cancellationToken).ConfigureAwait(false);
        // The write scope is immutable for the life of this context and the annual database holds exactly one fiscal year, so the
        // fiscal-year scope check and the actor-reference lookup cannot change between the SaveChanges calls of one unit of work.
        // A document request saves 7-8 times; repeating both checks each time was about 20 of its 159 SQL commands.
        if (!provisioningWrite && !fiscalScopeVerified)
        {
            // A request runs many short units of work, each on its own context, and every one repeated this two-query check (8 of the
            // ~143 commands of an invoice save+post). The answer is a fact about the database identity, so a verified (database,
            // company, year) is trusted for a few seconds across contexts; a mismatch is never cached.
            var scopeKey = string.Concat(Database.GetDbConnection().DataSource, "/", Database.GetDbConnection().Database, "|", scope.CompanyId, "|", scope.FiscalYearId);
            if (!VerifiedFiscalScopes.TryGetValue(scopeKey, out var until) || until <= Environment.TickCount64)
            {
                if (!await FiscalYears.AnyAsync(y => y.Id == scope.FiscalYearId && y.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
                    || await FiscalYears.AnyAsync(y => y.Id != scope.FiscalYearId || y.CompanyId != scope.CompanyId, cancellationToken).ConfigureAwait(false))
                    throw ScopeMismatch();
                if (VerifiedFiscalScopes.Count > 256) VerifiedFiscalScopes.Clear();
                VerifiedFiscalScopes[scopeKey] = Environment.TickCount64 + 5000;
            }

            fiscalScopeVerified = true;
        }

        if (actorReferenceKnown) return;
        // Actor references are immutable once created, so the common case (the row exists) needs only a shared read. The old
        // unconditional UPDLOCK,HOLDLOCK made every SaveChanges of the same actor queue on one hot row and, because it ran after
        // the invoice range reads, closed a lock cycle with the invoice UPDATEs (deadlock graphs: .artifacts/gate-b). The update
        // lock is taken only to create the row, re-checking under that lock so two first requests cannot both insert it.
        var existing = await ActorReferences.AsNoTracking().Where(r => r.Id == verified.Id).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        existing ??= await ActorReferences.FromSqlInterpolated(
            $"SELECT * FROM [audit].[ActorReferences] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={verified.Id}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
            ActorReferences.Add(new ActorReferenceRow
            {
                Id = verified.Id, CompanyId = scope.CompanyId, DisplayNameArabic = verified.DisplayNameArabic,
                DisplayNameEnglish = verified.DisplayNameEnglish, FirstVerifiedAtUtc = DateTimeOffset.UtcNow,
            });
        else if (existing.CompanyId != scope.CompanyId) throw ScopeMismatch();
        // Known only when the row existed; a row added above is confirmed on the save that inserts it.
        actorReferenceKnown = existing is not null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> VerifiedFiscalScopes = new(StringComparer.Ordinal);
    private bool fiscalScopeVerified;
    private bool actorReferenceKnown;

    /// <summary>
    /// Prepares a centrally verified local actor before SQL-based posting-request registration.
    /// Uses the caller's local transaction; it never commits that transaction or saves unrelated pending rows.
    /// </summary>
    internal async Task PrepareActorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await VerifyWriteActorAsync(cancellationToken).ConfigureAwait(false);
            var pending = ChangeTracker.Entries<ActorReferenceRow>().SingleOrDefault(e => e.State == EntityState.Added);
            if (pending is null) return;
            var actor = pending.Entity;
            pending.State = EntityState.Detached;
            await Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [audit].[ActorReferences] ([Id],[CompanyId],[DisplayNameArabic],[DisplayNameEnglish],[FirstVerifiedAtUtc]) VALUES ({actor.Id},{actor.CompanyId},{actor.DisplayNameArabic},{actor.DisplayNameEnglish},{actor.FirstVerifiedAtUtc})", cancellationToken).ConfigureAwait(false);
            ActorReferences.Attach(actor);
        }
        catch
        {
            MarkWriteFailed();
            throw;
        }
    }

    internal void MarkWriteFailed() => writeFailed = true;

    /// <summary>Checks composed service permission compatibility; secondary credit/allocation grants still require central authorization.</summary>
    internal void ValidateRequiredPermission(string permission)
    {
        ThrowIfWriteFailed();
        var scope = writeScope ?? throw new BusinessRuleException("accounting.write_scope_required", "An authorized operation is required.");
        if (permission == scope.PermissionCode) return;
        if (Application.Security.PermissionCatalog.TryGet(permission, out _)
            && !AccountingOperationPolicy.TryFromPermission(permission, out _))
        {
            // Read checks and explicit credit override are additional central checks, never write authority.
            if (permission.EndsWith(".View", StringComparison.Ordinal)
                || permission == Application.Security.PermissionCatalog.SalesCreditOverride) return;
        }
        // Negative-issue approval is a secondary central grant checked alongside a composed posting. The posting
        // service still verifies that the actor holds it (and its company/branch scope) before any stock is written.
        if (AccountingMutationPolicy.IsComposedPosting(scope.Operation)
            && permission == Application.Security.PermissionCatalog.InventoryNegativeApprove) return;
        if (AccountingMutationPolicy.IsComposedPosting(scope.Operation)
            && permission is Application.Security.PermissionCatalog.JournalsManage
                or Application.Security.PermissionCatalog.JournalsApprove or Application.Security.PermissionCatalog.JournalsPost) return;
        if ((scope.Operation is AccountingOperation.ReverseSalesInvoice or AccountingOperation.ReturnPurchaseInvoice
                or AccountingOperation.ReverseTreasuryVoucher or AccountingOperation.ManageAdjustments)
            && permission == Application.Security.PermissionCatalog.JournalsReverse) return;
        if ((scope.Operation is AccountingOperation.ManageTreasuryVouchers or AccountingOperation.PostTreasuryVoucher or AccountingOperation.AllocateTreasuryVoucher)
            && permission is Application.Security.PermissionCatalog.TreasuryVouchersAllocate or Application.Security.PermissionCatalog.TreasuryVouchersManage) return;
        // A composed correction posts its own new document/journal, so it also exercises the sibling Post grant
        // that the endpoint required alongside the reverse/return grant (the actor holds both).
        if (scope.Operation == AccountingOperation.ReverseSalesInvoice && permission == Application.Security.PermissionCatalog.SalesInvoicesPost) return;
        if (scope.Operation == AccountingOperation.ReturnPurchaseInvoice && permission == Application.Security.PermissionCatalog.PurchaseInvoicesPost) return;
        if (scope.Operation == AccountingOperation.ReverseTreasuryVoucher && permission == Application.Security.PermissionCatalog.TreasuryVouchersPost) return;
        // Bulk posting composes each document's own approve/post service under one batch authority. The posting
        // engine still verifies the specific grant (and scope) centrally for every document it posts.
        if (scope.Operation == AccountingOperation.ManagePostingBatches
            && permission is Application.Security.PermissionCatalog.SalesInvoicesApprove
                or Application.Security.PermissionCatalog.SalesInvoicesPost
                or Application.Security.PermissionCatalog.PurchaseInvoicesApprove
                or Application.Security.PermissionCatalog.PurchaseInvoicesPost
                or Application.Security.PermissionCatalog.TreasuryVouchersApprove
                or Application.Security.PermissionCatalog.TreasuryVouchersPost
                or Application.Security.PermissionCatalog.JournalsApprove
                or Application.Security.PermissionCatalog.JournalsPost
                or Application.Security.PermissionCatalog.InventoryTransfersManage
                or Application.Security.PermissionCatalog.InventoryAdjustmentsManage) return;
        throw AccountingOperationPolicy.Denied();
    }

    internal void ThrowIfWriteFailed()
    {
        if (writeFailed) throw new InvalidOperationException("A failed accounting unit of work cannot be reused. Roll back and retry with a new, authorized context.");
    }

    private async Task VerifyJournalLineParentsAsync(AccountingWriteScope scope, CancellationToken cancellationToken)
    {
        foreach (var line in ChangeTracker.Entries<GeneralLedger.JournalLineRow>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            var tracked = ChangeTracker.Entries<GeneralLedger.JournalEntryRow>().SingleOrDefault(e => e.Entity.Id == line.Entity.JournalId);
            var parent = tracked?.Entity ?? await JournalEntries.AsNoTracking().SingleOrDefaultAsync(j => j.Id == line.Entity.JournalId, cancellationToken).ConfigureAwait(false)
                ?? throw ScopeMismatch();
            if (parent.CompanyId != scope.CompanyId || parent.BranchId != scope.BranchId) throw ScopeMismatch();
            var original = tracked is null ? parent.State : (byte)tracked.Property(nameof(GeneralLedger.JournalEntryRow.State)).OriginalValue!;
            if (tracked?.State != EntityState.Added && original >= (byte)Domain.GeneralLedger.JournalEntryState.Posted)
                throw AccountingOperationPolicy.Denied();
            if (scope.Operation is AccountingOperation.ApproveJournal or AccountingOperation.PostJournal)
                throw AccountingOperationPolicy.Denied();
        }
    }

    private async Task VerifyDocumentLineParentsAsync(AccountingWriteScope scope, CancellationToken cancellationToken)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            switch (entry.Entity)
            {
                case Sales.SalesInvoiceLineRow line:
                    await VerifyParentAsync<Sales.SalesInvoiceRow>(line.SalesInvoiceId, scope, "Status", 0, cancellationToken).ConfigureAwait(false);
                    break;
                case Sales.SalesInvoiceAdjustmentRow adjustment:
                    await VerifyParentAsync<Sales.SalesInvoiceRow>(adjustment.SalesInvoiceId, scope, "Status", 0, cancellationToken).ConfigureAwait(false);
                    break;
                case Purchases.PurchaseInvoiceAdjustmentRow adjustment:
                    await VerifyParentAsync<Purchases.PurchaseInvoiceRow>(adjustment.PurchaseInvoiceId, scope, "Status", 0, cancellationToken).ConfigureAwait(false);
                    break;
                case Purchases.PurchaseInvoiceLineRow line:
                    await VerifyParentAsync<Purchases.PurchaseInvoiceRow>(line.PurchaseInvoiceId, scope, "Status", 0, cancellationToken).ConfigureAwait(false);
                    break;
                case Treasury.VoucherLineRow line:
                    await VerifyParentAsync<Treasury.VoucherRow>(line.VoucherId, scope, "Status", 0, cancellationToken).ConfigureAwait(false);
                    break;
                case Inventory.InventoryDocumentLineRow line:
                    await VerifyParentAsync<Inventory.InventoryDocumentRow>(line.DocumentId, scope, "State", 0, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task VerifyParentAsync<TRow>(Guid parentId, AccountingWriteScope scope, string stateName, int draftState,
        CancellationToken cancellationToken) where TRow : class
    {
        var tracked = ChangeTracker.Entries<TRow>().SingleOrDefault(e => e.Property("Id").CurrentValue is Guid id && id == parentId);
        var parent = tracked?.Entity ?? await Set<TRow>().AsNoTracking().SingleOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == parentId, cancellationToken).ConfigureAwait(false)
            ?? throw ScopeMismatch();
        var metadata = Entry(parent);
        if (!Equals(metadata.Property("CompanyId").CurrentValue, scope.CompanyId)
            || !Equals(metadata.Property("BranchId").CurrentValue, scope.BranchId)) throw ScopeMismatch();
        // Posting may update cost snapshots on approved document lines, but historical posted lines are immutable.
        var state = Convert.ToInt32(tracked is null ? metadata.Property(stateName).CurrentValue : tracked.Property(stateName).OriginalValue,
            System.Globalization.CultureInfo.InvariantCulture);
        if (tracked?.State != EntityState.Added && (state != draftState && (!AccountingMutationPolicy.IsComposedPosting(scope.Operation) || state >= 2)))
            throw AccountingOperationPolicy.Denied();
    }

    private static readonly string[] FiscalIdProperties = ["FiscalYearId", "PostingFiscalYearId"];
    private static BusinessRuleException ScopeMismatch() => new("accounting.write_scope_mismatch", "The financial write does not match its authorized actor/company/year/branch scope.");
}
