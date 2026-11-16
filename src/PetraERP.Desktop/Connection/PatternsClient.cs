using PetraERP.ApiClient;
using PetraERP.Desktop.Patterns;

namespace PetraERP.Desktop.Connection;

public sealed class PatternsClient(DesktopSession session) : IPatternsClient
{
    public Task<DocumentPatternPageResponse> ListPatternsAsync(Guid companyId, Guid branchId, int page, int pageSize, BusinessOperationKind? kind, bool? activeOnly, CancellationToken cancellationToken) =>
        session.Api.ListDocumentPatternsAsync(companyId, branchId, page, pageSize, kind, activeOnly, cancellationToken);

    // A document window opens by listing the runnable patterns and then reading EACH one for its active revision (N+1 requests, repeated
    // by every sales, purchase and voucher window). The answers are short-lived reference data: cached per workspace and branch for a
    // minute and retired at once by any pattern change made through this client. A stale detail can only cause a version conflict on
    // save (the server checks ExpectedVersion), never a silent overwrite.
    private static readonly TimeSpan PatternTtl = TimeSpan.FromMinutes(1);
    private const string PatternCategory = "Patterns";

    public Task<DocumentPatternResponse> GetPatternAsync(Guid companyId, Guid branchId, Guid patternId, CancellationToken cancellationToken) =>
        session.CachedAsync(branchId, PatternCategory, $"detail:{companyId:N}:{patternId:N}",
            ct => session.Api.GetDocumentPatternAsync(companyId, branchId, patternId, ct), PatternTtl, cancellationToken);

    private async Task<T> ChangedAsync<T>(Task<T> change)
    {
        var result = await change.ConfigureAwait(false);
        session.InvalidateCached(PatternCategory);
        return result;
    }

    public Task<DocumentPatternResponse> CreatePatternAsync(Guid companyId, Guid branchId, CreateDocumentPatternRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(session.Api.CreateDocumentPatternAsync(companyId, branchId, request, cancellationToken));

    public Task<DocumentPatternResponse> UpdatePatternAsync(Guid companyId, Guid branchId, Guid patternId, string expectedVersion, UpdateDocumentPatternRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(session.Api.UpdateDocumentPatternAsync(companyId, branchId, patternId, request, cancellationToken));

    public Task<PatternRevisionResponse> CreateRevisionAsync(Guid companyId, Guid branchId, Guid patternId, CreatePatternRevisionRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(session.Api.CreatePatternRevisionAsync(companyId, branchId, patternId, request, cancellationToken));

    public Task<DocumentPatternResponse> PublishRevisionAsync(Guid companyId, Guid branchId, Guid patternId, PublishPatternRevisionRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(session.Api.PublishPatternRevisionAsync(companyId, branchId, patternId, request, cancellationToken));

    public Task<DocumentPatternResponse> RetirePatternAsync(Guid companyId, Guid branchId, Guid patternId, RetirePatternRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(session.Api.RetireDocumentPatternAsync(companyId, branchId, patternId, request, cancellationToken));

    public Task<IReadOnlyList<DocumentPatternSummaryResponse>> GetRunnablePatternsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.CachedAsync(branchId, PatternCategory, $"runnable:{companyId:N}",
            ct => session.Api.GetRunnableDocumentPatternsAsync(companyId, branchId, ct), PatternTtl, cancellationToken);

    public Task<NextNumberResponse> GenerateNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId, CancellationToken cancellationToken) =>
        session.Api.GenerateNextPatternNumberAsync(companyId, branchId, patternId, fiscalYearId, cancellationToken: cancellationToken);

    public Task<NextNumberPreviewResponse> PreviewNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId,
        DateOnly? accountingDate, Guid? revisionId, CancellationToken cancellationToken) =>
        session.Api.PreviewNextPatternNumberAsync(companyId, branchId, patternId, fiscalYearId, accountingDate, revisionId, cancellationToken);
}
