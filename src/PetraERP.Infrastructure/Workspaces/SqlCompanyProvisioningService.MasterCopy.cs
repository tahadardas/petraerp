using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Workspaces;

public sealed partial class SqlCompanyProvisioningService
{
    private async Task CopyPreviousYearMasterDataAsync(AccountingDbContext target, ProvisionFiscalYearInput input,
        Guid actorUserId, CancellationToken token)
    {
        if (!(input.CopyChartOfAccounts || input.CopyWarehouses || input.CopyPatterns)) return;
        if (input.SourceFiscalYearId is not { } sourceYear || sourceYear == Guid.Empty)
            throw new BusinessRuleException("fiscal_year.source_required", "Select the registered prior fiscal year explicitly.");
        if (input.CopyPatterns && !input.CopyChartOfAccounts)
            throw new BusinessRuleException("fiscal_year.copy_dependency", "Copying patterns requires their chart of accounts and cost centers.");
        var resolver = new SqlFiscalDatabaseResolver(systemContext, configuration, Microsoft.Extensions.Options.Options.Create(databaseOptions));
        var resolved = await resolver.ResolveByCompanyAndYearAsync(input.CompanyId, sourceYear, token).ConfigureAwait(false);
        if (resolved.YearNumber >= input.YearNumber || resolved.State != "Active")
            throw new BusinessRuleException("fiscal_year.source_invalid", "The source must be an active earlier year of the same company.");
        using var source=contextFactory.CreateDbContext(resolved.ConnectionString);
        if (await source.FiscalYears.AnyAsync(y=>y.RangeEnd>=input.StartDate,token).ConfigureAwait(false))
            throw new BusinessRuleException("fiscal_year.source_invalid","The selected source year must precede the target range.");
        using var read = await source.Database.BeginTransactionAsync(IsolationLevel.Serializable,token).ConfigureAwait(false);
        var now=DateTimeOffset.UtcNow;
        // IDs intentionally stay stable across independent annual databases. The mapping is the identity map;
        // references never point across databases. Scalar cloning excludes navigations and server row versions.
        async Task CopyAsync<T>() where T:class
        {
            var records=await source.Set<T>().AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            foreach(var record in records)
            {
                var clone=(T)(Activator.CreateInstance(typeof(T),nonPublic:true) ?? throw new InvalidOperationException("No master-data constructor."));
                var entry=target.Entry(clone);
                foreach(var property in source.Model.FindEntityType(typeof(T))!.GetProperties())
                {
                    if(property.Name=="RowVersion") continue;
                    var value=property.PropertyInfo!.GetValue(record);
                    if(property.Name is "CreatedAtUtc" or "UpdatedAtUtc") value=now;
                    if(property.Name is "CreatedByUserId" or "ActorUserId") value=actorUserId;
                    entry.Property(property.Name).CurrentValue=value;
                }
                if(clone is PatternRevisionRow revision) revision.Reason=$"Master-data copy from {sourceYear:D}, revision {revision.RevisionNumber}; opening balances not rolled over.";
                target.Set<T>().Add(clone);
            }
        }
        // The base catalog already contains the target base currency. Copy other required currency identities only.
        var currencies=await source.Currencies.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        foreach(var currency in currencies.Where(c=>!target.Currencies.Local.Any(t=>t.Code==c.Code)))
            if(!await target.Currencies.AnyAsync(c=>c.Code==currency.Code,token).ConfigureAwait(false))
                target.Currencies.Add(new CurrencyRow {Code=currency.Code,NameArabic=currency.NameArabic,NameEnglish=currency.NameEnglish,IsActive=currency.IsActive});
        if(input.CopyChartOfAccounts)
        {
            await CopyAsync<AccountRow>().ConfigureAwait(false);
            await CopyAsync<CostCenterRow>().ConfigureAwait(false);
            await CopyAsync<AccountProfileRow>().ConfigureAwait(false);
            await CopyAsync<CostCenterAllocationRow>().ConfigureAwait(false);
            await CopyAsync<PartyRow>().ConfigureAwait(false);
        }
        if(input.CopyWarehouses)
        {
            // Party- and pattern-specific price lists require the selected related masters. Do not produce dangling IDs.
            if(await source.ItemPrices.AnyAsync(p=>(p.PartyId!=null&&!input.CopyChartOfAccounts)||(p.PatternId!=null&&!input.CopyPatterns),token).ConfigureAwait(false))
                throw new BusinessRuleException("fiscal_year.copy_dependency","Inventory prices reference parties/patterns that were not selected for copying.");
            await CopyAsync<WarehouseRow>().ConfigureAwait(false);
            await CopyAsync<UnitOfMeasureRow>().ConfigureAwait(false);
            await CopyAsync<ItemGroupRow>().ConfigureAwait(false);
            await CopyAsync<ItemRow>().ConfigureAwait(false);
            if (input.CopyChartOfAccounts)
            {
                await CopyAsync<ItemAccountMappingRow>().ConfigureAwait(false);
            }
            await CopyAsync<UnitConversionRow>().ConfigureAwait(false);
            await CopyAsync<ItemBarcodeRow>().ConfigureAwait(false);
            await CopyAsync<ItemPriceRow>().ConfigureAwait(false);
        }
        if(input.CopyPatterns)
        {
            var revisions=await source.PatternRevisions.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            if(!input.CopyWarehouses && revisions.Any(r=>
            {
                var config=JsonSerializer.Deserialize<PatternConfiguration>(r.ConfigurationJson) ?? throw new BusinessRuleException("pattern.configuration_invalid","The source pattern configuration is invalid.");
                return config.DefaultWarehouseId is not null || config.AllowedWarehouseIds.Count>0;
            })) throw new BusinessRuleException("fiscal_year.copy_dependency","Selected patterns reference warehouses; select warehouse copying too.");
            await CopyAsync<DocumentPatternRow>().ConfigureAwait(false);
            await CopyAsync<PatternRevisionRow>().ConfigureAwait(false);
            await CopyAsync<PatternBranchGrantRow>().ConfigureAwait(false);
        }
        var sourcePolicy=await source.Set<PetraERP.Infrastructure.CompanyOptions.CompanyPolicyRow>().AsNoTracking().SingleOrDefaultAsync(p=>p.CompanyId==input.CompanyId,token).ConfigureAwait(false);
        if(sourcePolicy is not null)
        {
            var policy=PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.Policy(sourcePolicy);
            if(!input.CopyPatterns) policy=policy with {DefaultSalesPatternId=null,DefaultPurchasePatternId=null,DefaultReceiptPatternId=null,DefaultPaymentPatternId=null};
            var targetYear=await target.FiscalYears.Where(y=>y.CompanyId==input.CompanyId).Select(y=>y.Id).SingleAsync(token).ConfigureAwait(false);
            var row=new PetraERP.Infrastructure.CompanyOptions.CompanyPolicyRow {CompanyId=input.CompanyId,FiscalYearId=targetYear,CreatedByUserId=actorUserId};
            PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.Apply(row,policy);target.Add(row);
            target.Add(new PetraERP.Infrastructure.CompanyOptions.CompanyPolicyAuditRow {Id=Guid.CreateVersion7(),CompanyId=input.CompanyId,FiscalYearId=targetYear,EntityId=input.CompanyId,ActorUserId=actorUserId,EventType="CompanyPolicyCopied",Reason=$"Master configuration copied from {sourceYear:D}; opening balances not rolled over.",AfterJson=JsonSerializer.Serialize(policy),OccurredAtUtc=now});
        }
        // Deliberately no journal, stock movement, invoice, voucher, sequence or balance table is in this whitelist.
        await target.SaveChangesAsync(token).ConfigureAwait(false);
        await read.CommitAsync(token).ConfigureAwait(false);
    }
}
