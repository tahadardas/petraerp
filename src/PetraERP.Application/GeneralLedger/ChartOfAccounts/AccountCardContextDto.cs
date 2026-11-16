using PetraERP.Application.Common;

namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

/// <summary>Company-scoped hierarchy context; path includes the current account as its last item.</summary>
public sealed record AccountCardContextDto(IReadOnlyList<AccountDto> Path, PagedResult<AccountDto> Children);
