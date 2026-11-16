#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Parties;
using PetraERP.Application.Parties.OpenItems;
using PetraERP.Application.Patterns;
using PetraERP.Application.Treasury;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Tests;

public sealed class OpenItemSettlementSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OpenItem_CrossYearSettlement_UpdatesBalancesAndAllowsReversal()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var input = new ProvisionCompanyInput("OPENSETTLE", "شركة التسويات", "Settlement Co", "USD", 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            var provisioned = await fixture.ProvisionAsync(input).ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            var timeProvider = TimeProvider.System;

            Guid companyId;
            Guid branchId;
            var arId = Guid.NewGuid();
            var bankId = Guid.NewGuid();
            var partyId = Guid.NewGuid();

            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await readContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await readContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                branchId = branch.Id;
            }

            // 1. Setup accounts
            using (var accContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                accContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));
                var ar = new AccountRow
                {
                    Id = arId,
                    CompanyId = companyId,
                    Code = "1131",
                    NameArabic = "العملاء",
                    NameEnglish = "Accounts Receivable",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 1
                };
                var bank = new AccountRow
                {
                    Id = bankId,
                    CompanyId = companyId,
                    Code = "1021",
                    NameArabic = "البنك",
                    NameEnglish = "Bank",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 1
                };
                accContext.Accounts.AddRange(ar, bank);
                await accContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // 2. Setup Party (Customer)
            using (var partyContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                partyContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageParties, branchId));
                var partyService = new SqlPartyService(partyContext);
                var partyResult = await partyService.CreatePartyAsync(companyId, new CreatePartyCommand(
                    Code: "CUST-001",
                    NameArabic: "عميل سابق",
                    NameEnglish: "Prior Customer",
                    Role: PartyRole.Customer,
                    ReceivableAccountId: arId,
                    DefaultCurrencyCode: "USD"), Cancellation).ConfigureAwait(true);
                partyId = partyResult.Id;
            }

            // 3. Setup Document Pattern for Receipt Voucher
            Guid revisionId;
            using (var patternContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                patternContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManagePatterns, branchId));
                var patterns = new SqlPatternService(patternContext);
                var scope = PatternScope.Create(companyId, branchId, fixture.Actor);
                var pattern = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand
                {
                    Code = "RV-OPEN",
                    NameArabic = "سند قبض",
                    ShortCode = "RV",
                    OperationKind = BusinessOperationKind.CashReceipt,
                    BranchIds = [branchId],
                    InitialConfiguration = new PatternConfiguration
                    {
                        AccountMappings = [
                            new() { Role = PatternAccountRoles.Bank, AccountId = bankId },
                            new() { Role = PatternAccountRoles.Receivable, AccountId = arId }
                        ],
                        NumberingPolicy = new() { Prefix = "RV-", MinimumDigits = 4 }
                    }
                }, Cancellation).ConfigureAwait(true);

                var revId = pattern.ActiveRevision?.Id ?? pattern.Revisions[0].Id;
                var published = await patterns.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand
                {
                    ExpectedVersion = pattern.Version,
                    RevisionId = revId,
                    Reason = "Initial publication"
                }, Cancellation).ConfigureAwait(true);
                revisionId = published.ActiveRevision?.Id ?? revId;
            }

            // 4. Create Open Item (Carried over from prior year: $500.00 outstanding)
            OpenItemDto openItem;
            using (var openItemContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                openItemContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageParties, branchId));
                var openItemService = new SqlOpenItemService(openItemContext, timeProvider);
                openItem = await openItemService.CreateOpenItemAsync(companyId, new CreateOpenItemCommand(
                    BranchId: branchId,
                    PartyId: partyId,
                    PartyType: 1, // Customer
                    OriginFiscalYearId: null,
                    OriginDocumentId: null,
                    OriginDocumentNumber: "INV-2025-999",
                    OriginDocumentDateUtc: DateTimeOffset.UtcNow.AddMonths(-3),
                    DueDateUtc: DateTimeOffset.UtcNow.AddDays(30),
                    CurrencyCode: "USD",
                    OriginalAmount: 500m,
                    ExchangeRate: 1m,
                    OriginType: 2, // PriorYearInvoiceRollover
                    SourceReference: "ROLLOVER-2025",
                    RolloverBatchId: null,
                    AccountId: arId), Cancellation).ConfigureAwait(true);
            }

            Assert.Equal(500m, openItem.OutstandingAmount);
            Assert.Equal(0m, openItem.SettledAmount);

            // 5. Create Receipt Voucher settling $300 of the open item
            VoucherDetailResult draftVoucher;
            using (var voucherContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                voucherContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageTreasuryVouchers, branchId));
                var patterns = new SqlPatternService(voucherContext);
                var voucherService = new SqlTreasuryVoucherService(voucherContext, patterns, timeProvider);
                var saveCommand = new SaveVoucherCommand(
                    Kind: VoucherKind.Receipt,
                    PatternRevisionId: revisionId,
                    PartyId: partyId,
                    SettlementMethod: SettlementMethod.Bank,
                    VoucherDateUtc: DateTimeOffset.UtcNow,
                    CurrencyCode: "USD",
                    ExchangeRate: 1m,
                    Amount: 300m,
                    Notes: "Receipt voucher for open item",
                    Allocations: [],
                    Lines: [])
                {
                    InvoiceAllocations = [
                        new InvoiceSettlementCommand(openItem.Id, 300m, 300m, 1m)
                    ]
                };
                draftVoucher = await voucherService.CreateDraftAsync(companyId, branchId, fixture.Actor, saveCommand, Cancellation).ConfigureAwait(true);
            }

            // 6. Approve Voucher
            VoucherDetailResult approvedVoucher;
            using (var approveContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                approveContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ApproveTreasuryVoucher, branchId));
                var patterns = new SqlPatternService(approveContext);
                var voucherService = new SqlTreasuryVoucherService(approveContext, patterns, timeProvider);
                approvedVoucher = await voucherService.ApproveAsync(companyId, branchId, draftVoucher.Id, fixture.Actor, new VoucherActionCommand(ConcurrencyToken.FromBytes(draftVoucher.RowVersion), "Approving settlement voucher"), Cancellation).ConfigureAwait(true);
            }

            // 7. Post Voucher
            VoucherDetailResult postedVoucher;
            using (var postContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                postContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.PostTreasuryVoucher, branchId));
                var patterns = new SqlPatternService(postContext);
                var voucherService = new SqlTreasuryVoucherService(postContext, patterns, timeProvider);
                postedVoucher = await voucherService.PostAsync(companyId, branchId, draftVoucher.Id, fixture.Actor, new PostVoucherCommand(ConcurrencyToken.FromBytes(approvedVoucher.RowVersion), "VOUCHER-OPEN-SETTLE-01"), Cancellation).ConfigureAwait(true);
            }

            // 8. Assert OpenItem is updated: Outstanding = 200, Settled = 300
            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var openItemService = new SqlOpenItemService(readContext, timeProvider);
                var updatedOpenItem = await openItemService.GetOpenItemAsync(companyId, openItem.Id, Cancellation).ConfigureAwait(true);
                Assert.Equal(200m, updatedOpenItem.OutstandingAmount);
                Assert.Equal(300m, updatedOpenItem.SettledAmount);
                Assert.Equal(200m, updatedOpenItem.CurrentBaseCarryingAmount);
                Assert.Equal((byte)OpenItemState.PartiallySettled, updatedOpenItem.State); // Partially settled
            }

            // 9. Reverse Voucher -> OpenItem should be restored
            using (var reverseContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                reverseContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ReverseTreasuryVoucher, branchId));
                var patterns = new SqlPatternService(reverseContext);
                var voucherService = new SqlTreasuryVoucherService(reverseContext, patterns, timeProvider);
                await voucherService.ReverseAsync(companyId, branchId, draftVoucher.Id, fixture.Actor, new ReverseVoucherCommand(ConcurrencyToken.FromBytes(postedVoucher.RowVersion), "Reversing settlement voucher"), Cancellation).ConfigureAwait(true);
            }

            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var openItemService = new SqlOpenItemService(readContext, timeProvider);
                var restoredOpenItem = await openItemService.GetOpenItemAsync(companyId, openItem.Id, Cancellation).ConfigureAwait(true);
                Assert.Equal(500m, restoredOpenItem.OutstandingAmount);
                Assert.Equal(0m, restoredOpenItem.SettledAmount);
                Assert.Equal(500m, restoredOpenItem.CurrentBaseCarryingAmount);
                Assert.Equal((byte)OpenItemState.Open, restoredOpenItem.State); // Open
            }
        }
    }
}
