using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Branches;

public sealed record BranchCardDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public bool IsActive { get; init; }
    public Guid? ParentBranchId { get; init; }
    public string? NumberPrefix { get; init; }
    public DateOnly? OpenedOn { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Notes { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public Guid? DefaultCashAccountId { get; init; }
    public Guid? DefaultBankAccountId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
    /// <summary>Opaque concurrency token to send back on update.</summary>
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record BranchTreeNodeDto
{
    public BranchCardDto Branch { get; init; } = new();
    public int Depth { get; init; }
    public List<BranchTreeNodeDto> Children { get; init; } = [];
}

public sealed record BranchListResponse
{
    public List<BranchCardDto> Items { get; init; } = [];
}

public sealed record BranchTreeResponse
{
    public List<BranchTreeNodeDto> Roots { get; init; } = [];
}

public record SaveBranchRequest
{
    [Required, MaxLength(64)] public string Code { get; init; } = string.Empty;
    [Required, MaxLength(200)] public string NameArabic { get; init; } = string.Empty;
    [MaxLength(200)] public string? NameEnglish { get; init; }
    public Guid? ParentBranchId { get; init; }
    [MaxLength(8)] public string? NumberPrefix { get; init; }
    public DateOnly? OpenedOn { get; init; }
    [MaxLength(400)] public string? Address { get; init; }
    [MaxLength(50)] public string? Phone { get; init; }
    [MaxLength(200), EmailAddress] public string? Email { get; init; }
    [MaxLength(1000)] public string? Notes { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public Guid? DefaultCashAccountId { get; init; }
    public Guid? DefaultBankAccountId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
}

public sealed record UpdateBranchRequest : SaveBranchRequest
{
    [Required, StringLength(12, MinimumLength = 12)] public string Version { get; init; } = string.Empty;
    [Required, MaxLength(400)] public string Reason { get; init; } = string.Empty;
}

public sealed record SetBranchActiveRequest
{
    public bool IsActive { get; init; }
    [Required, StringLength(12, MinimumLength = 12)] public string Version { get; init; } = string.Empty;
    [Required, MaxLength(400)] public string Reason { get; init; } = string.Empty;
}

public sealed record BranchAccountLinkDto
{
    public Guid Id { get; init; }
    public Guid BranchId { get; init; }
    /// <summary>Cash, Bank, Sales, SalesReturns, Purchases, PurchaseReturns, Inventory, CostOfGoodsSold, Receivable, Payable or InterBranchClearing.</summary>
    public string Role { get; init; } = string.Empty;
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public string AccountNameArabic { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
}

public sealed record BranchAccountLinkListResponse
{
    public List<BranchAccountLinkDto> Items { get; init; } = [];
}

public sealed record SetBranchAccountLinkRequest
{
    [Required, MaxLength(32)] public string Role { get; init; } = string.Empty;
    public Guid AccountId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    [Required, MaxLength(400)] public string Reason { get; init; } = string.Empty;
}

public sealed record BranchSubAccountPlanDto
{
    public string Role { get; init; } = string.Empty;
    public Guid ParentAccountId { get; init; }
    public string ParentCode { get; init; } = string.Empty;
    public string SuggestedCode { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
}

public sealed record BranchSubAccountPreviewResponse
{
    public List<BranchSubAccountPlanDto> Items { get; init; } = [];
}

public sealed record ApplyBranchSubAccountsRequest
{
    public Guid ModelBranchId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record BranchSubAccountApplyItemDto
{
    public string Role { get; init; } = string.Empty;
    public Guid AccountId { get; init; }
    public string AccountCode { get; init; } = string.Empty;
    public bool AccountCreated { get; init; }
    public bool LinkChanged { get; init; }
}

public sealed record BranchSubAccountApplyResponse
{
    public List<BranchSubAccountApplyItemDto> Items { get; init; } = [];
}
