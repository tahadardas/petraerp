#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class FxMonetaryEligibilitySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FxRevaluation_RespectsMonetaryEligibility_ExcludesNonMonetary_BlocksRequiresReview()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            Guid companyId;
            Guid branchId;
            var bankEurId = Guid.NewGuid();
            var prepaidEurId = Guid.NewGuid();
            var unclassifiedEurId = Guid.NewGuid();
            var equityId = Guid.NewGuid();

            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await readContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await readContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                branchId = branch.Id;
            }

            // Create accounts:
            // 1. Bank EUR - MonetaryEligible (1)
            // 2. Prepaid EUR - NonMonetary (2)
            // 3. Unclassified EUR - RequiresReview (4)
            // 4. Base Equity
            using (var accountContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                accountContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));

                if (!await accountContext.Currencies.AnyAsync(c => c.Code == "EUR", Cancellation).ConfigureAwait(true))
                {
                    accountContext.Currencies.Add(new CurrencyRow { Code = "EUR", NameArabic = "يورو", IsActive = true });
                }

                if (!await accountContext.CompanyAdditionalCurrencies.AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == "EUR", Cancellation).ConfigureAwait(true))
                {
                    accountContext.CompanyAdditionalCurrencies.Add(new CompanyAdditionalCurrencyRow
                    {
                        CompanyId = companyId,
                        CurrencyCode = "EUR",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    });
                }

                var bankEur = new AccountRow
                {
                    Id = bankEurId,
                    CompanyId = companyId,
                    Code = "1022",
                    NameArabic = "بنك يورو",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 3,
                    FixedCurrencyCode = "EUR",
                    IsActive = true,
                    FxMonetaryEligibility = (byte)FxMonetaryClassification.MonetaryEligible
                };

                var prepaidEur = new AccountRow
                {
                    Id = prepaidEurId,
                    CompanyId = companyId,
                    Code = "1151",
                    NameArabic = "مصاريف مدفوعة مقدما يورو",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 3,
                    FixedCurrencyCode = "EUR",
                    IsActive = true,
                    FxMonetaryEligibility = (byte)FxMonetaryClassification.NonMonetary
                };

                var unclassifiedEur = new AccountRow
                {
                    Id = unclassifiedEurId,
                    CompanyId = companyId,
                    Code = "1199",
                    NameArabic = "حساب قيد المراجعة",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 3,
                    FixedCurrencyCode = "EUR",
                    IsActive = true,
                    FxMonetaryEligibility = (byte)FxMonetaryClassification.RequiresReview
                };

                var equity = new AccountRow
                {
                    Id = equityId,
                    CompanyId = companyId,
                    Code = "3001",
                    NameArabic = "رأس المال",
                    AccountType = (byte)AccountType.Equity,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = (byte)FxMonetaryClassification.NonMonetary
                };

                accountContext.Accounts.AddRange(bankEur, prepaidEur, unclassifiedEur, equity);
                await accountContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // Post journal with EUR lines for bankEur and prepaidEur
            await PostOperationalJournalAsync(
                fixture,
                provisioned.WorkspaceId,
                companyId,
                provisioned.FiscalYearId,
                branchId,
                new DateOnly(2026, 2, 1),
                "EUR seed transactions",
                [
                    new JournalDraftLineInput { AccountId = bankEurId, TransactionDebit = 100m, TransactionCredit = 0m, TransactionCurrencyCode = "EUR", RateSourceCurrencyCode = "EUR", RateTargetCurrencyCode = "USD", RateValue = 1.10m, Description = "EUR Cash" },
                    new JournalDraftLineInput { AccountId = prepaidEurId, TransactionDebit = 50m, TransactionCredit = 0m, TransactionCurrencyCode = "EUR", RateSourceCurrencyCode = "EUR", RateTargetCurrencyCode = "USD", RateValue = 1.10m, Description = "Prepaid EUR" },
                    new JournalDraftLineInput { AccountId = equityId, TransactionDebit = 0m, TransactionCredit = 165m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "Equity offset" }
                ]).ConfigureAwait(true);

            // 1. Preview: Should include bankEur (MonetaryEligible) and EXCLUDE prepaidEur (NonMonetary)
            using (var previewContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var journalService = new SqlJournalEntryService(previewContext, TimeProvider.System);
                var currencyService = new SqlCurrencyService(previewContext, TimeProvider.System);
                var revalService = new SqlFxRevaluationService(previewContext, currencyService, journalService);

                var preview = await revalService.PreviewRevaluationAsync(companyId, branchId, new FxRevaluationPreviewCommand(
                    AsOfDate: new DateOnly(2026, 2, 28),
                    CurrencyCode: "EUR",
                    OverrideClosingRate: 1.20m), Cancellation).ConfigureAwait(true);

                Assert.Single(preview.Items);
                Assert.Equal(bankEurId, preview.Items[0].AccountId);
                Assert.Equal(100m, preview.Items[0].ForeignBalance);
                Assert.Equal(110m, preview.Items[0].BookBaseBalance);
                Assert.Equal(120m, preview.Items[0].RevaluedBaseBalance);
                Assert.Equal(10m, preview.Items[0].UnrealizedDifference);
                Assert.True(preview.Items[0].IsGain);
            }

            // 1b. Rate audit (read-only): with no bulletin rate the EUR lines are counted as "without rate"; once the bulletin says 1.25 the
            // lines booked at 1.10 show base differences of 15 and 7.5, and the base-currency equity line is never audited.
            using (var auditContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var auditService = new SqlFxRevaluationService(auditContext, new SqlCurrencyService(auditContext, TimeProvider.System), new SqlJournalEntryService(auditContext, TimeProvider.System));
                var noRate = await auditService.AuditRatesAsync(companyId, branchId, new FxRateAuditCommand(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "EUR"), Cancellation).ConfigureAwait(true);
                Assert.Empty(noRate.Items); Assert.Equal(2, noRate.LinesChecked); Assert.Equal(2, noRate.LinesWithoutBulletinRate);

                auditContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));
                auditContext.ExchangeRates.Add(new ExchangeRateRow { Id = Guid.NewGuid(), CompanyId = companyId, CurrencyCode = "EUR", BaseCurrencyCode = "USD", Rate = 1.25m,
                    EffectiveDate = new DateOnly(2026, 1, 1), Source = "test", Reason = "audit test", CreatedByUserId = fixture.Actor });
                await auditContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                var audited = await auditService.AuditRatesAsync(companyId, branchId, new FxRateAuditCommand(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "EUR"), Cancellation).ConfigureAwait(true);
                Assert.Equal(2, audited.Items.Count); Assert.Equal(0, audited.LinesWithoutBulletinRate);
                Assert.Equal(15m, audited.Items.Single(i => i.AccountId == bankEurId).Difference);
                Assert.Equal(7.5m, audited.Items.Single(i => i.AccountId == prepaidEurId).Difference);
                Assert.Equal(22.5m, audited.TotalDifference);
                Assert.All(audited.Items, item => { Assert.Equal(1.10m, item.BookedRate); Assert.Equal(1.25m, item.BulletinRate); });
            }

            // 2. Now add a EUR transaction for unclassifiedEur (RequiresReview) -> should throw fx.account_requires_review
            await PostOperationalJournalAsync(
                fixture,
                provisioned.WorkspaceId,
                companyId,
                provisioned.FiscalYearId,
                branchId,
                new DateOnly(2026, 2, 5),
                "RequiresReview EUR line",
                [
                    new JournalDraftLineInput { AccountId = unclassifiedEurId, TransactionDebit = 20m, TransactionCredit = 0m, TransactionCurrencyCode = "EUR", RateSourceCurrencyCode = "EUR", RateTargetCurrencyCode = "USD", RateValue = 1.10m, Description = "Unclassified" },
                    new JournalDraftLineInput { AccountId = equityId, TransactionDebit = 0m, TransactionCredit = 22m, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD", RateValue = 1m, Description = "Offset" }
                ]).ConfigureAwait(true);

            using (var previewContext2 = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var journalService2 = new SqlJournalEntryService(previewContext2, TimeProvider.System);
                var currencyService2 = new SqlCurrencyService(previewContext2, TimeProvider.System);
                var revalService2 = new SqlFxRevaluationService(previewContext2, currencyService2, journalService2);

                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => revalService2.PreviewRevaluationAsync(companyId, branchId, new FxRevaluationPreviewCommand(
                    AsOfDate: new DateOnly(2026, 2, 28),
                    CurrencyCode: "EUR",
                    OverrideClosingRate: 1.20m), Cancellation)).ConfigureAwait(true);

                Assert.Equal("fx.account_requires_review", ex.Code);
            }
        }
    }

    private static async Task PostOperationalJournalAsync(
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
