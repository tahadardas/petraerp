using PetraERP.ApiClient;
using PetraERP.Desktop.Currencies;

namespace PetraERP.Desktop.Tests;

/// <summary>In-memory FX revaluation port for page and render tests; the real behavior is proven against SQL.</summary>
internal sealed class FxRevaluationFixture : IFxRevaluationClient
{
    internal List<FxRateAuditRequest> AuditRequests { get; } = [];

    internal FxRateAuditResponse AuditResult { get; set; } = new()
    {
        From = new(2026, 1, 1), To = new(2026, 12, 31), BaseCurrencyCode = "JOD", LinesChecked = 3, LinesWithoutBulletinRate = 1, TotalDifference = "22.5000",
        Items =
        [
            new() { JournalId = Guid.NewGuid(), PostingSequence = 12, AccountingDate = new(2026, 10, 5), AccountId = Guid.NewGuid(), AccountCode = "1101", AccountNameArabic = "الصندوق بالدولار",
                Description = "تحصيل نقدي", CurrencyCode = "USD", ForeignAmount = "100.0000", BookedRate = "0.7100000000", BulletinRate = "0.7250000000", BookedBase = "71.0000", BulletinBase = "72.5000", Difference = "1.5000" },
            new() { JournalId = Guid.NewGuid(), PostingSequence = 13, AccountingDate = new(2026, 10, 9), AccountId = Guid.NewGuid(), AccountCode = "1201", AccountNameArabic = "ذمم العملاء",
                Description = "فاتورة مبيعات", CurrencyCode = "USD", ForeignAmount = "1000.0000", BookedRate = "0.7100000000", BulletinRate = "0.7310000000", BookedBase = "710.0000", BulletinBase = "731.0000", Difference = "21.0000" },
        ],
    };

    public Task<FxRevaluationPreviewResponse> PreviewFxRevaluationAsync(Guid companyId, Guid branchId, FxRevaluationPreviewRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FxRevaluationPreviewResponse { AsOfDate = request.AsOfDate, BaseCurrencyCode = "JOD", Items = [], TotalUnrealizedGains = "0.0000", TotalUnrealizedLosses = "0.0000", NetDifference = "0.0000" });

    public Task<FxRateAuditResponse> AuditFxRatesAsync(Guid companyId, Guid branchId, FxRateAuditRequest request, CancellationToken cancellationToken = default)
    {
        AuditRequests.Add(request);
        return Task.FromResult(AuditResult);
    }

    public Task<FxRevaluationExecutionResponse> ExecuteFxRevaluationAsync(Guid companyId, Guid branchId, ExecuteFxRevaluationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Revaluation posting is exercised against real SQL.");
}
