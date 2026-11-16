using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetraERP.Desktop.Reporting;

internal sealed record PetraReportWorkspaceFilter(
    string ColumnKey,
    PetraReportFilterOperator Operator,
    string? Value,
    string? SecondValue,
    PetraReportFilterLogic Logic,
    bool IsAuto);

internal sealed record PetraReportWorkspace(
    int Version,
    string Name,
    Guid UserId,
    Guid CompanyId,
    Guid FiscalYearId,
    string ReportCode,
    IReadOnlyList<PetraReportColumnState> Columns,
    IReadOnlyList<string> Grouping,
    IReadOnlyList<PetraReportWorkspaceFilter> Filters,
    string? SortKey,
    ListSortDirection? SortDirection,
    string Density,
    string? PrintTemplateReference,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<PetraReportCustomSummary>? CustomSummaries = null);

/// <summary>Versioned local presentation state. It stores no report rows, balances or authorization data.</summary>
internal sealed class PetraReportWorkspaceStore(string directory)
{
    internal const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal async Task<IReadOnlyList<PetraReportWorkspace>> LoadAsync(
        Guid userId,
        Guid companyId,
        Guid fiscalYearId,
        string reportCode,
        CancellationToken cancellationToken)
    {
        ValidateScope(userId, companyId, fiscalYearId, reportCode);
        var path = FilePath(userId, companyId, fiscalYearId, reportCode);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("The report workspace file exceeds its size limit.");
        var stream = File.OpenRead(path);
        await using var configuredStream = stream.ConfigureAwait(false);
        var values = await JsonSerializer.DeserializeAsync<PetraReportWorkspace[]>(stream, Json, cancellationToken).ConfigureAwait(false) ?? [];
        return values.Where(value => value.Version == CurrentVersion && value.UserId == userId && value.CompanyId == companyId &&
                value.FiscalYearId == fiscalYearId && string.Equals(value.ReportCode, reportCode, StringComparison.Ordinal) && ValidName(value.Name))
            .Take(30).ToArray();
    }

    internal async Task SaveAsync(PetraReportWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateScope(workspace.UserId, workspace.CompanyId, workspace.FiscalYearId, workspace.ReportCode);
        if (workspace.Version != CurrentVersion || !ValidName(workspace.Name) || workspace.Columns.Count > 32 ||
            workspace.Grouping.Count > 4 || workspace.Filters.Count > PetraReportFilterEngine.MaximumConditions)
            throw new ArgumentException("The report workspace is invalid or exceeds its bounded layout limits.", nameof(workspace));
        var existing = await LoadAsync(workspace.UserId, workspace.CompanyId, workspace.FiscalYearId, workspace.ReportCode, cancellationToken).ConfigureAwait(false);
        var values = existing.Where(value => !string.Equals(value.Name, workspace.Name, StringComparison.OrdinalIgnoreCase))
            .Prepend(workspace with { UpdatedAtUtc = DateTimeOffset.UtcNow }).Take(30).ToArray();
        await WriteAsync(workspace.UserId, workspace.CompanyId, workspace.FiscalYearId, workspace.ReportCode, values, cancellationToken).ConfigureAwait(false);
    }

    internal async Task DeleteAsync(PetraReportWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var existing = await LoadAsync(workspace.UserId, workspace.CompanyId, workspace.FiscalYearId, workspace.ReportCode, cancellationToken).ConfigureAwait(false);
        var values = existing.Where(value => !string.Equals(value.Name, workspace.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        await WriteAsync(workspace.UserId, workspace.CompanyId, workspace.FiscalYearId, workspace.ReportCode, values, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(Guid userId, Guid companyId, Guid fiscalYearId, string reportCode,
        PetraReportWorkspace[] values, CancellationToken cancellationToken)
    {
        var path = FilePath(userId, companyId, fiscalYearId, reportCode);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values, Json), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string FilePath(Guid userId, Guid companyId, Guid fiscalYearId, string reportCode) =>
        Path.Combine(directory, userId.ToString("N"), companyId.ToString("N"), fiscalYearId.ToString("N"), reportCode + ".json");

    private static void ValidateScope(Guid userId, Guid companyId, Guid fiscalYearId, string reportCode)
    {
        if (userId == Guid.Empty || companyId == Guid.Empty || fiscalYearId == Guid.Empty || string.IsNullOrWhiteSpace(reportCode) ||
            reportCode.Length > 64 || reportCode.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("An authenticated company and fiscal workspace is required for report layouts.");
    }

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 80;
}
