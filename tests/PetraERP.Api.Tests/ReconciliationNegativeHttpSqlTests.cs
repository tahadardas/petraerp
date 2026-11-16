using System.Net;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Ledger;
using PetraERP.Contracts.Patterns;
using PetraERP.Contracts.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Api.Tests;

/// <summary>
/// P02 acceptance on real SQL Server through the real API. A genuine sale, receipt voucher, additional settlement batches and a genuine
/// reversal are created through the product; then a disposable test database is deliberately corrupted at the storage layer (immutability
/// triggers are disabled only around the controlled mutation and re-enabled) and the reconciliation must report BLOCKING for each break,
/// and recover to non-BLOCKING once the mutation is reverted.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    private static Task<ReconciliationReportResponse> ReconcileAsync(ApiSqlHost host, HttpClient client, string token, string root) =>
        SendAsync<ReconciliationReportResponse>(host, client, HttpMethod.Get, root + "/reconciliation", token).ContinueWith(t =>
        {
            var response = t.Result;
            Assert.True(response.Status == HttpStatusCode.OK, $"reconciliation: HTTP {(int)response.Status} / {response.Code}");
            return response.Body!;
        }, TaskScheduler.Default);

    private static ReconciliationFindingResponse CheckOf(ReconciliationReportResponse report, string code) => report.Findings.Single(f => f.CheckCode == code);

    private const string SettlementTrigger = "[treasury].[TR_SettlementApplications_Immutable] ON [treasury].[SettlementApplications]";

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReconciliationReportsBlockingForBrokenSettlementLinkageStockAndPartyLedger()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var sale = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = DuplicatedItemSale(fixture), IdempotencyKey = $"rsale-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.Equal("Posted", sale.Body!.Outcome);
            var invoiceId = sale.Body.DocumentId;

            async Task<T> CreateAsync<T>(string path, object payload)
            {
                var response = await SendAsync<T>(host, client, HttpMethod.Post, root + path, token, payload).ConfigureAwait(true);
                Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{path}: {response.Status} / {response.Code}");
                return response.Body!;
            }
            var cash = (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
            {
                Code = "11002", NameArabic = "صندوق اختبار المطابقة", AccountType = AccountType.Assets, PostingAllowed = true,
                CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
            }).ConfigureAwait(true)).Id;
            var pattern = await CreateAsync<DocumentPatternResponse>("/patterns", new CreateDocumentPatternRequest
            {
                Code = "RR", NameArabic = "نمط سند قبض للمطابقة", ShortCode = "RR", OperationKind = BusinessOperationKind.CashReceipt, BranchIds = [host.BranchId],
                InitialConfiguration = new PatternConfigurationDto
                {
                    AccountMappings = [new() { Role = "Cash", AccountId = cash }, new() { Role = "Bank", AccountId = cash }],
                    NumberingPolicy = new() { Prefix = "RR-", MinimumDigits = 4, Scope = PatternNumberingScope.BranchAndFiscalYear },
                    ApprovalPolicy = new() { RequiresApproval = false },
                },
            }).ConfigureAwait(true);
            pattern = await CreateAsync<DocumentPatternResponse>($"/patterns/{pattern.Id}/publish", new PublishPatternRevisionRequest
            { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "نشر نمط سند القبض" }).ConfigureAwait(true);

            SaveVoucherRequest Receipt(string amount, string? settled) => new()
            {
                Kind = VoucherKind.Receipt, PatternRevisionId = pattern.ActiveRevision!.Id, PartyId = fixture.CustomerId, SettlementMethod = SettlementMethod.Cash,
                VoucherDateUtc = new DateTimeOffset(2026, 1, 25, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1", Amount = amount,
                InvoiceAllocations = settled is null ? [] : [new() { InvoiceId = invoiceId, InvoiceAmount = settled, SettlementAmount = settled, CrossRate = "1.00000000" }],
            };
            var vouchers = root + "/treasury/vouchers";
            var main = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, vouchers + "/save-and-process", token,
                new SaveAndProcessVoucherRequest { Document = Receipt("20", "10"), IdempotencyKey = $"rv-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.True(main.Body!.Outcome == "Posted", $"{main.Body.Outcome} [{main.Body.FailureCode}] {main.Body.Message}");
            var other = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, vouchers + "/save-and-process", token,
                new SaveAndProcessVoucherRequest { Document = Receipt("7", null), IdempotencyKey = $"ro-{Guid.NewGuid():N}" }).ConfigureAwait(true);
            Assert.True(other.Body!.Outcome == "Posted", $"{other.Body.Outcome} [{other.Body.FailureCode}] {other.Body.Message}");

            async Task ApplyAdvanceAsync(string amount)
            {
                var current = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Get, vouchers + $"/{main.Body.DocumentId}", token).ConfigureAwait(true);
                var applied = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, vouchers + $"/{main.Body.DocumentId}/apply-advance", token,
                    new ApplyAdvanceRequest
                    {
                        ExpectedVersion = current.Body!.Version, IdempotencyKey = $"radv-{Guid.NewGuid():N}", Reason = "تسوية الدفعة المقدمة",
                        DateUtc = new DateTimeOffset(2026, 1, 26, 12, 0, 0, TimeSpan.Zero),
                        Lines = [new() { InvoiceId = invoiceId, InvoiceAmount = amount, SettlementAmount = amount, CrossRate = "1.00000000" }],
                    }).ConfigureAwait(true);
                Assert.True(applied.Status == HttpStatusCode.OK, $"apply-advance: HTTP {(int)applied.Status} / {applied.Code}");
            }
            await ApplyAdvanceAsync("3").ConfigureAwait(true);
            await ApplyAdvanceAsync("2").ConfigureAwait(true);

            Guid activeBatch, reversedBatch, initialBatch;
            using (var annual = host.AnnualContext())
            {
                var later = await annual.Set<SettlementBatchRow>().AsNoTracking().Where(b => b.VoucherId == main.Body.DocumentId && !b.IsInitial).OrderBy(b => b.RecordedAtUtc)
                    .Select(b => b.Id).ToListAsync(Cancellation).ConfigureAwait(true);
                Assert.Equal(2, later.Count);
                activeBatch = later[0]; reversedBatch = later[1];
                initialBatch = await annual.Set<SettlementBatchRow>().AsNoTracking().Where(b => b.VoucherId == main.Body.DocumentId && b.IsInitial).Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            }
            var versionBeforeReverse = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Get, vouchers + $"/{main.Body.DocumentId}", token).ConfigureAwait(true);
            var reverse = await SendAsync<VoucherDetailDto>(host, client, HttpMethod.Post, vouchers + $"/{main.Body.DocumentId}/settlements/{reversedBatch}/reverse", token,
                new VoucherActionRequest { ExpectedVersion = versionBeforeReverse.Body!.SettlementBatches.Single(b => b.Id == reversedBatch).Version, Reason = "عكس دفعة تسوية للاختبار" }).ConfigureAwait(true);
            Assert.True(reverse.Status == HttpStatusCode.OK, $"reverse settlement: HTTP {(int)reverse.Status} / {reverse.Code}");

            // Baseline: a genuine reversal and genuine multiple batches are consistent.
            var baseline = await ReconcileAsync(host, client, token, root).ConfigureAwait(true);
            var baselineSettlement = CheckOf(baseline, "CHECK_09_SETTLEMENT_ALLOCATIONS");
            Assert.True(baselineSettlement.Severity == "PASS", $"baseline CHECK_09: {baselineSettlement.Severity} {baselineSettlement.Explanation}");
            foreach (var code in new[] { "CHECK_03_AR_SUBLEDGER_VS_CONTROL", "CHECK_06_INVENTORY_VS_GL", "CHECK_14_DUPLICATE_OR_ORPHAN_POSTING" })
            {
                var finding = CheckOf(baseline, code);
                Assert.True(finding.Severity != "BLOCKING", $"baseline {code}: {finding.Severity} {finding.Explanation}");
            }
            var baselineAr = CheckOf(baseline, "CHECK_03_AR_SUBLEDGER_VS_CONTROL");
            var baselineStock = CheckOf(baseline, "CHECK_06_INVENTORY_VS_GL");

            async Task ExpectBlockingAsync(string name, string code, string? marker, Func<Task<int>> mutate, Func<Task<int>> revert)
            {
                Assert.True(await mutate().ConfigureAwait(true) > 0, $"{name}: the controlled mutation changed no row");
                try
                {
                    var finding = CheckOf(await ReconcileAsync(host, client, token, root).ConfigureAwait(true), code);
                    Assert.True(finding.Severity == "BLOCKING", $"{name}: expected BLOCKING but {finding.Severity}: {finding.Explanation}");
                    if (marker is not null) Assert.Contains(marker, finding.Explanation, StringComparison.Ordinal);
                }
                finally { Assert.True(await revert().ConfigureAwait(true) > 0, $"{name}: the revert changed no row"); }
                var restored = CheckOf(await ReconcileAsync(host, client, token, root).ConfigureAwait(true), code);
                Assert.True(restored.Severity != "BLOCKING", $"{name}: not restored: {restored.Severity} {restored.Explanation}");
            }

            // 1. voucher targeting: an application re-pointed to another voucher while its batch still belongs to the original.
            Guid applicationId;
            SettlementBatchRow reversedRow, activeRow;
            using (var annual = host.AnnualContext())
            {
                applicationId = await annual.Set<SettlementApplicationRow>().AsNoTracking().Where(a => a.BatchId == activeBatch).Select(a => a.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                reversedRow = await annual.Set<SettlementBatchRow>().AsNoTracking().SingleAsync(b => b.Id == reversedBatch, Cancellation).ConfigureAwait(true);
                activeRow = await annual.Set<SettlementBatchRow>().AsNoTracking().SingleAsync(b => b.Id == activeBatch, Cancellation).ConfigureAwait(true);
            }
            Assert.Equal(2, reversedRow.State);
            Assert.NotNull(reversedRow.ReversalJournalId);
            await ExpectBlockingAsync("voucher targeting", "CHECK_09_SETTLEMENT_ALLOCATIONS", "APPLICATION_VOUCHER_DIFFERS_FROM_BATCH",
                () => ApplicationUpdateAsync(host, "VoucherId={0} WHERE Id={1}", other.Body.DocumentId, applicationId),
                () => ApplicationUpdateAsync(host, "VoucherId={0} WHERE Id={1}", main.Body.DocumentId, applicationId)).ConfigureAwait(true);

            // 2. reversal-journal linkage: reversed batch without / with the settlement's own journal as its reversal.
            await ExpectBlockingAsync("reversal journal removed", "CHECK_09_SETTLEMENT_ALLOCATIONS", "REVERSED_BATCH_WITHOUT_POSTED_REVERSAL_JOURNAL",
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET ReversalJournalId=NULL WHERE Id={0}", reversedBatch),
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET ReversalJournalId={0} WHERE Id={1}", reversedRow.ReversalJournalId!, reversedBatch)).ConfigureAwait(true);
            await ExpectBlockingAsync("reversal journal equals settlement journal", "CHECK_09_SETTLEMENT_ALLOCATIONS", "REVERSAL_JOURNAL_EQUALS_SETTLEMENT_JOURNAL",
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET ReversalJournalId=JournalId WHERE Id={0}", reversedBatch),
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET ReversalJournalId={0} WHERE Id={1}", reversedRow.ReversalJournalId!, reversedBatch)).ConfigureAwait(true);
            await ExpectBlockingAsync("active batch without journal", "CHECK_09_SETTLEMENT_ALLOCATIONS", "ACTIVE_BATCH_WITHOUT_POSTED_JOURNAL",
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET JournalId=NULL WHERE Id={0}", activeBatch),
                () => SqlAsync(host, "UPDATE [treasury].[SettlementBatches] SET JournalId={0} WHERE Id={1}", activeRow.JournalId!, activeBatch)).ConfigureAwait(true);

            // 3. duplicated application: the DB's unique (batch, target) index rejects a repeat inside one batch (asserted), so the only way to count
            //    one instruction twice is a second initial batch that clones it; the reconciliation must catch that duplicate.
            var sameBatchRepeat = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(host, """
                DECLARE @cols nvarchar(max) = (SELECT STRING_AGG(QUOTENAME(name), ',') FROM sys.columns WHERE object_id=OBJECT_ID(N'[treasury].[SettlementApplications]') AND name NOT IN (N'Id', N'RowVersion') AND is_computed=0);
                DECLARE @sql nvarchar(max) = N'INSERT INTO [treasury].[SettlementApplications] (Id,' + @cols + N') SELECT NEWID(),' + @cols + N' FROM [treasury].[SettlementApplications] WHERE Id=@source';
                EXEC sp_executesql @sql, N'@source uniqueidentifier', @source={0};
                """, applicationId)).ConfigureAwait(true);
            Assert.Contains("IX_SettlementApplications_BatchId_SalesInvoiceId", sameBatchRepeat.ToString(), StringComparison.Ordinal);
            var cloneBatch = Guid.NewGuid();
            await ExpectBlockingAsync("duplicated initial batch + application", "CHECK_09_SETTLEMENT_ALLOCATIONS", "DUPLICATE_INITIAL_BATCH",
                () => SqlAsync(host, """
                    DECLARE @bcols nvarchar(max) = (SELECT STRING_AGG(QUOTENAME(name), ',') FROM sys.columns WHERE object_id=OBJECT_ID(N'[treasury].[SettlementBatches]') AND name NOT IN (N'Id', N'RowVersion', N'RequestKey', N'Fingerprint') AND is_computed=0);
                    DECLARE @sqlb nvarchar(max) = N'INSERT INTO [treasury].[SettlementBatches] (Id,RequestKey,Fingerprint,' + @bcols + N') SELECT @newBatch, N''dup-'' + CONVERT(nvarchar(36), @newBatch), LOWER(CONVERT(varchar(64), HASHBYTES(''SHA2_256'', CONVERT(varchar(36), @newBatch)), 2)), ' + @bcols + N' FROM [treasury].[SettlementBatches] WHERE Id=@sourceBatch';
                    EXEC sp_executesql @sqlb, N'@newBatch uniqueidentifier, @sourceBatch uniqueidentifier', @newBatch={0}, @sourceBatch={1};
                    DECLARE @acols nvarchar(max) = (SELECT STRING_AGG(QUOTENAME(name), ',') FROM sys.columns WHERE object_id=OBJECT_ID(N'[treasury].[SettlementApplications]') AND name NOT IN (N'Id', N'RowVersion', N'BatchId') AND is_computed=0);
                    DECLARE @sqla nvarchar(max) = N'INSERT INTO [treasury].[SettlementApplications] (Id,BatchId,' + @acols + N') SELECT NEWID(), @newBatch, ' + @acols + N' FROM [treasury].[SettlementApplications] WHERE BatchId=@sourceBatch';
                    EXEC sp_executesql @sqla, N'@newBatch uniqueidentifier, @sourceBatch uniqueidentifier', @newBatch={0}, @sourceBatch={1};
                    """, cloneBatch, initialBatch),
                () => SqlAsync(host, $"DISABLE TRIGGER {SettlementTrigger}; DELETE FROM [treasury].[SettlementApplications] WHERE BatchId={{0}}; ENABLE TRIGGER {SettlementTrigger}; DELETE FROM [treasury].[SettlementBatches] WHERE Id={{0}};", cloneBatch)).ConfigureAwait(true);

            // 4. stock vs GL: the stock ledger is protected by CK_StockMovements_AuditBalance (asserted below), so the drift is introduced on the GL side:
            //    a posted line of an inventory account no longer agrees with the stock ledger.
            Guid inventoryLine;
            using (var annual = host.AnnualContext())
            {
                inventoryLine = await (from l in annual.Set<JournalLineRow>().AsNoTracking()
                                       join account in annual.Set<AccountRow>().AsNoTracking() on l.AccountId equals account.Id
                                       where account.Code == "19002"
                                       select l.Id).FirstAsync(Cancellation).ConfigureAwait(true);
                var movementId = await annual.Set<StockMovementRow>().AsNoTracking().Where(m => m.ValueAfter != null).Select(m => m.Id).FirstAsync(Cancellation).ConfigureAwait(true);
                var stockDrift = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(host, "UPDATE [inventory].[StockMovements] SET ValueAfter=ValueAfter+500 WHERE Id={0}", movementId)).ConfigureAwait(true);
                Assert.Contains("CK_StockMovements_AuditBalance", stockDrift.ToString(), StringComparison.Ordinal);
            }
            await ExpectBlockingAsync("stock vs GL", "CHECK_06_INVENTORY_VS_GL", null,
                () => SqlAsync(host, "UPDATE [ledger].[JournalLines] SET BaseDebit=BaseDebit+IIF(BaseDebit>0,{1},0), BaseCredit=BaseCredit+IIF(BaseCredit>0,{1},0), TransactionDebit=TransactionDebit+IIF(BaseDebit>0,{1},0), TransactionCredit=TransactionCredit+IIF(BaseCredit>0,{1},0) WHERE Id={0}", inventoryLine, 500m),
                () => SqlAsync(host, "UPDATE [ledger].[JournalLines] SET BaseDebit=BaseDebit+IIF(BaseDebit>0,{1},0), BaseCredit=BaseCredit+IIF(BaseCredit>0,{1},0), TransactionDebit=TransactionDebit+IIF(BaseDebit>0,{1},0), TransactionCredit=TransactionCredit+IIF(BaseCredit>0,{1},0) WHERE Id={0}", inventoryLine, -500m)).ConfigureAwait(true);

            // 5. party vs GL: the receivable line of the sale's journal no longer agrees with the subledger.
            Guid receivableLine;
            using (var annual = host.AnnualContext())
                receivableLine = await annual.Set<JournalLineRow>().AsNoTracking().Where(l => l.JournalId == sale.Body.JournalEntryId && l.AccountId == fixture.ReceivableId).Select(l => l.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            await ExpectBlockingAsync("party vs GL", "CHECK_03_AR_SUBLEDGER_VS_CONTROL", null,
                () => SqlAsync(host, "UPDATE [ledger].[JournalLines] SET BaseDebit=BaseDebit+IIF(BaseDebit>0,{1},0), BaseCredit=BaseCredit+IIF(BaseCredit>0,{1},0), TransactionDebit=TransactionDebit+IIF(BaseDebit>0,{1},0), TransactionCredit=TransactionCredit+IIF(BaseCredit>0,{1},0) WHERE Id={0}", receivableLine, 100m),
                () => SqlAsync(host, "UPDATE [ledger].[JournalLines] SET BaseDebit=BaseDebit+IIF(BaseDebit>0,{1},0), BaseCredit=BaseCredit+IIF(BaseCredit>0,{1},0), TransactionDebit=TransactionDebit+IIF(BaseDebit>0,{1},0), TransactionCredit=TransactionCredit+IIF(BaseCredit>0,{1},0) WHERE Id={0}", receivableLine, -100m)).ConfigureAwait(true);

            // Nothing changed permanently: the unmutated findings are identical to the baseline.
            var final = await ReconcileAsync(host, client, token, root).ConfigureAwait(true);
            Assert.Equal(baselineAr.Severity, CheckOf(final, "CHECK_03_AR_SUBLEDGER_VS_CONTROL").Severity);
            Assert.Equal(baselineStock.Severity, CheckOf(final, "CHECK_06_INVENTORY_VS_GL").Severity);
            Assert.Equal("PASS", CheckOf(final, "CHECK_09_SETTLEMENT_ALLOCATIONS").Severity);
        }
    }

    private static async Task<int> SqlAsync(ApiSqlHost host, string sql, params object[] parameters)
    {
        using var annual = host.AnnualContext();
        return await annual.Database.ExecuteSqlRawAsync(sql, parameters, Cancellation).ConfigureAwait(true);
    }

    private static Task<int> ApplicationUpdateAsync(ApiSqlHost host, string setAndWhere, params object[] parameters) =>
        SqlAsync(host, $"DISABLE TRIGGER {SettlementTrigger}; UPDATE [treasury].[SettlementApplications] SET {setAndWhere}; ENABLE TRIGGER {SettlementTrigger};", parameters);
}
