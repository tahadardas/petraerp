using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class LedgerModelTests
{
    private static PetraDbContext Context() => new(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);

    [Fact]
    public void LedgerStorageHasEighteenExplicitTablesInLedgerSchema()
    {
        using var context = Context();
        var entities = context.Model.GetEntityTypes().Where(static e => e.GetSchema() == "ledger").ToArray();
        Assert.Equal(22, entities.Length);

        var expectedTypes = new[]
        {
            typeof(FiscalYearRow), typeof(AccountingPeriodRow),
            typeof(AccountRow), typeof(CostCenterRow),
            typeof(JournalEntryRow), typeof(JournalLineRow),
            typeof(JournalAuditRow), typeof(PostingRequestRow),
            typeof(PostingSequenceRow), typeof(PeriodAuditRow), typeof(AccountAuditRow), typeof(AccountAuditStateRow),
            typeof(CostCenterAuditRow), typeof(CostCenterAuditStateRow),
            typeof(PetraERP.Infrastructure.Currencies.FxRevaluationRequestRow),
            typeof(GeneralLedger.OpeningBalances.OpeningBalanceBatchRow),
            typeof(GeneralLedger.OpeningBalances.OpeningBalanceLineRow),
            typeof(CostCenterAllocationRow), typeof(AccountProfileRow), typeof(JournalVoucherHeaderRow)
        };

        foreach (var expected in expectedTypes)
        {
            Assert.Contains(entities, e => e.ClrType == expected);
        }

        // Restrictive deletion across all ledger relationships: no cascade delete allowed
        Assert.All(entities.SelectMany(static e => e.GetForeignKeys()),
            fk => Assert.True(fk.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction));
    }

    [Fact]
    public void LedgerMonetaryAndExchangeRatePrecisionsAreExact()
    {
        using var context = Context();
        var line = context.Model.FindEntityType(typeof(JournalLineRow))!;

        Assert.Equal("decimal(19,4)", line.FindProperty(nameof(JournalLineRow.TransactionDebit))!.GetColumnType());
        Assert.Equal("decimal(19,4)", line.FindProperty(nameof(JournalLineRow.TransactionCredit))!.GetColumnType());
        Assert.Equal("decimal(19,4)", line.FindProperty(nameof(JournalLineRow.BaseDebit))!.GetColumnType());
        Assert.Equal("decimal(19,4)", line.FindProperty(nameof(JournalLineRow.BaseCredit))!.GetColumnType());
        Assert.Equal("decimal(19,8)", line.FindProperty(nameof(JournalLineRow.RateValue))!.GetColumnType());
    }

    [Fact]
    public void AuditAndPostingRequestsAreAppendOnlyWithoutRowVersion()
    {
        using var context = Context();
        var audit = context.Model.FindEntityType(typeof(JournalAuditRow))!;
        var request = context.Model.FindEntityType(typeof(PostingRequestRow))!;
        var accountAudit = context.Model.FindEntityType(typeof(AccountAuditRow))!;

        Assert.Null(audit.FindProperty("RowVersion"));
        Assert.Null(request.FindProperty("RowVersion"));
        Assert.Null(accountAudit.FindProperty("RowVersion"));
        Assert.False(typeof(ITrackedStorageRow).IsAssignableFrom(typeof(JournalAuditRow)));
        Assert.False(typeof(ITrackedStorageRow).IsAssignableFrom(typeof(PostingRequestRow)));
        Assert.False(typeof(ITrackedStorageRow).IsAssignableFrom(typeof(AccountAuditRow)));

        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            audit.FindProperty(nameof(JournalAuditRow.EventId))!.ValueGenerated);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            request.FindProperty(nameof(PostingRequestRow.IdempotencyKey))!.ValueGenerated);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never,
            accountAudit.FindProperty(nameof(AccountAuditRow.EventId))!.ValueGenerated);
    }

    [Fact]
    public void PostingSequenceHasCompositePrimaryKey()
    {
        using var context = Context();
        var sequence = context.Model.FindEntityType(typeof(PostingSequenceRow))!;
        var pk = sequence.FindPrimaryKey()!;
        var keyNames = pk.Properties.Select(static p => p.Name).ToArray();

        Assert.Equal(3, keyNames.Length);
        Assert.Contains(nameof(PostingSequenceRow.CompanyId), keyNames);
        Assert.Contains(nameof(PostingSequenceRow.BranchId), keyNames);
        Assert.Contains(nameof(PostingSequenceRow.FiscalYearId), keyNames);
    }

    [Fact]
    public void PostingRequestHasCompositePrimaryKey()
    {
        using var context = Context();
        var request = context.Model.FindEntityType(typeof(PostingRequestRow))!;
        var pk = request.FindPrimaryKey()!;
        var keyNames = pk.Properties.Select(static p => p.Name).ToArray();

        Assert.Equal(4, keyNames.Length);
        Assert.Equal(
            [nameof(PostingRequestRow.CompanyId), nameof(PostingRequestRow.BranchId), nameof(PostingRequestRow.Operation), nameof(PostingRequestRow.IdempotencyKey)],
            keyNames);
    }

    [Fact]
    public void ModelHasNoPendingMigrations()
    {
        using var context = Context();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

