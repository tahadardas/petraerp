using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Application.Workspaces.Rollover;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Application.Inventory.Opening;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Workspaces;

public sealed class SqlFiscalYearRolloverService(
    SystemDbContext systemContext,
    IAccountingDbContextFactory contextFactory,
    TimeProvider timeProvider) : IFiscalYearRolloverService
{
    public async Task<RolloverPreviewDto> PreviewRolloverAsync(RolloverPreviewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sourceReg = await systemContext.FiscalDatabases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.CompanyId == query.CompanyId && d.FiscalYearId == query.SourceFiscalYearId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("SourceFiscalDatabase");

        var targetReg = await systemContext.FiscalDatabases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.CompanyId == query.CompanyId && d.FiscalYearId == query.TargetFiscalYearId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("TargetFiscalDatabase");

        using var sourceContext = await contextFactory.CreateDbContextAsync(query.CompanyId, query.SourceFiscalYearId, cancellationToken).ConfigureAwait(false);
        using var targetContext = await contextFactory.CreateDbContextAsync(query.CompanyId, query.TargetFiscalYearId, cancellationToken).ConfigureAwait(false);

        var sourceYear = await sourceContext.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == query.SourceFiscalYearId, cancellationToken).ConfigureAwait(false);
        var targetYear = await targetContext.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == query.TargetFiscalYearId, cancellationToken).ConfigureAwait(false);

        // Calculate source balances
        var postedLines = await (
            from line in sourceContext.JournalLines.AsNoTracking()
            join entry in sourceContext.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
            join account in sourceContext.Accounts.AsNoTracking() on line.AccountId equals account.Id
            where entry.CompanyId == query.CompanyId && (entry.State == 4 || entry.State == 5)
            group new { line.BaseDebit, line.BaseCredit } by new { line.AccountId, account.Code, account.NameArabic, account.AccountType, account.CurrencyMode, account.FixedCurrencyCode } into g
            select new
            {
                g.Key.AccountId,
                g.Key.Code,
                g.Key.NameArabic,
                g.Key.AccountType,
                g.Key.CurrencyMode,
                g.Key.FixedCurrencyCode,
                Debit = g.Sum(x => x.BaseDebit),
                Credit = g.Sum(x => x.BaseCredit),
                Balance = g.Sum(x => x.BaseDebit - x.BaseCredit)
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var totalDebit = postedLines.Sum(l => l.Debit);
        var totalCredit = postedLines.Sum(l => l.Credit);
        var isTrialBalanced = totalDebit == totalCredit;

        // Balance sheet vs Income statement
        var balanceSheetLines = postedLines.Where(l => l.AccountType is 1 or 2 or 3 && l.Balance != 0).ToList();
        var revenues = postedLines.Where(l => l.AccountType == 4).Sum(l => l.Credit - l.Debit);
        var expenses = postedLines.Where(l => l.AccountType == 5).Sum(l => l.Debit - l.Credit);
        var netIncome = revenues - expenses;

        // Retained earnings account
        var retainedAccountId = query.RetainedEarningsAccountId;
        if (!retainedAccountId.HasValue)
        {
            var autoRetained = await targetContext.Accounts.AsNoTracking()
                .Where(a => a.CompanyId == query.CompanyId && a.AccountType == 3 && (a.Code.StartsWith('3') || a.NameArabic.Contains("أرباح مدورة") || a.NameArabic.Contains("أرباح مرحلة")))
                .OrderBy(a => a.Code)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (autoRetained is not null) retainedAccountId = autoRetained.Id;
        }

        if (!retainedAccountId.HasValue || !await targetContext.Accounts.AsNoTracking().AnyAsync(a => a.Id == retainedAccountId.Value && a.CompanyId == query.CompanyId && a.IsActive && a.PostingAllowed, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("rollover.retained_earnings_missing", "A valid active Retained Earnings account must be configured in the target chart of accounts.");

        var retainedAcc = await targetContext.Accounts.AsNoTracking().SingleAsync(a => a.Id == retainedAccountId.Value, cancellationToken).ConfigureAwait(false);

        // Open items from source year
        var openSales = await SettlementQueries.LoadInvoicesAsync(sourceContext, query.CompanyId, null, VoucherKind.Receipt, null, null, null, cancellationToken).ConfigureAwait(false);
        var openPurchases = await SettlementQueries.LoadInvoicesAsync(sourceContext, query.CompanyId, null, VoucherKind.Payment, null, null, null, cancellationToken).ConfigureAwait(false);
        var openItemsCount = openSales.Count + openPurchases.Count;
        var openItemsTotal = openSales.Sum(s => s.Outstanding) + openPurchases.Sum(p => p.Outstanding);

        // Inventory balances from source year
        var stockBalances = await sourceContext.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == query.CompanyId)
            .GroupBy(m => new { m.WarehouseId, m.ItemId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ItemId,
                Quantity = g.Sum(m => m.BaseQuantity),
                Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost)
            })
            .Where(b => b.Quantity > 0)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var stockItemsCount = stockBalances.Count;
        var stockTotalValue = stockBalances.Sum(b => b.Value);

        var company = await systemContext.Companies.AsNoTracking().SingleAsync(c => c.Id == query.CompanyId, cancellationToken).ConfigureAwait(false);

        var accountLines = balanceSheetLines.Select(l => new RolloverAccountLineDto(
            l.AccountId,
            l.Code,
            l.NameArabic,
            l.AccountType,
            l.Balance > 0 ? l.Balance : 0m,
            l.Balance < 0 ? -l.Balance : 0m,
            company.BaseCurrencyCode)).ToList();

        var bsDebit = accountLines.Sum(l => l.ClosingDebit) + (netIncome < 0 ? -netIncome : 0m);
        var bsCredit = accountLines.Sum(l => l.ClosingCredit) + (netIncome > 0 ? netIncome : 0m);

        var fingerprintRaw = $"{query.CompanyId:N}:{query.SourceFiscalYearId:N}:{query.TargetFiscalYearId:N}:{netIncome}:{totalDebit}:{totalCredit}:{openItemsCount}:{openItemsTotal}:{stockItemsCount}:{stockTotalValue}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintRaw)));

        return new RolloverPreviewDto(
            query.CompanyId,
            query.SourceFiscalYearId,
            sourceYear.Code,
            query.TargetFiscalYearId,
            targetYear.Code,
            retainedAcc.Id,
            retainedAcc.Code,
            revenues,
            expenses,
            netIncome,
            bsDebit,
            bsCredit,
            isTrialBalanced,
            openItemsCount,
            openItemsTotal,
            stockItemsCount,
            stockTotalValue,
            fingerprint,
            accountLines);
    }

    public async Task<RolloverResultDto> ExecuteRolloverAsync(Guid actorId, ExecuteRolloverCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var preview = await PreviewRolloverAsync(new RolloverPreviewQuery(
            command.CompanyId,
            command.SourceFiscalYearId,
            command.TargetFiscalYearId,
            command.RetainedEarningsAccountId), cancellationToken).ConfigureAwait(false);

        if (!string.Equals(preview.SourceFingerprint, command.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("rollover.source_changed", "Source financial data changed after preview was generated; regenerate preview.");

        if (!preview.IsTrialBalanceBalanced)
            throw new BusinessRuleException("rollover.unbalanced_source", "Cannot rollover from an unbalanced source trial balance.");

        var targetReg = await systemContext.FiscalDatabases
            .SingleOrDefaultAsync(d => d.CompanyId == command.CompanyId && d.FiscalYearId == command.TargetFiscalYearId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("TargetFiscalDatabase");

        using var sourceContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.SourceFiscalYearId, cancellationToken).ConfigureAwait(false);
        using var targetContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false);

        // Check idempotency in target
        var existingBatch = await targetContext.OpeningBalanceBatches
            .SingleOrDefaultAsync(b => b.CompanyId == command.CompanyId && b.SourceFingerprint == command.SourceFingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (existingBatch is not null && existingBatch.ApprovalState == (byte)OpeningBalanceBatchState.Posted)
        {
            return new RolloverResultDto(
                existingBatch.Id,
                command.CompanyId,
                command.SourceFiscalYearId,
                command.TargetFiscalYearId,
                targetReg.State,
                existingBatch.Id,
                existingBatch.PostedJournalId ?? Guid.Empty,
                0,
                0,
                targetReg.State == "Active",
                "Rollover was already executed for this source fingerprint.");
        }

        var company = await systemContext.Companies.AsNoTracking().SingleAsync(c => c.Id == command.CompanyId, cancellationToken).ConfigureAwait(false);
        var targetYear = await targetContext.FiscalYears.AsNoTracking().SingleAsync(y => y.Id == command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false);
        var defaultBranch = await targetContext.Branches.AsNoTracking().Where(b => b.CompanyId == command.CompanyId).OrderBy(b => b.Code).FirstAsync(cancellationToken).ConfigureAwait(false);

        // Build opening balance lines
        var lines = new List<OpeningBalanceLineInput>();
        var order = 0;
        foreach (var l in preview.AccountLines)
        {
            var targetAccount = await targetContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == l.AccountCode && a.CompanyId == command.CompanyId, cancellationToken).ConfigureAwait(false);
            if (targetAccount is null || !targetAccount.IsActive || !targetAccount.PostingAllowed)
                throw new BusinessRuleException("rollover.account_missing", $"Target account {l.AccountCode} is missing or not allowed for posting.");

            lines.Add(new OpeningBalanceLineInput(
                targetAccount.Id,
                null,
                company.BaseCurrencyCode,
                l.ClosingDebit,
                l.ClosingCredit,
                1m,
                l.ClosingDebit,
                l.ClosingCredit,
                $"رصيد دورة من السنة {preview.SourceYearCode}",
                order++));
        }

        // Add Retained Earnings line for Net Income
        if (preview.NetIncome != 0)
        {
            var debit = preview.NetIncome < 0 ? -preview.NetIncome : 0m;
            var credit = preview.NetIncome > 0 ? preview.NetIncome : 0m;
            lines.Add(new OpeningBalanceLineInput(
                preview.RetainedEarningsAccountId,
                null,
                company.BaseCurrencyCode,
                debit,
                credit,
                1m,
                debit,
                credit,
                $"صافي أرباح/خسائر مرحلة من السنة {preview.SourceYearCode}",
                order++));
        }

        // Post opening balance batch in target
        OpeningBalanceBatchDto batchDto;
        using (var draftContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            draftContext.BindWriteScope(new(command.CompanyId, command.TargetFiscalYearId, actorId, AccountingOperation.ManageJournals, defaultBranch.Id));
            var openingService = new SqlOpeningBalanceService(draftContext, timeProvider);
            batchDto = await openingService.CreateDraftAsync(command.CompanyId, command.TargetFiscalYearId, actorId, new CreateOpeningBalanceBatchCommand(
                targetYear.RangeStart,
                "Rollover",
                preview.SourceYearCode,
                command.SourceFingerprint,
                company.BaseCurrencyCode,
                company.BaseCurrencyCode,
                defaultBranch.Id,
                command.IdempotencyKey,
                lines), cancellationToken).ConfigureAwait(false);
        }

        using (var approveContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            approveContext.BindWriteScope(new(command.CompanyId, command.TargetFiscalYearId, actorId, AccountingOperation.ApproveJournal, defaultBranch.Id));
            var openingService = new SqlOpeningBalanceService(approveContext, timeProvider);
            batchDto = await openingService.ApproveAsync(command.CompanyId, batchDto.Id, actorId, command.Reason, cancellationToken).ConfigureAwait(false);
        }

        using (var postContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            postContext.BindWriteScope(new(command.CompanyId, command.TargetFiscalYearId, actorId, AccountingOperation.PostJournal, defaultBranch.Id));
            var openingService = new SqlOpeningBalanceService(postContext, timeProvider);
            batchDto = await openingService.PostAsync(command.CompanyId, batchDto.Id, actorId, command.Reason, cancellationToken).ConfigureAwait(false);
        }

        // Roll over open items
        var openSales = await SettlementQueries.LoadInvoicesAsync(sourceContext, command.CompanyId, null, VoucherKind.Receipt, null, null, null, cancellationToken).ConfigureAwait(false);
        var openPurchases = await SettlementQueries.LoadInvoicesAsync(sourceContext, command.CompanyId, null, VoucherKind.Payment, null, null, null, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var openItemsCreated = 0;

        using (var itemsContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            itemsContext.BindWriteScope(new(command.CompanyId, command.TargetFiscalYearId, actorId, AccountingOperation.ManageParties, defaultBranch.Id));

            foreach (var inv in openSales)
            {
                var targetAccount = await itemsContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == inv.AccountId && a.CompanyId == command.CompanyId, cancellationToken).ConfigureAwait(false)
                    ?? await itemsContext.Accounts.AsNoTracking().FirstAsync(a => a.CompanyId == command.CompanyId && a.AccountType == 1 && a.PostingAllowed, cancellationToken).ConfigureAwait(false);

                itemsContext.OpenItems.Add(new OpenItemRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = command.CompanyId,
                    BranchId = inv.BranchId,
                    PartyId = inv.PartyId,
                    PartyType = 1, // Customer
                    OriginFiscalYearId = command.SourceFiscalYearId,
                    OriginDocumentId = inv.Id,
                    OriginDocumentNumber = inv.Number,
                    OriginDocumentDateUtc = inv.DateUtc,
                    DueDateUtc = inv.DateUtc.AddDays(30),
                    CurrencyCode = inv.CurrencyCode,
                    OriginalAmount = inv.Outstanding,
                    SettledAmount = 0m,
                    OutstandingAmount = inv.Outstanding,
                    ExchangeRate = inv.ExchangeRate,
                    OriginalBaseCarryingAmount = inv.OutstandingBase,
                    CurrentBaseCarryingAmount = inv.OutstandingBase,
                    OriginType = (byte)OpenItemOriginType.PriorYearInvoiceRollover,
                    SourceReference = $"{preview.SourceYearCode}:{inv.Number}",
                    RolloverBatchId = batchDto.Id,
                    AccountId = targetAccount.Id,
                    State = (byte)OpenItemState.Open,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                openItemsCreated++;
            }

            foreach (var inv in openPurchases)
            {
                var targetAccount = await itemsContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == inv.AccountId && a.CompanyId == command.CompanyId, cancellationToken).ConfigureAwait(false)
                    ?? await itemsContext.Accounts.AsNoTracking().FirstAsync(a => a.CompanyId == command.CompanyId && a.AccountType == 2 && a.PostingAllowed, cancellationToken).ConfigureAwait(false);

                itemsContext.OpenItems.Add(new OpenItemRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = command.CompanyId,
                    BranchId = inv.BranchId,
                    PartyId = inv.PartyId,
                    PartyType = 2, // Supplier
                    OriginFiscalYearId = command.SourceFiscalYearId,
                    OriginDocumentId = inv.Id,
                    OriginDocumentNumber = inv.Number,
                    OriginDocumentDateUtc = inv.DateUtc,
                    DueDateUtc = inv.DateUtc.AddDays(30),
                    CurrencyCode = inv.CurrencyCode,
                    OriginalAmount = inv.Outstanding,
                    SettledAmount = 0m,
                    OutstandingAmount = inv.Outstanding,
                    ExchangeRate = inv.ExchangeRate,
                    OriginalBaseCarryingAmount = inv.OutstandingBase,
                    CurrentBaseCarryingAmount = inv.OutstandingBase,
                    OriginType = (byte)OpenItemOriginType.PriorYearInvoiceRollover,
                    SourceReference = $"{preview.SourceYearCode}:{inv.Number}",
                    RolloverBatchId = batchDto.Id,
                    AccountId = targetAccount.Id,
                    State = (byte)OpenItemState.Open,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                openItemsCreated++;
            }

            if (openItemsCreated > 0)
            {
                await itemsContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Roll over opening stock
        var stockBalances = await sourceContext.StockMovements.AsNoTracking()
            .Where(m => m.CompanyId == command.CompanyId)
            .GroupBy(m => new { m.WarehouseId, m.ItemId, m.UnitId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ItemId,
                g.Key.UnitId,
                Quantity = g.Sum(m => m.BaseQuantity),
                Value = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost)
            })
            .Where(b => b.Quantity > 0)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // FIFO items carry their open cost layers into the new year one opening line per layer (oldest first), so the new
        // year keeps the real layer costs. Weighted-average items keep one aggregated line.
        var fifoItemIds = await sourceContext.Items.AsNoTracking().Where(i => i.CompanyId == command.CompanyId && i.CostMethod == (int)CostMethod.Fifo)
            .Select(i => new { i.Id, i.BaseUnitId }).ToDictionaryAsync(i => i.Id, i => i.BaseUnitId, cancellationToken).ConfigureAwait(false);
        var openingLines = stockBalances.Where(s => !fifoItemIds.ContainsKey(s.ItemId))
            .Select(s => new OpeningStockLineInput(s.WarehouseId, s.ItemId, s.UnitId, s.Quantity, s.Value, $"رصيد مخزني مدور من {preview.SourceYearCode}")).ToList();
        if (fifoItemIds.Count > 0)
        {
            var layers = await sourceContext.InventoryCostLayers.AsNoTracking()
                .Where(l => l.CompanyId == command.CompanyId && l.QuantityRemaining > 0 && fifoItemIds.Keys.Contains(l.ItemId))
                .OrderBy(l => l.SourceSequence).ToListAsync(cancellationToken).ConfigureAwait(false);
            openingLines.AddRange(layers.Select(l => new OpeningStockLineInput(l.WarehouseId, l.ItemId, fifoItemIds[l.ItemId], l.QuantityRemaining, l.ValueRemaining,
                $"طبقة FIFO مدورة من {preview.SourceYearCode}")));
        }

        var stockMovementsCreated = 0;
        if (openingLines.Count > 0)
        {
            using (var stockContext = await contextFactory.CreateDbContextAsync(command.CompanyId, command.TargetFiscalYearId, cancellationToken).ConfigureAwait(false))
            {
                stockContext.BindWriteScope(new(command.CompanyId, command.TargetFiscalYearId, actorId, AccountingOperation.ManageTransfers, defaultBranch.Id));
                var stockService = new SqlInventoryOpeningService(stockContext, timeProvider);
                var stockCmd = new CreateOpeningStockBatchCommand(
                    defaultBranch.Id,
                    targetYear.RangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    $"Rollover:{preview.SourceYearCode}",
                    $"STOCK-ROLL-{command.TargetFiscalYearId:N}",
                    openingLines);

                var stockResult = await stockService.PostOpeningStockBatchAsync(command.CompanyId, actorId, stockCmd, cancellationToken).ConfigureAwait(false);
                stockMovementsCreated = stockResult.Lines.Count;
            }
        }

        // Run reconciliation check on target
        var targetTrial = await (
            from l in targetContext.JournalLines.AsNoTracking()
            join j in targetContext.JournalEntries.AsNoTracking() on l.JournalId equals j.Id
            where j.CompanyId == command.CompanyId && (j.State == 4 || j.State == 5)
            select new { l.BaseDebit, l.BaseCredit }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var targetDebit = targetTrial.Sum(x => x.BaseDebit);
        var targetCredit = targetTrial.Sum(x => x.BaseCredit);

        if (targetDebit != targetCredit)
            throw new BusinessRuleException("rollover.target_unbalanced", $"Target trial balance after rollover is unbalanced: Debit {targetDebit}, Credit {targetCredit}.");

        // Activate target database in Central Registry!
        targetReg.State = "Active";
        SqlFiscalDatabaseResolver.InvalidateReadiness();
        targetReg.UpdatedAtUtc = now;
        await systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RolloverResultDto(
            batchDto.Id,
            command.CompanyId,
            command.SourceFiscalYearId,
            command.TargetFiscalYearId,
            "Activated",
            batchDto.Id,
            batchDto.PostedJournalId ?? Guid.Empty,
            openItemsCreated,
            stockMovementsCreated,
            true,
            "Rollover successfully applied and target fiscal database activated.");
    }
}
