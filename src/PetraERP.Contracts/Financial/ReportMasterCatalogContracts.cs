namespace PetraERP.Contracts.Financial;

public sealed record ReportCatalogEntryDto
{
    public string Id { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string NameEnglish { get; init; } = string.Empty;
    /// <summary>NotImplemented | Partial | Implemented.</summary>
    public string Delivery { get; init; } = string.Empty;
    public string? Route { get; init; }
    public string Permission { get; init; } = string.Empty;
    public string? Note { get; init; }
}

public sealed record ReportMasterCatalogResponse
{
    public List<ReportCatalogEntryDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Implemented { get; init; }
    public int Partial { get; init; }
    public int NotImplemented { get; init; }
}
