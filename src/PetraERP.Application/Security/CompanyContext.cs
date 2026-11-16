namespace PetraERP.Application.Security;

/// <summary>A company the user is explicitly scoped to, as displayed in the context selector.</summary>
public sealed record CompanyContext(Guid Id, string Code, string NameArabic, string? NameEnglish, string BaseCurrencyCode);

/// <summary>A branch the user is explicitly scoped to inside one company.</summary>
public sealed record BranchContext(Guid Id, Guid CompanyId, string Code, string NameArabic, string? NameEnglish);
