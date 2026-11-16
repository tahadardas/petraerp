using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>Al-Ameen item profile: stored on the item row, validated here, read back as one record.</summary>
public sealed partial class SqlInventoryService
{
    /// <summary>
    /// Validates the profile against the base unit and the company's units, then copies it onto the row. A null profile leaves
    /// the row untouched, so callers that predate the profile (older clients, imports) keep working.
    /// </summary>
    private async Task ApplyProfileAsync(ItemRow row, ItemProfileData? profile, Guid companyId, Guid baseUnitId, bool isNew, CancellationToken cancellationToken)
    {
        if (profile is null) return;
        profile.Validate(baseUnitId);
        var wanted = new[] { profile.Unit2Id, profile.Unit3Id }.OfType<Guid>().ToArray();
        if (wanted.Length > 0)
        {
            var found = await context.UnitsOfMeasure.AsNoTracking()
                .Where(u => u.CompanyId == companyId && wanted.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (found.Count != wanted.Length)
                throw new BusinessRuleException("inventory.profile_unit_not_found", "A unit of the item profile does not belong to this company.", "profile.unit2Id");
        }

        row.Unit2Id = profile.Unit2Id;
        row.Unit2Factor = profile.Unit2Id is null ? null : profile.Unit2Factor;
        row.Unit2FactorVariable = profile.Unit2Id is not null && profile.Unit2FactorVariable;
        row.Unit3Id = profile.Unit3Id;
        row.Unit3Factor = profile.Unit3Id is null ? null : profile.Unit3Factor;
        row.Unit3FactorVariable = profile.Unit3Id is not null && profile.Unit3FactorVariable;
        row.DefaultUnitIndex = profile.DefaultUnitIndex;
        row.GiftQuantity = profile.GiftQuantity;
        row.GiftPerQuantity = profile.GiftPerQuantity;
        row.Specifications = Clean(profile.Specifications);
        row.Measure = Clean(profile.Measure);
        row.Source = Clean(profile.Source);
        row.Location = Clean(profile.Location);
        row.Manufacturer = Clean(profile.Manufacturer);
        row.Color = Clean(profile.Color);
        row.OriginCountry = Clean(profile.OriginCountry);
        row.Quality = Clean(profile.Quality);
        row.Model = Clean(profile.Model);
        row.ItemKind = profile.ItemKind;
        row.IsAssembly = profile.IsAssembly;
        row.ComponentPricing = profile.ComponentPricing;
        row.TracksSerialNumber = profile.TracksSerialNumber;
        row.EnforceSerialOnReceipt = profile.TracksSerialNumber && profile.EnforceSerialOnReceipt;
        row.EnforceSerialOnIssue = profile.TracksSerialNumber && profile.EnforceSerialOnIssue;
        row.TracksProductionDate = profile.TracksProductionDate;
        row.TracksExpiryDate = profile.TracksExpiryDate;
        var dated = profile.TracksProductionDate || profile.TracksExpiryDate;
        row.EnforceDatesOnReceipt = dated && profile.EnforceDatesOnReceipt;
        row.EnforceDatesOnIssue = dated && profile.EnforceDatesOnIssue;
        row.HideInSearch = profile.HideInSearch;
        row.IntegerQuantityOnInvoice = profile.IntegerQuantityOnInvoice;
        row.ImagePath = Clean(profile.ImagePath);
        row.PricingPolicy = profile.PricingPolicy;
        row.LastPurchaseRate = profile.LastPurchaseRate;

        await SyncProfileConversionsAsync(row, companyId, baseUnitId, profile, isNew, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Units 2 and 3 are real item-specific conversions (unit x factor = base unit), so documents convert them with the same engine
    /// as every other unit. A factor can change only while the item has no stock movement; the opposite stored direction is a conflict.
    /// </summary>
    private async Task SyncProfileConversionsAsync(ItemRow row, Guid companyId, Guid baseUnitId, ItemProfileData profile, bool isNew, CancellationToken cancellationToken)
    {
        var existing = isNew
            ? new List<UnitConversionRow>()
            : await context.UnitConversions.Where(c => c.CompanyId == companyId && c.ItemId == row.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMovements = !isNew && await context.StockMovements.AnyAsync(m => m.CompanyId == companyId && m.ItemId == row.Id, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        foreach (var (unitId, factor, field) in new[] { (profile.Unit2Id, profile.Unit2Factor, "profile.unit2Factor"), (profile.Unit3Id, profile.Unit3Factor, "profile.unit3Factor") })
        {
            if (unitId is not { } unit || factor is not { } value) continue;
            if (existing.Any(c => c.FromUnitId == baseUnitId && c.ToUnitId == unit))
                throw new BusinessRuleException("inventory.conversion_conflict", "The item already has the opposite conversion for this unit.", field);
            var canonical = existing.FirstOrDefault(c => c.FromUnitId == unit && c.ToUnitId == baseUnitId);
            if (canonical is null)
            {
                context.UnitConversions.Add(new UnitConversionRow
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, ItemId = row.Id, FromUnitId = unit, ToUnitId = baseUnitId, Factor = value,
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                });
            }
            else if (canonical.Factor != value)
            {
                if (hasMovements)
                    throw new BusinessRuleException("inventory.conversion_immutable", "The conversion factor cannot change after the item has stock movements.", field);
                canonical.Factor = value;
                canonical.UpdatedAtUtc = now;
            }
        }
    }

    private static ItemProfileData ToProfile(ItemRow row) => new(
        Unit2Id: row.Unit2Id,
        Unit2Factor: row.Unit2Factor,
        Unit2FactorVariable: row.Unit2FactorVariable,
        Unit3Id: row.Unit3Id,
        Unit3Factor: row.Unit3Factor,
        Unit3FactorVariable: row.Unit3FactorVariable,
        DefaultUnitIndex: row.DefaultUnitIndex,
        GiftQuantity: row.GiftQuantity,
        GiftPerQuantity: row.GiftPerQuantity,
        Specifications: row.Specifications,
        Measure: row.Measure,
        Source: row.Source,
        Location: row.Location,
        Manufacturer: row.Manufacturer,
        Color: row.Color,
        OriginCountry: row.OriginCountry,
        Quality: row.Quality,
        Model: row.Model,
        ItemKind: row.ItemKind,
        IsAssembly: row.IsAssembly,
        ComponentPricing: row.ComponentPricing,
        TracksSerialNumber: row.TracksSerialNumber,
        EnforceSerialOnReceipt: row.EnforceSerialOnReceipt,
        EnforceSerialOnIssue: row.EnforceSerialOnIssue,
        TracksProductionDate: row.TracksProductionDate,
        TracksExpiryDate: row.TracksExpiryDate,
        EnforceDatesOnReceipt: row.EnforceDatesOnReceipt,
        EnforceDatesOnIssue: row.EnforceDatesOnIssue,
        HideInSearch: row.HideInSearch,
        IntegerQuantityOnInvoice: row.IntegerQuantityOnInvoice,
        ImagePath: row.ImagePath,
        PricingPolicy: row.PricingPolicy,
        LastPurchaseRate: row.LastPurchaseRate);

    /// <summary>
    /// Makes the item's barcodes exactly <paramref name="wanted"/> (a null list leaves them alone). Existing rows are kept when the
    /// value is unchanged, so their identity and history survive an unrelated edit.
    /// </summary>
    private async Task SyncBarcodesAsync(Guid companyId, Guid itemId, IReadOnlyList<CreateItemBarcodeCommand>? wanted, CancellationToken cancellationToken)
    {
        if (wanted is null) return;
        var requested = wanted.Where(b => !string.IsNullOrWhiteSpace(b.Barcode)).Select(b => (Barcode: b.Barcode.Trim(), b.UnitId, b.IsDefault)).ToList();
        if (requested.Select(b => b.Barcode).Distinct(StringComparer.Ordinal).Count() != requested.Count)
            throw new BusinessRuleException("inventory.barcode_duplicate", "A barcode is repeated on the item card.", "barcodes");
        var existing = await context.ItemBarcodes.Where(b => b.CompanyId == companyId && b.ItemId == itemId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var gone in existing.Where(e => requested.All(r => !string.Equals(r.Barcode, e.Barcode, StringComparison.Ordinal))))
            context.ItemBarcodes.Remove(gone);
        foreach (var (barcode, unitId, isDefault) in requested)
        {
            var found = existing.FirstOrDefault(e => string.Equals(e.Barcode, barcode, StringComparison.Ordinal));
            if (found is null)
                context.ItemBarcodes.Add(new ItemBarcodeRow { Id = Guid.NewGuid(), CompanyId = companyId, ItemId = itemId, UnitId = unitId, Barcode = barcode, IsDefault = isDefault });
            else
            {
                found.UnitId = unitId;
                found.IsDefault = isDefault;
            }
        }
    }

    public async Task DeleteItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var row = await context.Items.FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Item");
        if (await context.StockMovements.AnyAsync(m => m.CompanyId == companyId && m.ItemId == itemId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("inventory.item_has_movements", "لا يمكن حذف مادة عليها حركة، عطّلها بدلاً من ذلك / An item with stock movements cannot be deleted; deactivate it.");
        var mappings = await context.ItemAccountMappings.Where(m => m.CompanyId == companyId && m.OwnerType == 1 && m.OwnerId == itemId).ToListAsync(cancellationToken).ConfigureAwait(false);
        context.ItemAccountMappings.RemoveRange(mappings);
        context.Items.Remove(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.InnerException is DbUpdateException)
        {
            // A document line (sales, purchase, transfer, count) still points at the item: the database restricts the delete.
            throw new BusinessRuleException("inventory.item_in_use", "المادة مستخدمة في مستندات ولا يمكن حذفها، عطّلها بدلاً من ذلك / The item is used by documents; deactivate it.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
