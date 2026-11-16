using PetraERP.ApiClient;

namespace PetraERP.TestDataImport;

internal sealed partial class LegacyImporter
{
    private async Task WarehousesAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var row in LegacyTable.Read(legacy, "st000"))
        {
            var key = Text(row, "GUID");
            if (map.TryGet("store", key, out _)) continue;
            try
            {
                var store = await Api.CreateWarehouseAsync(companyId, branchId, new CreateWarehouseRequest
                { BranchId = branchId, Code = "W" + Text(row, "Code"), NameArabic = Clip(Text(row, "Name"), 200), NameEnglish = Optional(Text(row, "LatinName")), Location = Optional(Text(row, "Address")) },
                    cancellationToken).ConfigureAwait(false);
                map.Set("store", key, store.Id!.Value); created++;
            }
            catch (PetraApiException exception) { rejected.Add($"warehouse {Text(row, "Code")}: {Problem(exception)}"); }
        }
        Line($"- Warehouses created: {created}");
    }

    private async Task ItemsAsync(CancellationToken cancellationToken)
    {
        var items = LegacyTable.Read(legacy, "mt000");
        // Ameen stores unit names per item card ("قطعة" and "قطعة " are the same unit); one PetraERP unit per trimmed name.
        var names = items.SelectMany(i => new[] { Text(i, "Unity"), Text(i, "Unit2"), Text(i, "Unit3") }).Where(n => n.Length != 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            if (map.TryGet("unit", names[index], out _)) continue;
            try
            {
                var unit = await Api.CreateUnitAsync(companyId, branchId, new CreateUnitRequest
                { Code = "U" + (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), NameArabic = Clip(names[index], 200), Symbol = Clip(names[index], 16), DecimalPlaces = 3 },
                    cancellationToken).ConfigureAwait(false);
                map.Set("unit", names[index], unit.Id!.Value);
            }
            catch (PetraApiException exception) { rejected.Add($"unit {names[index]}: {Problem(exception)}"); }
        }
        var created = 0;
        foreach (var row in items)
        {
            var key = Text(row, "GUID");
            if (map.TryGet("item", key, out _)) continue;
            var unitName = Text(row, "Unity");
            Guid? unitId = unitName.Length != 0 && map.TryGet("unit", unitName, out var found) ? found : null;
            try
            {
                var item = await Api.CreateItemAsync(companyId, branchId, new CreateItemRequest
                { Code = Text(row, "Code"), NameArabic = Clip(Text(row, "Name"), 200), NameEnglish = Optional(Text(row, "LatinName")), BaseUnitId = unitId },
                    cancellationToken).ConfigureAwait(false);
                map.Set("item", key, item.Id!.Value); created++;
                foreach (var (name, factor) in new[] { (Text(row, "Unit2"), Text(row, "Unit2Fact")), (Text(row, "Unit3"), Text(row, "Unit3Fact")) })
                {
                    if (name.Length == 0 || unitId is null || !map.TryGet("unit", name, out var alternate) || !decimal.TryParse(factor,
                        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0m) continue;
                    try
                    {
                        await Api.CreateUnitConversionAsync(companyId, branchId, new CreateUnitConversionRequest
                        { ItemId = item.Id, FromUnitId = alternate, ToUnitId = unitId, Factor = value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) },
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (PetraApiException exception) { rejected.Add($"unit conversion {Text(row, "Code")} {name}: {Problem(exception)}"); }
                }
            }
            catch (PetraApiException exception) { rejected.Add($"item {Text(row, "Code")}: {Problem(exception)}"); }
        }
        Line($"- Units: {names.Length}; items created: {created} of {items.Count} (no opening stock: Ameen quantity snapshots are not movement truth)");
    }

    private async Task PartiesAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        var rows = LegacyTable.Read(legacy, "cu000");
        foreach (var row in rows)
        {
            var key = Text(row, "GUID");
            if (map.TryGet("party", key, out _)) continue;
            var accountKey = Text(row, "AccountGUID");
            var hasAccount = accountKey != ZeroGuid && accounts.ContainsKey(accountKey) && map.TryGet("account", accountKey, out _);
            var category = hasAccount ? Category(accounts[accountKey]) : AccountType.Assets;
            // Ameen has one card type; the linked account decides: liabilities → supplier, otherwise customer.
            var supplier = category == AccountType.Liabilities;
            Guid? account = hasAccount && category is AccountType.Assets or AccountType.Liabilities ? map.Get("account", accountKey) : null;
            try
            {
                var party = await Api.CreatePartyAsync(companyId, branchId, new CreatePartyRequest
                {
                    Code = "P" + Text(row, "Number"), NameArabic = Clip(Text(row, "CustomerName"), 200), NameEnglish = Optional(Text(row, "LatinName")),
                    Role = supplier ? PartyRole.Supplier : PartyRole.Customer, DefaultCurrencyCode = baseCurrency, CreditLimit = "0.0000",
                    ReceivableAccountId = supplier ? null : account, PayableAccountId = supplier ? account : null,
                }, cancellationToken).ConfigureAwait(false);
                map.Set("party", key, party.Id!.Value); created++;
            }
            catch (PetraApiException exception) { rejected.Add($"party {Text(row, "Number")} {Text(row, "CustomerName")}: {Problem(exception)}"); }
        }
        Line($"- Parties created: {created} of {rows.Count}");
    }
}
