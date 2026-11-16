using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Parties.OpenItems;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties.OpenItems;

public sealed class SqlOpenItemService(FinancialDbContext context, TimeProvider timeProvider) : IOpenItemService
{
    public async Task<OpenItemDto> CreateOpenItemAsync(Guid companyId, CreateOpenItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OriginalAmount <= 0)
            throw new RequestValidationException("open_items.amount_invalid", "OriginalAmount must be positive.", nameof(command.OriginalAmount));
        if (command.ExchangeRate <= 0)
            throw new RequestValidationException("open_items.rate_invalid", "ExchangeRate must be positive.", nameof(command.ExchangeRate));

        var party = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PartyId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Party");

        var branch = await context.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == command.BranchId && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Branch");

        var account = await context.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == command.AccountId && a.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Account");

        var now = timeProvider.GetUtcNow();
        var baseAmount = decimal.Round(command.OriginalAmount * command.ExchangeRate, 4, MidpointRounding.ToEven);

        var row = new OpenItemRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = command.BranchId,
            PartyId = command.PartyId,
            PartyType = command.PartyType,
            OriginFiscalYearId = command.OriginFiscalYearId,
            OriginDocumentId = command.OriginDocumentId,
            OriginDocumentNumber = command.OriginDocumentNumber,
            OriginDocumentDateUtc = command.OriginDocumentDateUtc,
            DueDateUtc = command.DueDateUtc,
            CurrencyCode = command.CurrencyCode,
            OriginalAmount = command.OriginalAmount,
            SettledAmount = 0m,
            OutstandingAmount = command.OriginalAmount,
            ExchangeRate = command.ExchangeRate,
            OriginalBaseCarryingAmount = baseAmount,
            CurrentBaseCarryingAmount = baseAmount,
            OriginType = command.OriginType,
            SourceReference = command.SourceReference,
            RolloverBatchId = command.RolloverBatchId,
            AccountId = command.AccountId,
            State = (byte)OpenItemState.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.OpenItems.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapDto(row, party.NameArabic, account.Code);
    }

    public async Task<OpenItemDto> GetOpenItemAsync(Guid companyId, Guid openItemId, CancellationToken cancellationToken = default)
    {
        var item = await context.OpenItems.AsNoTracking().SingleOrDefaultAsync(o => o.Id == openItemId && o.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("OpenItem");

        var partyName = await context.Parties.Where(p => p.Id == item.PartyId).Select(p => p.NameArabic).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var accountCode = await context.Accounts.Where(a => a.Id == item.AccountId).Select(a => a.Code).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        return MapDto(item, partyName, accountCode);
    }

    public async Task<IReadOnlyList<OpenItemDto>> ListOpenItemsAsync(Guid companyId, Guid? branchId, Guid? partyId, byte? partyType, byte? state, string? currencyCode, CancellationToken cancellationToken = default)
    {
        var query = context.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId);
        if (branchId.HasValue) query = query.Where(o => o.BranchId == branchId.Value);
        if (partyId.HasValue) query = query.Where(o => o.PartyId == partyId.Value);
        if (partyType.HasValue) query = query.Where(o => o.PartyType == partyType.Value);
        if (state.HasValue) query = query.Where(o => o.State == state.Value);
        if (!string.IsNullOrWhiteSpace(currencyCode)) query = query.Where(o => o.CurrencyCode == currencyCode);

        var rows = await query.OrderByDescending(o => o.OriginDocumentDateUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        var partyIds = rows.Select(r => r.PartyId).Distinct().ToArray();
        var accountIds = rows.Select(r => r.AccountId).Distinct().ToArray();

        var parties = await context.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameArabic, cancellationToken).ConfigureAwait(false);
        var accounts = await context.Accounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken).ConfigureAwait(false);

        return rows.Select(r => MapDto(r, parties.GetValueOrDefault(r.PartyId, string.Empty), accounts.GetValueOrDefault(r.AccountId, string.Empty))).ToList();
    }

    public async Task<IReadOnlyList<PartyBalanceSummaryDto>> GetPartyBalancesAsync(Guid companyId, Guid? branchId, byte? partyType, CancellationToken cancellationToken = default)
    {
        var query = context.OpenItems.AsNoTracking().Where(o => o.CompanyId == companyId && o.State != (byte)OpenItemState.Cancelled);
        if (branchId.HasValue) query = query.Where(o => o.BranchId == branchId.Value);
        if (partyType.HasValue) query = query.Where(o => o.PartyType == partyType.Value);

        var grouped = await query.GroupBy(o => new { o.PartyId, o.PartyType, o.CurrencyCode })
            .Select(g => new
            {
                g.Key.PartyId,
                g.Key.PartyType,
                g.Key.CurrencyCode,
                TotalOriginal = g.Sum(x => x.OriginalAmount),
                TotalSettled = g.Sum(x => x.SettledAmount),
                TotalOutstanding = g.Sum(x => x.OutstandingAmount),
                TotalBase = g.Sum(x => x.CurrentBaseCarryingAmount)
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var partyIds = grouped.Select(g => g.PartyId).Distinct().ToArray();
        var parties = await context.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameArabic, cancellationToken).ConfigureAwait(false);

        return grouped.Select(g => new PartyBalanceSummaryDto(
            g.PartyId,
            parties.GetValueOrDefault(g.PartyId, string.Empty),
            g.PartyType,
            g.CurrencyCode,
            g.TotalOriginal,
            g.TotalSettled,
            g.TotalOutstanding,
            g.TotalBase)).ToList();
    }

    private static OpenItemDto MapDto(OpenItemRow r, string partyName, string accountCode) => new(
        r.Id,
        r.CompanyId,
        r.BranchId,
        r.PartyId,
        partyName,
        r.PartyType,
        r.OriginFiscalYearId,
        r.OriginDocumentId,
        r.OriginDocumentNumber,
        r.OriginDocumentDateUtc,
        r.DueDateUtc,
        r.CurrencyCode,
        r.OriginalAmount,
        r.SettledAmount,
        r.OutstandingAmount,
        r.ExchangeRate,
        r.OriginalBaseCarryingAmount,
        r.CurrentBaseCarryingAmount,
        r.OriginType,
        r.SourceReference,
        r.RolloverBatchId,
        r.AccountId,
        accountCode,
        r.State,
        r.CreatedAtUtc,
        r.UpdatedAtUtc,
        Convert.ToBase64String(r.RowVersion));
}
