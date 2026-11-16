using System.IO;
using System.Text.Json;

namespace PetraERP.Desktop.Reporting;

public sealed record PetraReportBranding
{
    public int Version { get; init; } = 1;
    public required Guid CompanyId { get; init; }
    public string CompanyNameArabic { get; init; } = string.Empty;
    public string CompanyNameEnglish { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string TaxNumber { get; init; } = string.Empty;
    public string CommercialRegistration { get; init; } = string.Empty;
    public string? LogoBase64 { get; init; }
    public string HeaderSubtitle { get; init; } = string.Empty;
    public string FooterText { get; init; } = string.Empty;
    public string PreparedByTitle { get; init; } = "المحاسب / Prepared by";
    public string ReviewedByTitle { get; init; } = "المدقق / Audited by";
    public string ApprovedByTitle { get; init; } = "المدير المالي / Approved by";
    public bool ShowSignatures { get; init; } = true;
    public bool ShowLogo { get; init; } = true;
    public bool ShowTaxNumber { get; init; } = true;
    public bool ShowHeaderSubtitle { get; init; } = true;
    public double MarginTop { get; init; } = 28;
    public double MarginBottom { get; init; } = 28;
    public double MarginLeft { get; init; } = 28;
    public double MarginRight { get; init; } = 28;
    public string HeaderTemplate { get; init; } = "Standard";
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PetraReportBrandingStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string rootDirectory;

    public PetraReportBrandingStore(string? baseDirectory = null)
    {
        rootDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PetraERP", "branding");
    }

    public async Task<PetraReportBranding> LoadAsync(Guid companyId, string companyName, CancellationToken cancellationToken = default)
    {
        var path = FilePath(companyId);
        if (!File.Exists(path))
        {
            return CreateDefault(companyId, companyName);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<PetraReportBranding>(stream, Json, cancellationToken).ConfigureAwait(false);
            return loaded ?? CreateDefault(companyId, companyName);
        }
        catch (Exception)
        {
            return CreateDefault(companyId, companyName);
        }
    }

    public async Task SaveAsync(PetraReportBranding branding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branding);
        Directory.CreateDirectory(rootDirectory);
        var path = FilePath(branding.CompanyId);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, branding with { UpdatedAtUtc = DateTimeOffset.UtcNow }, Json, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temp, path, true);
    }

    public static PetraReportBranding CreateDefault(Guid companyId, string companyName) => new()
    {
        CompanyId = companyId,
        CompanyNameArabic = companyName,
        CompanyNameEnglish = companyName,
        HeaderSubtitle = "نظام بترا للحلول المحاسبية والإدارية / Petra ERP",
        FooterText = "تم إنشاء التقرير آلياً من نظام بترا المحاسبي · سري للاستخدام الداخلي",
        PreparedByTitle = "المحاسب المسؤول",
        ReviewedByTitle = "المدقق الداخلي",
        ApprovedByTitle = "المدير المالي والإداري",
        ShowSignatures = true,
        ShowLogo = false,
        ShowTaxNumber = true,
        ShowHeaderSubtitle = true,
        MarginTop = 28,
        MarginBottom = 28,
        MarginLeft = 28,
        MarginRight = 28,
    };

    private string FilePath(Guid companyId) => Path.Combine(rootDirectory, $"company_{companyId:N}.json");
}
