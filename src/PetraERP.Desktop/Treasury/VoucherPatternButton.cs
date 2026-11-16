using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Treasury;

/// <summary>A voucher list button: the pattern's name, and whether it starts a receipt or a payment.</summary>
public sealed record VoucherPatternButton(PatternLookupItem Pattern, VoucherKind Kind);
