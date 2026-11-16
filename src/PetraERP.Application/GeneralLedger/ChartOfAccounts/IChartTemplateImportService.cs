using PetraERP.Domain.GeneralLedger.Templates;

namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

public sealed record TemplateImportPreview(ChartOfAccountsTemplate Template, IReadOnlyList<string> Errors)
{
    public bool CanImport => Errors.Count == 0;
}
public sealed record TemplateImportResult(int AccountsImported, bool AlreadyImported, string ContentHash);
public interface IChartTemplateImportService
{
    Task<TemplateImportPreview> PreviewAsync(LedgerMasterScope scope, ChartOfAccountsTemplate chartTemplate, CancellationToken cancellationToken);
    Task<TemplateImportResult> ImportAsync(LedgerMasterScope scope, ChartOfAccountsTemplate chartTemplate, CancellationToken cancellationToken);
    Task<TemplateImportPreview> ReadFileAsync(LedgerMasterScope scope, string fileName, Stream stream, ChartOfAccountsTemplate metadata, CancellationToken cancellationToken);
}
