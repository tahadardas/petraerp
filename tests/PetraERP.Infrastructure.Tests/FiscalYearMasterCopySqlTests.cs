using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class FiscalYearMasterCopySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompleteMasterDataCopyExcludesTransactionsAndMaintainsIntegrity()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result2026 = await fixture.ProvisionAsync("MASTER").ConfigureAwait(true);
            Assert.True(result2026.Succeeded, result2026.Error);

            Guid branchId;
            Guid warehouseId;
            Guid childAccountId;
            Guid costCenterId;
            Guid partyId;
            Guid unitId;
            Guid itemId;
            Guid patternId;

            // 1. Seed master data in FY2026 via provisioning scope
            using (var annual2026 = await fixture.Factory.CreateDbContextAsync(result2026.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                branchId = await annual2026.Branches.Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                warehouseId = await annual2026.Warehouses.Select(w => w.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                var root1 = await annual2026.Accounts.Where(a => a.Code == "1").Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);

                childAccountId = Guid.NewGuid();
                var childAccount = new AccountRow
                {
                    Id = childAccountId,
                    CompanyId = result2026.CompanyId,
                    Code = "1101",
                    NameArabic = "الصندوق الرئيسي",
                    NameEnglish = "Main Cash",
                    ParentId = root1,
                    AccountType = 1,
                    PostingAllowed = true,
                    CurrencyMode = 1,
                    CostCenterPolicy = 1,
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };

                costCenterId = Guid.NewGuid();
                var costCenter = new CostCenterRow
                {
                    Id = costCenterId,
                    CompanyId = result2026.CompanyId,
                    Code = "CC01",
                    NameArabic = "مركز الإدارة",
                    NameEnglish = "Administration CC",
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };

                partyId = Guid.NewGuid();
                var party = new PartyRow
                {
                    Id = partyId,
                    CompanyId = result2026.CompanyId,
                    Role = 1, // Customer
                    CreditPolicyKind = 1, // AllowWithWarning
                    Code = "CUST01",
                    NameArabic = "شركة الأمل",
                    NameEnglish = "Al-Amal Co",
                    NormalizedSearchText = "شركة الأمل",
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };

                unitId = Guid.NewGuid();
                var unit = new UnitOfMeasureRow
                {
                    Id = unitId,
                    CompanyId = result2026.CompanyId,
                    Code = "BOX",
                    NameArabic = "صندوق",
                    Symbol = "bx",
                };

                itemId = Guid.NewGuid();
                var item = new ItemRow
                {
                    Id = itemId,
                    CompanyId = result2026.CompanyId,
                    Code = "ITM01",
                    NameArabic = "سلعة أساسية",
                    NormalizedSearchText = "سلعة أساسية",
                    BaseUnitId = unitId,
                    DefaultWarehouseId = warehouseId,
                };

                var barcode = new ItemBarcodeRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = result2026.CompanyId,
                    ItemId = itemId,
                    UnitId = unitId,
                    Barcode = "9876543210123",
                };

                var itemPrice = new ItemPriceRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = result2026.CompanyId,
                    ItemId = itemId,
                    UnitId = unitId,
                    PriceListCode = "DEFAULT",
                    CurrencyCode = "USD",
                    Price = 250.0000m,
                };

                patternId = Guid.NewGuid();
                var pattern = new DocumentPatternRow
                {
                    Id = patternId,
                    CompanyId = result2026.CompanyId,
                    OperationKind = (int)BusinessOperationKind.ManualJournal,
                    Code = "MJ01",
                    NameArabic = "قيد يدوي",
                    NameEnglish = "Manual Journal",
                    ShortCode = "MJ",
                    IsActive = true,
                    IsDefault = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };

                var patternRevision = new PatternRevisionRow
                {
                    Id = Guid.NewGuid(),
                    PatternId = patternId,
                    RevisionNumber = 1,
                    State = 1, // Published
                    EffectiveFromUtc = DateTimeOffset.UtcNow,
                    ConfigurationJson = "{}",
                    CreatedByUserId = fixture.Actor,
                    Reason = "Initial revision",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                };

                pattern.ActiveRevisionId = patternRevision.Id;

                var grant = new PatternBranchGrantRow
                {
                    Id = Guid.NewGuid(),
                    PatternId = patternId,
                    BranchId = branchId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                };

                var policyRow = new CompanyPolicyRow
                {
                    CompanyId = result2026.CompanyId,
                    FiscalYearId = result2026.FiscalYearId,
                    NegativeIssuePolicy = (byte)NegativeIssuePolicy.RequestApproval,
                    RequireDocumentApproval = true,
                    AllowManualNumbering = true,
                    AllowPatternNumberingOverrides = true,
                    DefaultNumberingScope = 4,
                    InitialNumberSequence = 1,
                    CreatedByUserId = fixture.Actor,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };

                annual2026.BindProvisioningScope(new AccountingWriteScope(result2026.CompanyId, result2026.FiscalYearId, fixture.Actor, PermissionCatalog.DatabaseManage));
                var tx = await annual2026.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (tx.ConfigureAwait(true))
                {
                    annual2026.Accounts.Add(childAccount);
                    annual2026.CostCenters.Add(costCenter);
                    annual2026.Parties.Add(party);
                    annual2026.UnitsOfMeasure.Add(unit);
                    annual2026.Items.Add(item);
                    annual2026.ItemBarcodes.Add(barcode);
                    annual2026.ItemPrices.Add(itemPrice);
                    annual2026.DocumentPatterns.Add(pattern);
                    annual2026.PatternRevisions.Add(patternRevision);
                    annual2026.PatternBranchGrants.Add(grant);
                    annual2026.CompanyPolicies.Add(policyRow);
                    await annual2026.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    await tx.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }

            // 2. Seed transactional data in FY2026 via operational scopes
            using (var journalContext = await fixture.Factory.CreateDbContextAsync(result2026.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                journalContext.BindWriteScope(new AccountingWriteScope(result2026.CompanyId, result2026.FiscalYearId, fixture.Actor, PermissionCatalog.JournalsManage, branchId));
                var jTx = await journalContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (jTx.ConfigureAwait(true))
                {
                    var journal = new JournalEntryRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = result2026.CompanyId,
                        BranchId = branchId,
                        AccountingDate = new DateOnly(2026, 2, 1),
                        Description = "قيد مسودة للعام 2026",
                        State = (byte)JournalEntryState.Draft,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    };

                    journal.Lines.Add(new JournalLineRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = result2026.CompanyId,
                        JournalId = journal.Id,
                        AccountId = childAccountId,
                        TransactionCurrencyCode = "USD",
                        TransactionDebit = 500m,
                        TransactionCredit = 0m,
                        BaseCurrencyCode = "USD",
                        BaseDebit = 500m,
                        BaseCredit = 0m,
                        RateSourceCurrencyCode = "USD",
                        RateTargetCurrencyCode = "USD",
                        RateValue = 1m,
                        Description = "طرف مدين",
                    });

                    journal.Lines.Add(new JournalLineRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = result2026.CompanyId,
                        JournalId = journal.Id,
                        AccountId = childAccountId,
                        TransactionCurrencyCode = "USD",
                        TransactionDebit = 0m,
                        TransactionCredit = 500m,
                        BaseCurrencyCode = "USD",
                        BaseDebit = 0m,
                        BaseCredit = 500m,
                        RateSourceCurrencyCode = "USD",
                        RateTargetCurrencyCode = "USD",
                        RateValue = 1m,
                        Description = "طرف دائن",
                    });

                    journalContext.JournalEntries.Add(journal);
                    await journalContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    await jTx.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }

            using (var stockContext = await fixture.Factory.CreateDbContextAsync(result2026.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                stockContext.BindWriteScope(new AccountingWriteScope(result2026.CompanyId, result2026.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryAdjustmentsManage, branchId));
                var sTx = await stockContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (sTx.ConfigureAwait(true))
                {
                    var movement = new StockMovementRow
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = result2026.CompanyId,
                        BranchId = branchId,
                        WarehouseId = warehouseId,
                        ItemId = itemId,
                        UnitId = unitId,
                        MovementKind = (int)StockMovementKind.AdjustmentIncrease,
                        Quantity = 20m,
                        BaseQuantity = 20m,
                        UnitCost = 15m,
                        TotalCost = 300m,
                        MovementDateUtc = DateTimeOffset.UtcNow,
                        DocumentType = "StockInitial",
                        DocumentId = Guid.NewGuid(),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        CreatedByUserId = fixture.Actor,
                    };

                    stockContext.StockMovements.Add(movement);
                    await stockContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    await sTx.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }

            // 3. Provision FY2027 with master-data copy enabled
            var input2027 = new ProvisionFiscalYearInput(
                CompanyId: result2026.CompanyId,
                YearNumber: 2027,
                YearCode: "FY2027",
                StartDate: new DateOnly(2027, 1, 1),
                EndDate: new DateOnly(2027, 12, 31),
                CopyChartOfAccounts: true,
                CopyWarehouses: true,
                CopyPatterns: true,
                SourceFiscalYearId: result2026.FiscalYearId);

            var result2027 = await fixture.Service.ProvisionFiscalYearDatabaseAsync(input2027, fixture.Actor, Cancellation).ConfigureAwait(true);

            if (await fixture.System.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.databases WHERE name={result2027.DatabaseName}")
                .SingleAsync(Cancellation).ConfigureAwait(true) == 1)
            {
                await fixture.Database.RegisterAnnualDatabaseAsync(result2027.DatabaseName).ConfigureAwait(true);
            }

            Assert.True(result2027.Succeeded, result2027.Error);
            Assert.Equal("OpeningBalancesPending", result2027.ReadinessState);
            Assert.NotNull(result2027.Notice);
            Assert.Contains("تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية.", result2027.Notice);

            // 4. Inspect FY2027 database: verify master data present, transactions completely empty
            using (var annual2027 = await fixture.Factory.CreateDbContextAsync(result2027.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                // Master data copied
                Assert.Equal(6, await annual2027.Accounts.CountAsync(Cancellation).ConfigureAwait(true)); // 5 roots + 1 child
                Assert.True(await annual2027.Accounts.AnyAsync(a => a.Id == childAccountId && a.Code == "1101", Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.CostCenters.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.True(await annual2027.CostCenters.AnyAsync(c => c.Id == costCenterId && c.Code == "CC01", Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.Parties.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.True(await annual2027.Parties.AnyAsync(p => p.Id == partyId && p.Code == "CUST01", Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.Warehouses.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.UnitsOfMeasure.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.Items.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.True(await annual2027.Items.AnyAsync(i => i.Id == itemId && i.Code == "ITM01", Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.ItemBarcodes.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.ItemPrices.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.DocumentPatterns.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.True(await annual2027.DocumentPatterns.AnyAsync(p => p.Id == patternId && p.Code == "MJ01", Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.PatternRevisions.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.PatternBranchGrants.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual2027.CompanyPolicies.CountAsync(Cancellation).ConfigureAwait(true));

                // Transaction data EXCLUDED
                Assert.Equal(0, await annual2027.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.JournalLines.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.SalesInvoices.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.PurchaseInvoices.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.Vouchers.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await annual2027.FxRevaluationRequests.CountAsync(Cancellation).ConfigureAwait(true));
            }

            // 5. Central registry state verification
            var registry2027 = await fixture.System.FiscalDatabases.SingleAsync(r => r.Id == result2027.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal("OpeningBalancesPending", registry2027.State);
            Assert.False(registry2027.IsDefault);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MasterDataCopyEnforcesDependencyAndScopeValidation()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var resultA = await fixture.ProvisionAsync("COMPA").ConfigureAwait(true);
            Assert.True(resultA.Succeeded, resultA.Error);

            var resultB = await fixture.ProvisionAsync("COMPB").ConfigureAwait(true);
            Assert.True(resultB.Succeeded, resultB.Error);

            // 1. Dependency validation: Copying patterns without chart of accounts fails provisioning
            var invalidDependencyInput = new ProvisionFiscalYearInput(
                CompanyId: resultA.CompanyId,
                YearNumber: 2027,
                YearCode: "FY2027_DEP",
                StartDate: new DateOnly(2027, 1, 1),
                EndDate: new DateOnly(2027, 12, 31),
                CopyChartOfAccounts: false,
                CopyWarehouses: true,
                CopyPatterns: true,
                SourceFiscalYearId: resultA.FiscalYearId);

            var depResult = await fixture.Service.ProvisionFiscalYearDatabaseAsync(invalidDependencyInput, fixture.Actor, Cancellation).ConfigureAwait(true);
            await fixture.TrackProvisioningTargetAsync(depResult).ConfigureAwait(true);
            Assert.False(depResult.Succeeded);
            Assert.Contains("requires their chart of accounts", depResult.Error);

            // 2. Scope validation: Source year from a different company is rejected
            var crossCompanyInput = new ProvisionFiscalYearInput(
                CompanyId: resultB.CompanyId,
                YearNumber: 2027,
                YearCode: "FY2027_B",
                StartDate: new DateOnly(2027, 1, 1),
                EndDate: new DateOnly(2027, 12, 31),
                CopyChartOfAccounts: true,
                CopyWarehouses: true,
                CopyPatterns: true,
                SourceFiscalYearId: resultA.FiscalYearId); // Belongs to COMPA!

            var scopeResult = await fixture.Service.ProvisionFiscalYearDatabaseAsync(crossCompanyInput, fixture.Actor, Cancellation).ConfigureAwait(true);
            await fixture.TrackProvisioningTargetAsync(scopeResult).ConfigureAwait(true);
            Assert.False(scopeResult.Succeeded);
            Assert.NotNull(scopeResult.Error);
            var operation = await fixture.System.ProvisioningOperations.SingleAsync(o => o.Id == scopeResult.OperationId, Cancellation).ConfigureAwait(true);
            Assert.Equal("Failed", operation.Status);
            Assert.Contains("is not registered for company", operation.ErrorDetails);

            // 3. Date range validation: Source year end date must precede target start date
            var overlappingInput = new ProvisionFiscalYearInput(
                CompanyId: resultA.CompanyId,
                YearNumber: 2028,
                YearCode: "FY2028_OVER",
                StartDate: new DateOnly(2026, 6, 1), // Starts in 2026, YearNumber is 2028 -> invalid
                EndDate: new DateOnly(2027, 5, 31),
                CopyChartOfAccounts: true,
                CopyWarehouses: true,
                CopyPatterns: true,
                SourceFiscalYearId: resultA.FiscalYearId);

            var dateEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                fixture.Service.ProvisionFiscalYearDatabaseAsync(overlappingInput, fixture.Actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("fiscal_year.number_invalid", dateEx.Code);
        }
    }
}
