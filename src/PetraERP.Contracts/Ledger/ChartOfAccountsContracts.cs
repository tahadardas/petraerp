using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Ledger;

public enum AccountType { Assets = 1, Liabilities = 2, Equity = 3, Revenue = 4, Expenses = 5 }

public enum CostCenterPolicy { NotAllowed = 1, Optional = 2, Required = 3 }

public enum AccountCurrencyMode { BaseCurrencyOnly = 1, AnyEnabledCurrency = 2, FixedCurrency = 3 }

public sealed record AccountRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string NameArabic { get; init; }

    [StringLength(200)]
    public string? NameEnglish { get; init; }

    [Required]
    public required AccountType AccountType { get; init; }

    [Required]
    public required bool PostingAllowed { get; init; }

    [Required]
    public required CostCenterPolicy CostCenterPolicy { get; init; }

    [Required]
    public required AccountCurrencyMode CurrencyMode { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? FixedCurrencyCode { get; init; }

    public Guid? ParentId { get; init; }
}

public sealed record SetActiveRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    public required bool IsActive { get; init; }
}

public enum AccountNavigationDirection { First = 1, Previous = 2, Next = 3, Last = 4 }

/// <summary>Result of first/previous/next/last navigation; <see cref="Account"/> is null past either end.</summary>
public sealed record AccountNavigationResponse
{
    public AccountResponse? Account { get; init; }
}

/// <summary>Deletes an unused leaf account. The server rejects accounts with children or journal lines (deactivate them instead).</summary>
public sealed record DeleteAccountRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

/// <summary>Append-only audit snapshot of a deleted account.</summary>
public sealed record AccountAuditResponse
{
    public required Guid EventId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountType AccountType { get; init; }
    public Guid? ParentId { get; init; }
    public required Guid ActorUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record AccountResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountType AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required CostCenterPolicy CostCenterPolicy { get; init; }
    public required AccountCurrencyMode CurrencyMode { get; init; }
    public string? FixedCurrencyCode { get; init; }
    public Guid? ParentId { get; init; }
    public required int Level { get; init; }
    public required bool HasChildren { get; init; }
    public required bool HasJournalLines { get; init; }
    public required bool IsActive { get; init; }
    public required string Version { get; init; }
}

public sealed record AccountPageResponse
{
    public required IReadOnlyList<AccountResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}

public sealed record CostCenterRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string NameArabic { get; init; }

    [StringLength(200)]
    public string? NameEnglish { get; init; }

    public Guid? ParentId { get; init; }
    public byte CenterType { get; init; } = 1; // 1 = Ordinary, 2 = Parent, 3 = Aggregation, 4 = Distribution

    [StringLength(1000)]
    public string? Description { get; init; }

    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }

    [StringLength(200)]
    public string? ResponsiblePerson { get; init; }
}

public sealed record CostCenterResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public Guid? ParentId { get; init; }
    public byte CenterType { get; init; } = 1;
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public string? Description { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? ResponsiblePerson { get; init; }
    public required bool IsActive { get; init; }
    public required bool HasJournalLines { get; init; }
    public required string Version { get; init; }
    public int ChildrenCount { get; init; }
}

public sealed record CostCenterAllocationItemDto
{
    public Guid TargetCostCenterId { get; init; }
    [Required]
    public required string Percentage { get; init; }
}

public sealed record SetCostCenterAllocationsRequest
{
    public required IReadOnlyList<CostCenterAllocationItemDto> Allocations { get; init; }
}

public sealed record CostCenterAllocationResponse
{
    public Guid Id { get; init; }
    public Guid SourceCostCenterId { get; init; }
    public Guid TargetCostCenterId { get; init; }
    public string TargetCode { get; init; } = string.Empty;
    public string TargetNameArabic { get; init; } = string.Empty;
    public required string Percentage { get; init; }
}

public sealed record CostCenterPageResponse
{
    public required IReadOnlyList<CostCenterResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}
