using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Parties;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Primitives;
using PetraERP.Application.Inventory;
using PetraERP.Application.Patterns;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Sales;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

public sealed class PartySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ArabicPartySearchPagesAndScopesOneThousandRows()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            for (var index = 0; index < 1000; index++)
            {
                var code = "P" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                context.Parties.Add(new PartyRow
                {
                    Id = Guid.NewGuid(), CompanyId = scenario.CompanyA, Code = code, NameArabic = "شركة الأمل",
                    NormalizedSearchText = ArabicTextNormalizer.Normalize(code + " شركة الأمل"),
                    Role = index == 999 ? 3 : 1, IsActive = index != 998, CreditPolicyKind = 1,
                });
            }
            context.Parties.Add(new PartyRow { Id = Guid.NewGuid(), CompanyId = scenario.CompanyB, Code = "P9999", NameArabic = "شركة الأمل",
                NormalizedSearchText = ArabicTextNormalizer.Normalize("P9999 شركة الأمل"), Role = 3, CreditPolicyKind = 1 });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var service = new SqlPartyService(context);
            var page = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery("امل", PartyRole.Customer, true, 20, 50), Cancellation).ConfigureAwait(true);
            Assert.Equal(999, page.TotalCount);
            Assert.Equal(49, page.Items.Count);
            Assert.Equal("P0999", page.Items[^1].Code);
            var suppliers = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery("أمل", PartyRole.Supplier, true), Cancellation).ConfigureAwait(true);
            Assert.Equal("P0999", Assert.Single(suppliers.Items).Code);
            var inactive = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery("امل", PartyRole.Customer, false), Cancellation).ConfigureAwait(true);
            Assert.Equal("P0998", Assert.Single(inactive.Items).Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PartyCreationAndCompanyUniqueness()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);

            var cmdA = new CreatePartyCommand(
                Code: "CUST-001",
                NameArabic: "شركة الأمل للتجارة",
                NameEnglish: "Al-Amal Trading Co",
                Role: PartyRole.Customer,
                TaxNumber: "TX-12345",
                CreditLimit: 10000m,
                PaymentTermDays: 30,
                PolicyKind: CreditPolicyKind.BlockOnExceed) { ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 };

            var partyA = await service.CreatePartyAsync(scenario.CompanyA, cmdA, Cancellation).ConfigureAwait(true);
            Assert.Equal("CUST-001", partyA.Code);
            Assert.Equal("شركة الأمل للتجارة", partyA.NameArabic);
            Assert.Equal("Al-Amal Trading Co", partyA.NameEnglish);
            Assert.Equal(PartyRole.Customer, partyA.Role);
            Assert.True(partyA.IsActive);
            Assert.Equal(10000m, partyA.CreditLimit);

            // Duplicate code in same company fails
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.CreatePartyAsync(scenario.CompanyA, cmdA, Cancellation)).ConfigureAwait(true);

            // Same code in different company succeeds
            var partyB = await service.CreatePartyAsync(scenario.CompanyB, cmdA with { ChangedInBranchId = scenario.BranchB1 }, Cancellation).ConfigureAwait(true);
            Assert.Equal(scenario.CompanyB, partyB.CompanyId);
            Assert.Equal("CUST-001", partyB.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PartyAccountValidationAndCompanyIntegrity()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);

            var accountA = await scenario.AccountAsync(scenario.CompanyA, "113101", AccountTypeCode.Assets, posting: true).ConfigureAwait(true);
            var accountB = await scenario.AccountAsync(scenario.CompanyB, "113101", AccountTypeCode.Assets, posting: true).ConfigureAwait(true);
            var headerAccount = await scenario.AccountAsync(scenario.CompanyA, "113100", AccountTypeCode.Assets, posting: false).ConfigureAwait(true);

            // Cross-company account assignment rejected
            var crossCompanyCmd = new CreatePartyCommand(
                Code: "CUST-CROSS",
                NameArabic: "زبون خارجي",
                NameEnglish: null,
                Role: PartyRole.Customer,
                ReceivableAccountId: accountB.Id);

            var crossEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartyAsync(scenario.CompanyA, crossCompanyCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.PartyAccountCrossCompany, RuleViolation.CodeOf(crossEx));

            // Non-posting header account rejected
            var headerCmd = new CreatePartyCommand(
                Code: "CUST-HEADER",
                NameArabic: "زبون رئيسي",
                NameEnglish: null,
                Role: PartyRole.Customer,
                ReceivableAccountId: headerAccount.Id);

            var headerEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartyAsync(scenario.CompanyA, headerCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.PartyAccountInvalid, RuleViolation.CodeOf(headerEx));

            // Valid posting account in same company succeeds
            var validCmd = new CreatePartyCommand(
                Code: "CUST-VALID",
                NameArabic: "زبون صالح",
                NameEnglish: null,
                Role: PartyRole.Customer,
                ReceivableAccountId: accountA.Id);

            var created = await service.CreatePartyAsync(scenario.CompanyA, validCmd, Cancellation).ConfigureAwait(true);
            Assert.Equal(accountA.Id, created.ReceivableAccountId);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ArabicSearchAndTokenPrefixQueries()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);

            await service.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "116",
                NameArabic: "مصاريف جبالة",
                NameEnglish: "Jeblah Expenses",
                Role: PartyRole.Both), Cancellation).ConfigureAwait(true);

            await service.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "P-200",
                NameArabic: "شركة الأمل للتجارة",
                NameEnglish: "Hope Trading",
                Role: PartyRole.Customer), Cancellation).ConfigureAwait(true);

            await service.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "P-300",
                NameArabic: "مؤسسة النور الحديثة",
                NameEnglish: "Modern Noor",
                Role: PartyRole.Supplier), Cancellation).ConfigureAwait(true);

            // Test exact PT-077 queries
            var q1 = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(SearchText: "مص 116"), Cancellation).ConfigureAwait(true);
            Assert.Single(q1.Items);
            Assert.Equal("116", q1.Items[0].Code);

            var q2 = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(SearchText: "جب 116"), Cancellation).ConfigureAwait(true);
            Assert.Single(q2.Items);
            Assert.Equal("116", q2.Items[0].Code);

            var q3 = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(SearchText: "116 مص"), Cancellation).ConfigureAwait(true);
            Assert.Single(q3.Items);
            Assert.Equal("116", q3.Items[0].Code);

            var q4 = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(SearchText: "مص ١١٦"), Cancellation).ConfigureAwait(true);
            Assert.Single(q4.Items);
            Assert.Equal("116", q4.Items[0].Code);

            // Test alef variation (أمل vs امل)
            var q5 = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(SearchText: "امل"), Cancellation).ConfigureAwait(true);
            Assert.Single(q5.Items);
            Assert.Equal("P-200", q5.Items[0].Code);

            // Test lookup method
            var lookups = await service.LookupPartiesAsync(scenario.CompanyA, "116 مص", null, 10, Cancellation).ConfigureAwait(true);
            Assert.Single(lookups);
            Assert.Equal("116", lookups[0].Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CreditExposureAndDerivedBalanceFromPostedJournals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);

            var recAccount = await scenario.AccountAsync(scenario.CompanyA, "113105", AccountTypeCode.Assets, posting: true).ConfigureAwait(true);
            var revAccount = await scenario.AccountAsync(scenario.CompanyA, "411101", AccountTypeCode.Revenue, posting: true).ConfigureAwait(true);
            var warehouse = await new SqlInventoryService(context).CreateWarehouseAsync(scenario.CompanyA,
                new CreateWarehouseCommand(scenario.BranchA1, "WH-EXP", "مستودع اختبار", null, null), Cancellation).ConfigureAwait(true);
            var patternSvc = new SqlPatternService(context);
            var pScope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);
            var pat = await patternSvc.CreatePatternAsync(pScope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal, Code = "MJ-PEXP", NameArabic = "سند قيد",
                ShortCode = "MJ", BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration { NumberingPolicy = new PatternNumberingPolicy { Prefix = "MJ-", MinimumDigits = 5 } }
            }, Cancellation).ConfigureAwait(true);
            var pubPat = await patternSvc.PublishRevisionAsync(pScope, pat.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = pat.Version, RevisionId = pat.ActiveRevision!.Id, Reason = "نشر أولي"
            }, Cancellation).ConfigureAwait(true);
            var revId = pubPat.ActiveRevision!.Id;

            var party = await service.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "CUST-EXP",
                NameArabic: "زبون الائتمان",
                NameEnglish: "Credit Customer",
                Role: PartyRole.Customer,
                ReceivableAccountId: recAccount.Id,
                CreditLimit: 50000m,
                PaymentTermDays: 15,
                PolicyKind: CreditPolicyKind.BlockOnExceed) { ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 }, Cancellation).ConfigureAwait(true);

            // Initially balance is 0
            var exposure0 = await service.GetPartyCreditExposureAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, exposure0.CurrentBalance);
            Assert.Equal(50000m, exposure0.AvailableCredit);
            Assert.False(exposure0.IsExceeded);

            // Post a journal debiting customer 20,000 USD
            var j1 = await scenario.JournalAsync(
                scenario.CompanyA,
                scenario.BranchA1,
                new DateOnly(2026, 1, 15),
                "USD",
                post: true,
                (recAccount.Id, 20000m, 0m),
                (revAccount.Id, 0m, 20000m)).ConfigureAwait(true);
            await context.JournalLines.Where(l => l.JournalId == j1.Id && l.AccountId == recAccount.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Description, "ذمم/نقد البيع"), Cancellation).ConfigureAwait(true);
            context.SalesInvoices.Add(new SalesInvoiceRow
            {
                Id = Guid.NewGuid(),
                CompanyId = scenario.CompanyA,
                BranchId = scenario.BranchA1,
                CustomerId = party.Id,
                PaymentMethod = 1, WarehouseId = warehouse.Id, PatternRevisionId = revId,
                Status = 1,
                JournalEntryId = j1.Id,
                CurrencyCode = "USD",
                ExchangeRate = 1.0m,
                InvoiceDateUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            // Verify derived exposure updated server-side without editable balance
            var exposure1 = await service.GetPartyCreditExposureAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(20000m, exposure1.TotalDebit);
            Assert.Equal(0m, exposure1.TotalCredit);
            Assert.Equal(20000m, exposure1.CurrentBalance);
            Assert.Equal(30000m, exposure1.AvailableCredit);
            Assert.False(exposure1.IsExceeded);

            // Post another 35,000 debit (total 55,000 exceeds 50,000 limit)
            var j2 = await scenario.JournalAsync(
                scenario.CompanyA,
                scenario.BranchA1,
                new DateOnly(2026, 1, 16),
                "USD",
                post: true,
                (recAccount.Id, 35000m, 0m),
                (revAccount.Id, 0m, 35000m)).ConfigureAwait(true);
            await context.JournalLines.Where(l => l.JournalId == j2.Id && l.AccountId == recAccount.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Description, "ذمم/نقد البيع"), Cancellation).ConfigureAwait(true);
            context.SalesInvoices.Add(new SalesInvoiceRow
            {
                Id = Guid.NewGuid(),
                CompanyId = scenario.CompanyA,
                BranchId = scenario.BranchA1,
                CustomerId = party.Id,
                PaymentMethod = 1, WarehouseId = warehouse.Id, PatternRevisionId = revId,
                Status = 1,
                JournalEntryId = j2.Id,
                CurrencyCode = "USD",
                ExchangeRate = 1.0m,
                InvoiceDateUtc = new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var exposure2 = await service.GetPartyCreditExposureAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(55000m, exposure2.TotalDebit);
            Assert.Equal(55000m, exposure2.CurrentBalance);
            Assert.Equal(0m, exposure2.AvailableCredit);
            Assert.True(exposure2.IsExceeded);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DeactivationAndReactivation()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);

            var party = await service.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "P-TOGGLE",
                NameArabic: "جهة قابلة للتعطيل",
                NameEnglish: null,
                Role: PartyRole.Supplier), Cancellation).ConfigureAwait(true);

            Assert.True(party.IsActive);

            await service.DeactivatePartyAsync(scenario.CompanyA, party.Id, "تجميد الحساب مؤقتاً", Cancellation).ConfigureAwait(true);
            var partyDeactivated = await service.GetPartyByIdAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            Assert.False(partyDeactivated.IsActive);

            // Excluded from active-only query
            var activeOnly = await service.GetPartiesAsync(scenario.CompanyA, new PartySearchQuery(IsActiveFilter: true), Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(activeOnly.Items, p => p.Id == party.Id);

            // Reactivate
            await service.ReactivatePartyAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            var partyReactivated = await service.GetPartyByIdAsync(scenario.CompanyA, party.Id, Cancellation).ConfigureAwait(true);
            Assert.True(partyReactivated.IsActive);
        }
    }
}
