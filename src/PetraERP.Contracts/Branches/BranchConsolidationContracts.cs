namespace PetraERP.Contracts.Branches;

public sealed record BranchBalanceColumnDto
{
    public Guid BranchId { get; init; }
    public string Opening { get; init; } = "0.0000";
    public string Debit { get; init; } = "0.0000";
    public string Credit { get; init; } = "0.0000";
    public string Closing { get; init; } = "0.0000";
}

public sealed record BranchTrialBalanceRowDto
{
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public bool IsInterBranchClearing { get; init; }
    public List<BranchBalanceColumnDto> ByBranch { get; init; } = [];
    public string TotalOpening { get; init; } = "0.0000";
    public string TotalDebit { get; init; } = "0.0000";
    public string TotalCredit { get; init; } = "0.0000";
    public string TotalClosing { get; init; } = "0.0000";
    public string ConsolidatedClosing { get; init; } = "0.0000";
}

public sealed record BranchTrialBalanceBranchDto
{
    public Guid BranchId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>Amounts are decimal strings with four decimals (base currency).</summary>
public sealed record BranchTrialBalanceResponse
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public bool Eliminated { get; init; }
    public List<BranchTrialBalanceBranchDto> Branches { get; init; } = [];
    public List<BranchTrialBalanceRowDto> Rows { get; init; } = [];
    public string TotalDebit { get; init; } = "0.0000";
    public string TotalCredit { get; init; } = "0.0000";
    public string ConsolidatedClosingSum { get; init; } = "0.0000";
    /// <summary>Net of the inter-branch clearing accounts; non-zero means an unreconciled inter-branch transfer.</summary>
    public string InterBranchResidual { get; init; } = "0.0000";
}
