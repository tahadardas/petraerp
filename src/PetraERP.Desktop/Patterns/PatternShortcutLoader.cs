using PetraERP.ApiClient;
using PetraERP.Desktop.Caching;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Patterns;

/// <summary>
/// The patterns a list window offers as "new document" buttons (Al-Ameen: one button per pattern). A pattern is
/// listed only while it is runnable and has an active revision; a failed read leaves the plain new-document button.
/// Loaded fresh per screen; scoped caching belongs to the session-aware client layer (see <see cref="CacheScope"/>).
/// </summary>
internal static class PatternShortcutLoader
{
    private static PatternAccountMappingDto? Mapping(PatternConfigurationDto? config, params string[] roles) =>
        config?.AccountMappings?.FirstOrDefault(m => roles.Any(role => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Mirrors the server rule: the role is whitelisted for document overrides, or its mapping is not enforced.</summary>
    private static bool AllowsOverride(PatternConfigurationDto? config, params string[] roles) =>
        config?.OptionOverridesWhitelist?.Any(w => roles.Any(role => string.Equals(w, role, StringComparison.OrdinalIgnoreCase))) == true
        || Mapping(config, roles)?.AllowOverride == true;

    public static Task<IReadOnlyList<PatternLookupItem>> LoadAsync(
        IPatternsClient client,
        Guid companyId,
        Guid branchId,
        BusinessOperationKind kind,
        CancellationToken cancellationToken) =>
        // Not cached here: this helper has no workspace identity, and a company-only key mixed fiscal years. The scoped cache
        // (CacheScope) is applied at the session-aware client layer; until a pattern bootstrap exists this is one small read.
        LoadCoreAsync(client, companyId, branchId, k => k == kind, cancellationToken);

    public static Task<IReadOnlyList<PatternLookupItem>> LoadAsync(
        IPatternsClient client,
        Guid companyId,
        Guid branchId,
        Func<BusinessOperationKind?, bool> predicate,
        CancellationToken cancellationToken) =>
        LoadCoreAsync(client, companyId, branchId, predicate, cancellationToken);

    private static async Task<IReadOnlyList<PatternLookupItem>> LoadCoreAsync(
        IPatternsClient client,
        Guid companyId,
        Guid branchId,
        Func<BusinessOperationKind?, bool> predicate,
        CancellationToken cancellationToken)
    {
        try
        {
            var runnable = await client.GetRunnablePatternsAsync(companyId, branchId, cancellationToken).ConfigureAwait(true);
            var tasks = runnable.Where(p => predicate(p.OperationKind) && p.Id.HasValue).Select(async p =>
            {
                var detail = await client.GetPatternAsync(companyId, branchId, p.Id!.Value, cancellationToken).ConfigureAwait(false);
                var config = detail.ActiveRevision?.Configuration;
                return detail.ActiveRevision?.Id is not { } revision ? null
                    : new PatternLookupItem
                    {
                        PatternId = p.Id.Value,
                        RevisionId = revision,
                        Name = p.NameArabic ?? p.Code ?? string.Empty,
                        LockDocumentDate = config?.EntryPolicy?.LockDocumentDate == true,
                        AllowManualNumbering = config?.NumberingPolicy?.AllowManualNumbering == true,
                        DefaultWarehouseId = config?.DefaultWarehouseId,
                        DefaultCostCenterId = config?.DefaultCostCenterId,
                        DefaultCurrencyCode = config?.DefaultCurrencyCode,
                        DefaultPaymentMethod = config?.AllowedPaymentMethods is { Count: > 0 } methods ? methods[0] : null,
                        FieldCaptions = (config?.LineFields ?? []).Where(f => !string.IsNullOrWhiteSpace(f.FieldName) && !string.IsNullOrWhiteSpace(f.CaptionArabic))
                            .GroupBy(f => f.FieldName!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().CaptionArabic!.Trim(), StringComparer.OrdinalIgnoreCase),
                        OperationKind = p.OperationKind,
                        CashAccountId = Mapping(config, "Cash")?.AccountId,
                        AllowsCashAccountOverride = AllowsOverride(config, "Cash"),
                        AllowsReceivableAccountOverride = AllowsOverride(config, "Receivable", "Customer"),
                    };
            }).ToArray();
            return (await Task.WhenAll(tasks).ConfigureAwait(true)).OfType<PatternLookupItem>().ToList();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
