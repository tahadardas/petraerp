using System.Text;

namespace PetraERP.TestDataImport;

/// <summary>Append-only legacy-key → PetraERP-id log, so an interrupted import resumes without duplicates.</summary>
internal sealed class ImportMap : IDisposable
{
    private readonly Dictionary<string, Guid> entries = new(StringComparer.Ordinal);
    private readonly StreamWriter writer;

    public ImportMap(string path)
    {
        if (File.Exists(path))
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && Guid.TryParse(parts[1], out var id)) entries[parts[0]] = id;
            }
        writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public bool TryGet(string kind, string legacyKey, out Guid id) => entries.TryGetValue(kind + ":" + legacyKey, out id);
    public Guid Get(string kind, string legacyKey) => entries.TryGetValue(kind + ":" + legacyKey, out var id) ? id
        : throw new KeyNotFoundException($"{kind} {legacyKey} was not imported.");

    public void Set(string kind, string legacyKey, Guid id)
    {
        entries[kind + ":" + legacyKey] = id;
        writer.WriteLine(kind + ":" + legacyKey + "\t" + id.ToString("D"));
    }

    public void Dispose() => writer.Dispose();
}
