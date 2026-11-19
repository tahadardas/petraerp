using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Parties;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Parties;

namespace PetraERP.Infrastructure.Tests;

public sealed class ChartOfAccountsSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountTreeListsCustomersAndSuppliersThatHadNoAccountYet()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var parties = new SqlPartyService(context);
            var both = await parties.CreatePartyAsync(s.CompanyA, new CreatePartyCommand("BOTH1", "طرف زبون ومورد", null, PartyRole.Both), Cancellation).ConfigureAwait(true);
            // Legacy / imported parties carry no accounts: the chart must still show them (Al-Ameen parity).
            await context.Parties.Where(p => p.Id == both.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.ReceivableAccountId, (Guid?)null).SetProperty(p => p.PayableAccountId, (Guid?)null), Cancellation).ConfigureAwait(true);

            // A request uses its own context: the chart is read through a fresh one, as the API does.
            using var request = database.Context();
            var tree = await LedgerScenario.Chart(request).GetAccountTreeAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true);

            var linked = await request.Parties.AsNoTracking().SingleAsync(p => p.Id == both.Id, Cancellation).ConfigureAwait(true);
            Assert.NotNull(linked.ReceivableAccountId);
            Assert.NotNull(linked.PayableAccountId);
            // One party, one ledger account (owner decision): the roles share the single account created for the party.
            Assert.Equal(linked.ReceivableAccountId, linked.PayableAccountId);
            Assert.Contains(tree, a => a.Id == linked.ReceivableAccountId && a.NameArabic == "طرف زبون ومورد");
            Assert.Contains(tree, a => a.Id == linked.PayableAccountId && a.NameArabic == "طرف زبون ومورد");
        }
    }

    private static AccountInput Input(AccountDto account, Action<AccountInputBuilder>? change = null)
    {
        var builder = new AccountInputBuilder(account);
        change?.Invoke(builder);
        return builder.Build();
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task HierarchyTreeLevelsAndCycleRejection()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var root = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var current = await s.AccountAsync(s.CompanyA, "11", AccountTypeCode.Assets, false, root.Id).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, current.Id).ConfigureAwait(true);
            await s.AccountAsync(s.CompanyA, "4", AccountTypeCode.Revenue, false).ConfigureAwait(true);

            using var context = database.Context();
            var chart = LedgerScenario.Chart(context);
            var tree = await chart.GetAccountTreeAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true);
            Assert.Equal(["1", "11", "1101", "4"], tree.Select(a => a.Code));
            Assert.Equal([0, 1, 2, 0], tree.Select(a => a.Level));
            Assert.True(tree[0].HasChildren);
            Assert.False(tree[2].HasChildren);

            // Root under its own grandchild's parent is a cycle; a posting account cannot be a parent.
            var cycle = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.UpdateAccountAsync(s.Master(s.CompanyA), root.Id,
                Input(root, b => b.Parent = current.Id), LedgerScenario.Token(root.VersionBase64), Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountHierarchyCycle, RuleViolation.CodeOf(cycle));
            var postingParent = await Assert.ThrowsAnyAsync<Exception>(() => s.AccountAsync(s.CompanyA, "1101-1", AccountTypeCode.Assets, true, cash.Id)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountParentInvalid, RuleViolation.CodeOf(postingParent));
            var missingParent = await Assert.ThrowsAnyAsync<Exception>(() => s.AccountAsync(s.CompanyA, "12", AccountTypeCode.Assets, false, Guid.NewGuid())).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountParentInvalid, RuleViolation.CodeOf(missingParent));
            var hasChildren = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.UpdateAccountAsync(s.Master(s.CompanyA), current.Id,
                Input(current, b => b.Posting = true), LedgerScenario.Token(current.VersionBase64), Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountHasChildren, RuleViolation.CodeOf(hasChildren));

            // Record navigation reads one row in ordinal code order and never crosses companies.
            var master = s.Master(s.CompanyA);
            async Task<string?> Go(AccountNavigationCode direction, Guid? from) =>
                (await chart.NavigateAccountAsync(master, direction, from, Cancellation).ConfigureAwait(true))?.Code;
            Assert.Equal("1", await Go(AccountNavigationCode.First, null).ConfigureAwait(true));
            Assert.Equal("4", await Go(AccountNavigationCode.Last, null).ConfigureAwait(true));
            Assert.Equal("11", await Go(AccountNavigationCode.Next, root.Id).ConfigureAwait(true));
            Assert.Equal("1", await Go(AccountNavigationCode.Previous, current.Id).ConfigureAwait(true));
            Assert.Null(await Go(AccountNavigationCode.Previous, root.Id).ConfigureAwait(true));
            var four = tree.Single(a => a.Code == "4");
            Assert.Null(await Go(AccountNavigationCode.Next, four.Id).ConfigureAwait(true));
            Assert.Equal("1101", await Go(AccountNavigationCode.Previous, four.Id).ConfigureAwait(true));
            await Assert.ThrowsAsync<RequestValidationException>(() => chart.NavigateAccountAsync(master, AccountNavigationCode.Next, null, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.NavigateAccountAsync(s.Master(s.CompanyB), AccountNavigationCode.Next, root.Id, Cancellation)).ConfigureAwait(true);
            Assert.Null(await chart.NavigateAccountAsync(s.Master(s.CompanyB), AccountNavigationCode.First, null, Cancellation).ConfigureAwait(true));

            var page = await chart.SearchAccountsAsync(s.Master(s.CompanyA), new AccountQuery("حساب 11", null, true, null, null, PageRequest.Create(1, 10)), Cancellation).ConfigureAwait(true);
            Assert.Equal(["11", "1101"], page.Items.Select(a => a.Code));
            Assert.Equal(2, page.Items[1].Level);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OnlyAnUnusedLeafIsDeletedWithAnAppendOnlyAuditSnapshot()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var root = await s.AccountAsync(s.CompanyA, "2", AccountTypeCode.Liabilities, false).ConfigureAwait(true);
            var leaf = await s.AccountAsync(s.CompanyA, "2101", AccountTypeCode.Liabilities, true, root.Id).ConfigureAwait(true);
            var draftOnly = await s.AccountAsync(s.CompanyA, "2102", AccountTypeCode.Liabilities, true, root.Id).ConfigureAwait(true);
            var other = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            // A draft journal line is a reference too.
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 2, 1), "USD", false, (draftOnly.Id, 0m, 5m), (other.Id, 5m, 0m)).ConfigureAwait(true);
            using var context = database.Context();
            var chart = LedgerScenario.Chart(context);
            var master = s.Master(s.CompanyA);

            var parent = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.DeleteAccountAsync(master, root.Id,
                LedgerScenario.Token(root.VersionBase64), "تنظيف", Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountHasChildren, RuleViolation.CodeOf(parent));
            draftOnly = await chart.GetAccountAsync(master, draftOnly.Id, Cancellation).ConfigureAwait(true);
            var draftUse = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.DeleteAccountAsync(master, draftOnly.Id,
                LedgerScenario.Token(draftOnly.VersionBase64), "تنظيف", Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountInUse, RuleViolation.CodeOf(draftUse));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => chart.DeleteAccountAsync(master, leaf.Id,
                LedgerScenario.Token(root.VersionBase64), "تنظيف", Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<ArgumentException>(() => chart.DeleteAccountAsync(master, leaf.Id,
                LedgerScenario.Token(leaf.VersionBase64), "   ", Cancellation)).ConfigureAwait(true);
            // Another company cannot see, so cannot delete, the account.
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.DeleteAccountAsync(s.Master(s.CompanyB), leaf.Id,
                LedgerScenario.Token(leaf.VersionBase64), "تنظيف", Cancellation)).ConfigureAwait(true);

            await chart.DeleteAccountAsync(master, leaf.Id, LedgerScenario.Token(leaf.VersionBase64), "  أُنشئ بالخطأ  ", Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetAccountAsync(master, leaf.Id, Cancellation)).ConfigureAwait(true);
            var tree = await chart.GetAccountTreeAsync(master, Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(tree, a => a.Id == leaf.Id);
            Assert.True(tree.Single(a => a.Id == root.Id).HasChildren);
            var audit = Assert.Single(await chart.GetDeletedAccountsAuditAsync(master, Cancellation).ConfigureAwait(true));
            Assert.Equal((leaf.Id, "2101", root.Id, s.Actor, "أُنشئ بالخطأ", AccountTypeCode.Liabilities),
                (audit.AccountId, audit.Code, audit.ParentId, audit.ActorUserId, audit.Reason, audit.AccountType));
            Assert.Empty(await chart.GetDeletedAccountsAuditAsync(s.Master(s.CompanyB), Cancellation).ConfigureAwait(true));

            // The audit is append-only in the database itself.
            var tamper = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [ledger].[AccountAudit]", Cancellation)).ConfigureAwait(true);
            Assert.Contains("append-only", tamper.Message, StringComparison.Ordinal);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DuplicateCodesConflictPerCompanyAndOtherCompaniesAreInvisible()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var a = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var duplicate = await Assert.ThrowsAsync<ResourceConflictException>(() => s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true)).ConfigureAwait(true);
            Assert.Equal(LedgerMasterErrorCodes.AccountCodeTaken, duplicate.Code);
            var b = await s.AccountAsync(s.CompanyB, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);

            using var context = database.Context();
            var chart = LedgerScenario.Chart(context);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetAccountAsync(s.Master(s.CompanyA), b.Id, Cancellation)).ConfigureAwait(true);
            Assert.DoesNotContain(await chart.GetAccountTreeAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true), x => x.Id == b.Id);
            var foreignParent = await Assert.ThrowsAnyAsync<Exception>(() => s.AccountAsync(s.CompanyB, "1", AccountTypeCode.Assets, false, a.Id)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountParentInvalid, RuleViolation.CodeOf(foreignParent));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.SetAccountActiveAsync(s.Master(s.CompanyB), a.Id, false,
                LedgerScenario.Token(a.VersionBase64), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StaleVersionsAreRejectedAndPostedHistoryFixesAccountingMeaning()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            using (var context = database.Context())
            {
                var chart = LedgerScenario.Chart(context);
                var renamed = await chart.UpdateAccountAsync(s.Master(s.CompanyA), cash.Id, Input(cash, b => b.Arabic = "الصندوق الرئيسي"),
                    LedgerScenario.Token(cash.VersionBase64), Cancellation).ConfigureAwait(true);
                Assert.Equal("الصندوق الرئيسي", renamed.NameArabic);
                Assert.NotEqual(cash.VersionBase64, renamed.VersionBase64);
                await Assert.ThrowsAsync<ConcurrencyConflictException>(() => chart.UpdateAccountAsync(s.Master(s.CompanyA), cash.Id,
                    Input(cash, b => b.Arabic = "قديم"), LedgerScenario.Token(cash.VersionBase64), Cancellation)).ConfigureAwait(true);
                cash = renamed;
            }

            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 5), "USD", true, (cash.Id, 100m, 0m), (sales.Id, 0m, 100m)).ConfigureAwait(true);
            using (var context = database.Context())
            {
                var chart = LedgerScenario.Chart(context);
                cash = await chart.GetAccountAsync(s.Master(s.CompanyA), cash.Id, Cancellation).ConfigureAwait(true);
                Assert.True(cash.HasJournalLines);
                var inUse = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.UpdateAccountAsync(s.Master(s.CompanyA), cash.Id,
                    Input(cash, b => b.Type = AccountTypeCode.Expenses), LedgerScenario.Token(cash.VersionBase64), Cancellation)).ConfigureAwait(true);
                Assert.Equal(RuleViolation.AccountInUse, RuleViolation.CodeOf(inUse));
                var code = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.UpdateAccountAsync(s.Master(s.CompanyA), cash.Id,
                    Input(cash, b => b.Code = "1102"), LedgerScenario.Token(cash.VersionBase64), Cancellation)).ConfigureAwait(true);
                Assert.Equal(RuleViolation.AccountInUse, RuleViolation.CodeOf(code));
                // A used account cannot be deleted; deactivation keeps its posted history.
                var deleteUsed = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.DeleteAccountAsync(s.Master(s.CompanyA), cash.Id,
                    LedgerScenario.Token(cash.VersionBase64), "حذف", Cancellation)).ConfigureAwait(true);
                Assert.Equal(RuleViolation.AccountInUse, RuleViolation.CodeOf(deleteUsed));
                var inactive = await chart.SetAccountActiveAsync(s.Master(s.CompanyA), cash.Id, false, LedgerScenario.Token(cash.VersionBase64), Cancellation).ConfigureAwait(true);
                Assert.False(inactive.IsActive);
                Assert.True(inactive.HasJournalLines);
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CostCentersAreCompanyScopedConcurrencyCheckedAndCodeFixedOnceUsed()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var chart = LedgerScenario.Chart(context);
            var center = await chart.CreateCostCenterAsync(s.Master(s.CompanyA), new CostCenterInput { Code = "C1", NameArabic = "مركز دمشق" }, Cancellation).ConfigureAwait(true);
            var duplicate = await Assert.ThrowsAsync<ResourceConflictException>(() => chart.CreateCostCenterAsync(s.Master(s.CompanyA),
                new CostCenterInput { Code = "C1", NameArabic = "مكرر" }, Cancellation)).ConfigureAwait(true);
            Assert.Equal(LedgerMasterErrorCodes.CostCenterCodeTaken, duplicate.Code);
            await chart.CreateCostCenterAsync(s.Master(s.CompanyB), new CostCenterInput { Code = "C1", NameArabic = "مركز حمص" }, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetCostCenterAsync(s.Master(s.CompanyB), center.Id, Cancellation)).ConfigureAwait(true);
            var listed = await chart.SearchCostCentersAsync(s.Master(s.CompanyA), new CostCenterQuery(null, null, PageRequest.Create(1, 50)), Cancellation).ConfigureAwait(true);
            Assert.Equal(center.Id, Assert.Single(listed.Items).Id);

            var renamed = await chart.UpdateCostCenterAsync(s.Master(s.CompanyA), center.Id, new CostCenterInput { Code = "C1", NameArabic = "مركز المزة" },
                LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => chart.SetCostCenterActiveAsync(s.Master(s.CompanyA), center.Id, false,
                LedgerScenario.Token(center.VersionBase64), Cancellation)).ConfigureAwait(true);
            var inactive = await chart.SetCostCenterActiveAsync(s.Master(s.CompanyA), center.Id, false, LedgerScenario.Token(renamed.VersionBase64), Cancellation).ConfigureAwait(true);
            Assert.False(inactive.IsActive);
            Assert.Equal("مركز المزة", inactive.NameArabic);
        }
    }

    private sealed class AccountInputBuilder(AccountDto account)
    {
        internal string Code { get; set; } = account.Code;
        internal string Arabic { get; set; } = account.NameArabic;
        internal AccountTypeCode Type { get; set; } = account.AccountType;
        internal bool Posting { get; set; } = account.PostingAllowed;
        internal Guid? Parent { get; set; } = account.ParentId;

        internal AccountInput Build() => new()
        {
            Code = Code,
            NameArabic = Arabic,
            NameEnglish = account.NameEnglish,
            AccountType = Type,
            PostingAllowed = Posting,
            CostCenterPolicy = account.CostCenterPolicy,
            CurrencyMode = account.CurrencyMode,
            FixedCurrencyCode = account.FixedCurrencyCode,
            ParentId = Parent,
        };
    }
}
