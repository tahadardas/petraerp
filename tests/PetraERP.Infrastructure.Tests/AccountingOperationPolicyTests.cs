using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountingOperationPolicyTests
{
    [Theory]
    [InlineData(PermissionCatalog.AccountsView)]
    [InlineData(PermissionCatalog.JournalsView)]
    [InlineData(PermissionCatalog.UsersManage)]
    [InlineData(PermissionCatalog.SalesCreditOverride)]
    [InlineData("unknown")]
    public void NonOperationPermissionCannotCreateWriteScope(string permission)
    {
        var error = Assert.Throws<BusinessRuleException>(() => new AccountingWriteScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), permission));
        Assert.Equal("accounting.operation_denied", error.Code);
    }

    [Theory]
    [InlineData(AccountingOperation.ManageSalesInvoices, typeof(SalesInvoiceAdjustmentRow))]
    [InlineData(AccountingOperation.PostSalesInvoice, typeof(SalesInvoiceAdjustmentRow))]
    [InlineData(AccountingOperation.ManagePurchaseInvoices, typeof(Purchases.PurchaseInvoiceAdjustmentRow))]
    [InlineData(AccountingOperation.PostPurchaseInvoice, typeof(Purchases.PurchaseInvoiceAdjustmentRow))]
    public void InvoiceOperationsMayWriteTheirDiscountAndAdditionRows(AccountingOperation operation, Type row) =>
        Assert.True(AccountingMutationPolicy.AllowsType(operation, row));

    [Fact]
    public void DiscountAndAdditionRowsStayWithTheirOwnInvoiceFamily()
    {
        Assert.False(AccountingMutationPolicy.AllowsType(AccountingOperation.ManagePurchaseInvoices, typeof(SalesInvoiceAdjustmentRow)));
        Assert.False(AccountingMutationPolicy.AllowsType(AccountingOperation.ManageSalesInvoices, typeof(Purchases.PurchaseInvoiceAdjustmentRow)));
        Assert.False(AccountingMutationPolicy.AllowsType(AccountingOperation.ManageParties, typeof(SalesInvoiceAdjustmentRow)));
    }

    [Fact]
    public void DraftOperationCannotAuthorizePostingStateOrPostingRequest()
    {
        using var context = Context();
        var row = new JournalEntryRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(),
            Description = "قيد مسودة", State = (byte)JournalEntryState.Draft };
        var entry = context.JournalEntries.Attach(row);
        row.State = (byte)JournalEntryState.Posted;
        context.ChangeTracker.DetectChanges();
        Assert.Throws<BusinessRuleException>(() => AccountingMutationPolicy.Validate(entry, AccountingOperation.ManageJournals, false));
        Assert.False(AccountingMutationPolicy.AllowsType(AccountingOperation.ManageJournals, typeof(PostingRequestRow)));
    }

    [Fact]
    public void SalesPostingAllowsOwnDocumentAndLedgerEffectsButRejectsMasterData()
    {
        using var context = Context();
        var account = context.Accounts.Add(new AccountRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Code = "999", NameArabic = "مرفوض" });
        Assert.Throws<BusinessRuleException>(() => AccountingMutationPolicy.Validate(account, AccountingOperation.PostSalesInvoice, false));
        Assert.True(AccountingMutationPolicy.AllowsType(AccountingOperation.PostSalesInvoice, typeof(SalesInvoiceRow)));
        Assert.True(AccountingMutationPolicy.AllowsType(AccountingOperation.PostSalesInvoice, typeof(JournalEntryRow)));
        Assert.False(AccountingMutationPolicy.AllowsType(AccountingOperation.PostSalesInvoice, typeof(Purchases.PurchaseInvoiceRow)));
    }

    [Fact]
    public void JournalApprovalAndPostingRequireTheirActualPreviousStates()
    {
        using var context = Context();
        var row = new JournalEntryRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(),
            Description = "مسودة غير معتمدة", State = (byte)JournalEntryState.Draft };
        var entry = context.JournalEntries.Attach(row);
        row.State = (byte)JournalEntryState.Posted;
        context.ChangeTracker.DetectChanges();
        Assert.Throws<BusinessRuleException>(() => AccountingMutationPolicy.Validate(entry, AccountingOperation.PostJournal, false));
        row.State = (byte)JournalEntryState.Approved;
        context.ChangeTracker.DetectChanges();
        Assert.Throws<BusinessRuleException>(() => AccountingMutationPolicy.Validate(entry, AccountingOperation.ApproveJournal, false));
    }

    [Fact]
    public void ComposedSalesPostingAllowsDraftAndApprovedDocumentsToPost()
    {
        using var context = Context();
        var draft = new SalesInvoiceRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(),
            Status = (byte)SalesInvoiceStatus.Draft };
        var draftEntry = context.SalesInvoices.Attach(draft);
        draftEntry.State = EntityState.Unchanged;
        draft.Status = (byte)SalesInvoiceStatus.Posted;
        context.ChangeTracker.DetectChanges();
        AccountingMutationPolicy.Validate(draftEntry, AccountingOperation.PostSalesInvoice, false);

        var approved = new SalesInvoiceRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(),
            Status = (byte)SalesInvoiceStatus.Approved };
        var approvedEntry = context.SalesInvoices.Attach(approved);
        approvedEntry.State = EntityState.Unchanged;
        approved.Status = (byte)SalesInvoiceStatus.Posted;
        context.ChangeTracker.DetectChanges();
        AccountingMutationPolicy.Validate(approvedEntry, AccountingOperation.PostSalesInvoice, false);

        var cancelled = new SalesInvoiceRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), BranchId = Guid.NewGuid(),
            Status = (byte)SalesInvoiceStatus.Cancelled };
        var cancelledEntry = context.SalesInvoices.Attach(cancelled);
        cancelledEntry.State = EntityState.Unchanged;
        cancelled.Status = (byte)SalesInvoiceStatus.Posted;
        context.ChangeTracker.DetectChanges();
        var error = Assert.Throws<BusinessRuleException>(() =>
            AccountingMutationPolicy.Validate(cancelledEntry, AccountingOperation.PostSalesInvoice, false));
        Assert.Equal("accounting.operation_denied", error.Code);
    }

    [Fact]
    public void EveryOperationHasOneCatalogPermissionAndReadCodesHaveNoOperation()
    {
        foreach (var operation in Enum.GetValues<AccountingOperation>())
        {
            var permission = AccountingOperationPolicy.Permission(operation);
            Assert.Equal(permission, PermissionCatalog.Get(permission).Code);
            Assert.Equal(operation, AccountingOperationPolicy.FromPermission(permission));
        }
        Assert.Throws<BusinessRuleException>(() => AccountingOperationPolicy.Permission((AccountingOperation)int.MaxValue));
    }

    private static AccountingDbContext Context() => new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer().Options);
}
