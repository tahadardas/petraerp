using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>Local presentation preferences under the existing preferences root. Contains no report rows or balances.</summary>
public sealed class ProfessionalReportPresetStore(string directory) : IProfessionalReportPresetStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static string Scope(ShellContext context)
    {
        if (context.UserId == Guid.Empty || context.FiscalYearId is not { } year || year == Guid.Empty)
            throw new InvalidOperationException("An authenticated fiscal workspace is required to save report preferences.");
        return $"{context.UserId:N}_{context.CompanyId:N}_{context.BranchId:N}_{year:N}";
    }

    private string FilePath(ShellContext context, ProfessionalReportKind kind) => Path.Combine(directory, "ProfessionalReporting", Scope(context), kind + ".json");

    public async Task<IReadOnlyList<ProfessionalReportPreset>> LoadAsync(ShellContext context, ProfessionalReportKind kind, CancellationToken cancellationToken)
    {
        var path = FilePath(context, kind);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("The report preferences file exceeds its size limit.");
        var stream = File.OpenRead(path);
        await using var configuredStream = stream.ConfigureAwait(false);
        var values = await JsonSerializer.DeserializeAsync<ProfessionalReportPreset[]>(stream, Json, cancellationToken).ConfigureAwait(false) ?? [];
        return values.Where(value => value.Parameters.Kind == kind && value.Parameters.FiscalYearId == context.FiscalYearId).Take(30).ToArray();
    }

    public async Task SaveAsync(ShellContext context, ProfessionalReportPreset preset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Parameters.FiscalYearId != context.FiscalYearId || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80)
            throw new ArgumentException("Preset name and fiscal workspace must be valid.", nameof(preset));
        var existing = await LoadAsync(context, preset.Parameters.Kind, cancellationToken).ConfigureAwait(false);
        var values = existing.Where(value => !string.Equals(value.Name, preset.Name, StringComparison.Ordinal)).Prepend(preset).Take(30).ToArray();
        var path = FilePath(context, preset.Parameters.Kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values, Json), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
