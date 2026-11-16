#pragma warning disable CA1707

using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Application.Workspaces.Rollover;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

public sealed class FiscalYearRolloverSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FiscalYearRollover_PreviewAndExecute_CarriesBalancesAndActivatesTargetDatabase()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            // 1. Provision Source Year 2026
            var res2026 = await fixture.ProvisionAsync("ROLL").ConfigureAwait(true);
            Assert.True(res2026.Succeeded, res2026.Error);

            Guid branchId;
            Guid cashId;
            Guid revenueId;
            Guid expenseId;
            Guid retainedId;

            using (var annual2026 = await fixture.Factory.CreateDbContextAsync(res2026.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                branchId = await annual2026.Branches.Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                var root1 = await annual2026.Accounts.Where(a => a.Code == "1").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                var root3 = await annual2026.Accounts.Where(a => a.Code == "3").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                var root4 = await annual2026.Accounts.Where(a => a.Code == "4").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                var root5 = await annual2026.Accounts.Where(a => a.Code == "5").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);

                cashId = Guid.NewGuid();
                revenueId = Guid.NewGuid();
                expenseId = Guid.NewGuid();
                retainedId = Guid.NewGuid();

                annual2026.BindProvisioningScope(new AccountingWriteScope(res2026.CompanyId, res2026.FiscalYearId, fixture.Actor, PermissionCatalog.DatabaseManage));

                var cash = new AccountRow { Id = cashId, CompanyId = res2026.CompanyId, Code = "1101", NameArabic = "الصندوق", ParentId = root1, AccountType = 1, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 1 };
                var retained = new AccountRow { Id = retainedId, CompanyId = res2026.CompanyId, Code = "3301", NameArabic = "أرباح مدورة", ParentId = root3, AccountType = 3, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 2 };
                var revenue = new AccountRow { Id = revenueId, CompanyId = res2026.CompanyId, Code = "4101", NameArabic = "إيرادات المبيعات", ParentId = root4, AccountType = 4, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 2 };
                var expense = new AccountRow { Id = expenseId, CompanyId = res2026.CompanyId, Code = "5101", NameArabic = "مصاريف إدارية", ParentId = root5, AccountType = 5, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 2 };

                annual2026.Accounts.AddRange(cash, retained, revenue, expense);
                await annual2026.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // 2. Post balanced transaction in 2026:
            // Debit Cash 800, Debit Expense 200, Credit Revenue 1000
            // Net Income = Revenues (1000) - Expenses (200) = 800
            await PostOperationalJournalAsync(
                fixture,
                res2026.WorkspaceId,
                res2026.CompanyId,
                res2026.FiscalYearId,
                branchId,
                new DateOnly(2026, 3, 15),
                "2026 Operational Transactions",
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionDebit = 800m, TransactionCredit = 0m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "Cash collected" },
                    new JournalDraftLineInput { AccountId = expenseId, TransactionDebit = 200m, TransactionCredit = 0m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "Expense paid" },
                    new JournalDraftLineInput { AccountId = revenueId, TransactionDebit = 0m, TransactionCredit = 1000m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "Revenue recognized" }
                ]).ConfigureAwait(true);

            // 3. Provision Target Year 2027 with master-data copy enabled
            var input2027 = new ProvisionFiscalYearInput(
                CompanyId: res2026.CompanyId,
                YearNumber: 2027,
                YearCode: "FY2027",
                StartDate: new DateOnly(2027, 1, 1),
                EndDate: new DateOnly(2027, 12, 31),
                CopyChartOfAccounts: true,
                CopyWarehouses: true,
                CopyPatterns: true,
                SourceFiscalYearId: res2026.FiscalYearId);

            var res2027 = await fixture.Service.ProvisionFiscalYearDatabaseAsync(input2027, fixture.Actor, Cancellation).ConfigureAwait(true);
            Assert.True(res2027.Succeeded, res2027.Error);
            await fixture.Database.RegisterAnnualDatabaseAsync(res2027.DatabaseName).ConfigureAwait(true);

            // Verify target DB is initially OpeningBalancesPending
            var reg2027 = await fixture.System.FiscalDatabases.SingleAsync(d => d.Id == res2027.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal("OpeningBalancesPending", reg2027.State);

            // 4. Test Preview
            var rolloverService = new SqlFiscalYearRolloverService(fixture.System, fixture.Factory, TimeProvider.System);
            var preview = await rolloverService.PreviewRolloverAsync(new RolloverPreviewQuery(
                CompanyId: res2026.CompanyId,
                SourceFiscalYearId: res2026.FiscalYearId,
                TargetFiscalYearId: res2027.FiscalYearId,
                RetainedEarningsAccountId: retainedId), Cancellation).ConfigureAwait(true);

            Assert.Equal(1000m, preview.TotalRevenues);
            Assert.Equal(200m, preview.TotalExpenses);
            Assert.Equal(800m, preview.NetIncome);
            Assert.True(preview.IsTrialBalanceBalanced);
            Assert.Equal(retainedId, preview.RetainedEarningsAccountId);

            // 5. Test Execute
            var executeResult = await rolloverService.ExecuteRolloverAsync(fixture.Actor, new ExecuteRolloverCommand(
                CompanyId: res2026.CompanyId,
                SourceFiscalYearId: res2026.FiscalYearId,
                TargetFiscalYearId: res2027.FiscalYearId,
                RetainedEarningsAccountId: retainedId,
                SourceFingerprint: preview.SourceFingerprint,
                Reason: "Rolling over to FY2027",
                IdempotencyKey: "ROLLOVER-2026-2027-01"), Cancellation).ConfigureAwait(true);

            Assert.Equal("Activated", executeResult.Status);
            Assert.True(executeResult.TargetDatabaseActivated);

            // Verify target DB state transitioned to Active
            var updatedReg2027 = await fixture.System.FiscalDatabases.SingleAsync(d => d.Id == res2027.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal("Active", updatedReg2027.State);

            // Verify target DB contains posted opening balance journal with Cash Debit 800 and Retained Earnings Credit 800
            using (var annual2027 = await fixture.Factory.CreateDbContextAsync(res2027.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var targetBatch = await annual2027.OpeningBalanceBatches.Include(b => b.Lines).SingleAsync(Cancellation).ConfigureAwait(true);
                Assert.Equal(4, targetBatch.ApprovalState); // (byte)OpeningBalanceBatchState.Posted
                var totalDebit = targetBatch.Lines.Sum(l => l.DebitBase);
                var totalCredit = targetBatch.Lines.Sum(l => l.CreditBase);
                Assert.Equal(800m, totalDebit);
                Assert.Equal(800m, totalCredit);

                var targetJournal = await annual2027.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == targetBatch.PostedJournalId, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)JournalEntryState.Posted, targetJournal.State);
                Assert.Equal(2, targetJournal.Lines.Count);
                Assert.Contains(targetJournal.Lines, l => l.AccountId == cashId && l.BaseDebit == 800m && l.BaseCredit == 0m);
                Assert.Contains(targetJournal.Lines, l => l.AccountId == retainedId && l.BaseDebit == 0m && l.BaseCredit == 800m);
            }
        }
    }

    internal static async Task PostOperationalJournalAsync(
        IndependentAccountingSqlFixture fixture,
        Guid workspaceId,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        DateOnly date,
        string description,
        IReadOnlyList<JournalDraftLineInput> lines)
    {
        string versionBase64;
        Guid journalId;
        using (var draftCtx = await fixture.Factory.CreateDbContextAsync(workspaceId, Cancellation).ConfigureAwait(true))
        {
            draftCtx.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branchId));
            var service = new SqlJournalEntryService(draftCtx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, fixture.Actor);
            var draft = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = date,
                Description = description,
                Lines = lines
            }, Cancellation).ConfigureAwait(true);

            var validated = await service.ValidateDraftAsync(scope, draft.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(draft.VersionBase64)),
                Reason = "Validation"
            }, Cancellation).ConfigureAwait(true);

            journalId = draft.Id;
            versionBase64 = validated.VersionBase64;
        }

        using (var approveCtx = await fixture.Factory.CreateDbContextAsync(workspaceId, Cancellation).ConfigureAwait(true))
        {
            approveCtx.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, fixture.Actor, AccountingOperation.ApproveJournal, branchId));
            var service = new SqlJournalEntryService(approveCtx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, fixture.Actor);
            var approved = await service.ApproveDraftAsync(scope, journalId, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(versionBase64)),
                Reason = "Approve"
            }, Cancellation).ConfigureAwait(true);

            versionBase64 = approved.VersionBase64;
        }

        using (var postCtx = await fixture.Factory.CreateDbContextAsync(workspaceId, Cancellation).ConfigureAwait(true))
        {
            postCtx.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, fixture.Actor, AccountingOperation.PostJournal, branchId));
            var postingService = new SqlPostingService(postCtx);
            var postingRequest = PostingRequest.Post(
                PostingRequestScope.Create(companyId, branchId, fixture.Actor),
                Guid.NewGuid(),
                journalId,
                ExpectedJournalVersion.FromBase64(versionBase64),
                AuditReason.Create("Post test journal"));

            await postingService.PostAsync(postingRequest, Cancellation).ConfigureAwait(true);
        }
    }
}
