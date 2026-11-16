using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>
/// Price lists: rows scoped by list, unit, currency, optional customer, optional pattern, quantity break and validity
/// window, and one server-side resolver (<see cref="PriceListPolicy"/>) that every client uses. No price, currency or unit
/// conversion is invented: when nothing applies the quote says so and the user enters the price.
/// </summary>
public sealed partial class SqlInventoryService
{
    public async Task<ItemPriceResult> AddItemPriceAsync(Guid companyId, Guid itemId, CreateItemPriceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var transaction = await CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var item = await context.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Item");
        var row = await NewPriceRowAsync(companyId, itemId, item.BaseUnitId, command, cancellationToken).ConfigureAwait(false);
        context.ItemPrices.Add(row);
        try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw new ResourceConflictException("A price already exists for this list, unit, currency, scope, break and start date.", exception);
        }
        var unitCode = await context.UnitsOfMeasure.AsNoTracking().Where(u => u.Id == row.UnitId).Select(u => u.Code).SingleAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToResult(row, unitCode);
    }

    public async Task DeleteItemPriceAsync(Guid companyId, Guid itemId, Guid priceId, CancellationToken cancellationToken = default)
    {
        // Documents copy their price, so removing a list row never changes history.
        var row = await context.ItemPrices.SingleOrDefaultAsync(p => p.Id == priceId && p.ItemId == itemId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("ItemPrice");
        context.ItemPrices.Remove(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PriceQuoteResult> QuotePriceAsync(Guid companyId, PriceQuoteQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Quantity < 0 || decimal.Round(query.Quantity, 4) != query.Quantity)
            throw new RequestValidationException("inventory.quantity_precision", "Quantity must be non-negative with at most four decimals.", "quantity");
        var item = await context.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == query.ItemId && i.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Item");

        Guid? patternId = null; string? patternJson = null;
        if (query.PatternRevisionId is { } revisionId)
        {
            var revision = await context.PatternRevisions.AsNoTracking().Where(r => r.Id == revisionId && r.Pattern!.CompanyId == companyId)
                .Select(r => new { r.PatternId, r.ConfigurationJson }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("PatternRevision");
            patternId = revision.PatternId; patternJson = revision.ConfigurationJson;
            if (query.BranchId is { } branch && await context.PatternBranchGrants.AnyAsync(g => g.PatternId == patternId, cancellationToken).ConfigureAwait(false)
                && !await context.PatternBranchGrants.AnyAsync(g => g.PatternId == patternId && g.BranchId == branch, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("inventory.price_pattern_invalid", "النمط غير متاح لهذا الفرع / Pattern is unavailable in this branch.", "patternRevisionId");
        }
        string? partyCurrency = null;
        if (query.PartyId is { } partyId)
        {
            var party = await context.Parties.AsNoTracking().Where(p => p.Id == partyId && p.CompanyId == companyId)
                .Select(p => new { p.DefaultCurrencyCode }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("Party");
            partyCurrency = party.DefaultCurrencyCode;
        }
        var baseCurrency = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).SingleAsync(cancellationToken).ConfigureAwait(false);
        string currency;
        try { currency = DocumentCurrencyPolicy.ResolveCode(query.CurrencyCode, partyCurrency, PatternCurrencyDefaults.DefaultCurrency(patternJson), baseCurrency); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, "currencyCode");
        }

        await CompanyCurrencies.RequireEnabledAsync(context, companyId, baseCurrency, currency, "inventory", cancellationToken).ConfigureAwait(false);
        if (!await context.UnitsOfMeasure.AnyAsync(u => u.Id == query.UnitId && u.CompanyId == companyId && u.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.unit_scope", "الوحدة غير فعالة أو خارج الشركة / Unit is inactive or outside this company.", "unitId");

        var rows = await context.ItemPrices.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.ItemId == item.Id && p.UnitId == query.UnitId && p.CurrencyCode == currency)
            .Select(p => new PriceCandidate(p.Id, p.PriceListCode, p.UnitId, p.CurrencyCode, p.Price, p.MinQuantity, p.PartyId, p.PatternId, p.ValidFrom, p.ValidTo))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var selected = PriceListPolicy.Select(rows, new PriceRequest(query.UnitId, currency, query.Quantity, query.Date, query.PartyId, patternId, query.PriceListCode));
        return selected is null
            ? new PriceQuoteResult(false, currency)
            : new PriceQuoteResult(true, currency, selected.Price, selected.Id, selected.PriceListCode, PriceListPolicy.Specificity(selected),
                selected.PartyId, selected.PatternId, selected.MinQuantity, selected.ValidFrom, selected.ValidTo);
    }

    /// <summary>Validates one price row against the company: list code, amounts and window, a unit the item can use, an enabled currency, and an in-company customer/pattern.</summary>
    private async Task<ItemPriceRow> NewPriceRowAsync(Guid companyId, Guid itemId, Guid baseUnitId, CreateItemPriceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var list = (command.PriceListCode ?? string.Empty).Trim().ToUpperInvariant();
        if (list.Length is 0 or > 64)
            throw new BusinessRuleException("inventory.price_list_invalid", "A price list code of 1–64 characters is required.", "priceListCode");
        try { PriceListPolicy.RequireValid(command.Price, command.MinQuantity, command.ValidFrom, command.ValidTo); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, "prices");
        }

        if (!await context.UnitsOfMeasure.AnyAsync(u => u.Id == command.UnitId && u.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.unit_scope", "The price unit does not belong to this company.", "unitId");
        if (command.UnitId != baseUnitId
            && await InventoryUnits.FindAsync(context, companyId, itemId, command.UnitId, baseUnitId, cancellationToken).ConfigureAwait(false) is null)
            throw new BusinessRuleException("inventory.unit_conversion_invalid", "A price unit must be the item's base unit or have a conversion to it.", "unitId");

        string currency;
        try { currency = DocumentCurrencyPolicy.Normalize(command.CurrencyCode) ?? throw RuleViolation.Create(DocumentCurrencyPolicy.CurrencyInvalid, "A price needs a currency."); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, "currencyCode");
        }
        var baseCurrency = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode).SingleAsync(cancellationToken).ConfigureAwait(false);
        await CompanyCurrencies.RequireEnabledAsync(context, companyId, baseCurrency, currency, "inventory", cancellationToken).ConfigureAwait(false);

        if (command.PartyId is { } partyId && !await context.Parties.AnyAsync(p => p.Id == partyId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.price_party_invalid", "The price customer does not belong to this company.", "partyId");
        if (command.PatternId is { } patternId && !await context.DocumentPatterns.AnyAsync(p => p.Id == patternId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.price_pattern_invalid", "The price pattern does not belong to this company.", "patternId");

        var now = Clock.GetUtcNow();
        return new ItemPriceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ItemId = itemId,
            UnitId = command.UnitId,
            PriceListCode = list,
            Price = command.Price,
            CurrencyCode = currency,
            MinQuantity = command.MinQuantity,
            PartyId = command.PartyId,
            PatternId = command.PatternId,
            ValidFrom = command.ValidFrom,
            ValidTo = command.ValidTo,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static UnitDimension RequireDimension(UnitDimension dimension) =>
        Enum.IsDefined(dimension) ? dimension : throw new BusinessRuleException("inventory.unit_dimension_invalid", "The unit dimension is not supported.", "dimension");

    private static ItemPriceResult ToResult(ItemPriceRow p, string unitCode) => new(p.Id, p.ItemId, p.UnitId, unitCode, p.PriceListCode, p.Price,
        p.CurrencyCode, p.MinQuantity, p.PartyId, p.PatternId, p.ValidFrom, p.ValidTo);
}
