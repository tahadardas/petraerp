using System.Globalization;
using System.Text.Json;

namespace PetraERP.Reporting.OpenSource.Printing;

/// <summary>
/// Print templates on disk, one JSON file per template under <c>{root}\{company}\{kind}\</c>, plus the company's default choice per
/// document kind. The built-in template of a kind is always listed first and can be used as is or saved under another name; it is never
/// written to disk and cannot be deleted. Templates hold layout only (no data), so they are safe to export, import and copy.
/// </summary>
public sealed class PrintTemplateStore(string root)
{
    private const string DefaultsFile = "defaults.json";
    private const int MaximumTemplatesPerKind = 50;

    /// <summary>The built-in template plus every saved template of the company for this kind (built-in first, then by name).</summary>
    public IReadOnlyList<PrintTemplate> List(Guid companyId, PrintDocumentKind kind)
    {
        var result = new List<PrintTemplate> { DefaultPrintTemplates.For(kind) };
        var directory = KindDirectory(companyId, kind);
        if (!Directory.Exists(directory)) return result;
        var saved = new List<PrintTemplate>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Take(MaximumTemplatesPerKind))
        {
            if (Path.GetFileName(path).Equals(DefaultsFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (Read(path) is { } template && template.Kind == kind) saved.Add(template);
        }

        result.AddRange(saved.OrderBy(template => template.Name, StringComparer.CurrentCulture));
        return result;
    }

    /// <summary>The template a company chose for a kind, or the built-in one when none is chosen or the chosen one no longer exists.</summary>
    public PrintTemplate Resolve(Guid companyId, PrintDocumentKind kind)
    {
        var templates = List(companyId, kind);
        var id = ReadDefaults(companyId, kind).GetValueOrDefault(kind.ToString());
        return templates.FirstOrDefault(template => template.Id == id) ?? templates[0];
    }

    public string? DefaultId(Guid companyId, PrintDocumentKind kind) => ReadDefaults(companyId, kind).GetValueOrDefault(kind.ToString());

    public void SetDefault(Guid companyId, PrintDocumentKind kind, string templateId)
    {
        var defaults = ReadDefaults(companyId, kind);
        defaults[kind.ToString()] = templateId;
        WriteAtomic(Path.Combine(KindDirectory(companyId, kind), DefaultsFile), JsonSerializer.Serialize(defaults));
    }

    /// <summary>Saves (creates or replaces) a template. The built-in id is refused: save it under a new name instead.</summary>
    public PrintTemplate Save(Guid companyId, PrintTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var valid = template.Validated();
        if (valid.Id == DefaultPrintTemplates.IdFor(valid.Kind)) throw new InvalidOperationException("The built-in template cannot be overwritten; save a copy under another name.");
        if (!Directory.Exists(KindDirectory(companyId, valid.Kind)) || Directory.EnumerateFiles(KindDirectory(companyId, valid.Kind), "*.json").Count() < MaximumTemplatesPerKind || File.Exists(PathOf(companyId, valid)))
            WriteAtomic(PathOf(companyId, valid), PrintTemplateJson.Serialize(valid));
        else throw new InvalidOperationException("The company has reached the maximum number of print templates for this document.");
        return valid;
    }

    /// <summary>A copy with a new identity (used by "save as"); the original is untouched.</summary>
    public static PrintTemplate Copy(PrintTemplate template, string name) => template with { Id = Guid.NewGuid().ToString("N"), Name = name };

    public void Delete(Guid companyId, PrintDocumentKind kind, string templateId)
    {
        if (templateId == DefaultPrintTemplates.IdFor(kind)) return;
        var path = Path.Combine(KindDirectory(companyId, kind), SafeFile(templateId));
        if (File.Exists(path)) File.Delete(path);
        var defaults = ReadDefaults(companyId, kind);
        if (defaults.GetValueOrDefault(kind.ToString()) == templateId)
        {
            defaults.Remove(kind.ToString());
            WriteAtomic(Path.Combine(KindDirectory(companyId, kind), DefaultsFile), JsonSerializer.Serialize(defaults));
        }
    }

    /// <summary>Writes a template to a file the user chose (export).</summary>
    public static void Export(PrintTemplate template, string path)
    {
        ArgumentNullException.ThrowIfNull(template);
        WriteAtomic(path, PrintTemplateJson.Serialize(template));
    }

    /// <summary>Reads a template file the user chose (import); it gets a new identity so it can never replace an existing template.</summary>
    public static PrintTemplate Import(string path, PrintDocumentKind expectedKind)
    {
        if (new FileInfo(path).Length > PrintTemplateJson.MaximumFileBytes) throw new InvalidDataException("The print template file is too large.");
        var template = PrintTemplateJson.Deserialize(File.ReadAllText(path));
        if (template.Kind != expectedKind) throw new InvalidDataException("The file is a template for another document kind.");
        return template with { Id = Guid.NewGuid().ToString("N") };
    }

    private static PrintTemplate? Read(string path)
    {
        try
        {
            return new FileInfo(path).Length > PrintTemplateJson.MaximumFileBytes ? null : PrintTemplateJson.Deserialize(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A damaged or unreadable template is skipped; it must not stop printing.
            return null;
        }
    }

    private Dictionary<string, string> ReadDefaults(Guid companyId, PrintDocumentKind kind)
    {
        var path = Path.Combine(KindDirectory(companyId, kind), DefaultsFile);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length < 64 * 1024
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string KindDirectory(Guid companyId, PrintDocumentKind kind)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        return Path.Combine(root, companyId.ToString("N", CultureInfo.InvariantCulture), kind.ToString());
    }

    private string PathOf(Guid companyId, PrintTemplate template) => Path.Combine(KindDirectory(companyId, template.Kind), SafeFile(template.Id));

    private static string SafeFile(string id) => id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '_') ? throw new ArgumentException("Invalid template id.", nameof(id)) : id + ".json";

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
