using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Usage:
//   AlAmeenCsvLoader --export <DB_2026 dir> --server .\SQLEXPRESS --database AlAmeen_Dardas_2026 [--tables core|all|a,b,c] [--recreate]
// The export is only read. The target database is created if missing; tables are created from the CSV header with types
// inferred from ALL rows (uniqueidentifier, int, bigint, decimal(38,10), datetime2, nvarchar). Leading-zero numbers stay text.

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) options[args[i]] = args[++i];
    else flags.Add(args[i]);
}
string Opt(string name, string? fallback = null) => options.TryGetValue(name, out var v) ? v : fallback ?? throw new ArgumentException("Missing " + name);

var export = Opt("--export");
var server = Opt("--server", @".\SQLEXPRESS");
var database = Opt("--database");
var tablesOption = Opt("--tables", "core");
string[] core = ["ac000", "cu000", "mt000", "st000", "my000", "ce000", "en000", "bt000", "bu000", "bi000", "ms000", "co000", "gr000", "er000", "ma000", "mn000", "bg000"];
var dataDir = Path.Combine(export, "DATA");
string[] tables = tablesOption.Equals("all", StringComparison.OrdinalIgnoreCase)
    ? Directory.GetFiles(dataDir, "dbo__*.csv.gz").Select(f => Path.GetFileName(f)["dbo__".Length..^".csv.gz".Length]).Order().ToArray()
    : tablesOption.Equals("core", StringComparison.OrdinalIgnoreCase) ? core.Where(t => File.Exists(Path.Combine(dataDir, $"dbo__{t}.csv.gz"))).ToArray()
    : tablesOption.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var master = $"Server={server};Database=master;Integrated Security=True;TrustServerCertificate=True;Encrypt=True";
var target = $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;Encrypt=True;Connect Timeout=60";
await using (var m = new SqlConnection(master))
{
    await m.OpenAsync();
    if (flags.Contains("--recreate"))
        await Exec(m, $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
    await Exec(m, $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}] COLLATE Arabic_CI_AI");
    await Exec(m, $"ALTER DATABASE [{database}] SET ALLOW_SNAPSHOT_ISOLATION ON; ALTER DATABASE [{database}] SET RECOVERY SIMPLE");
}

await using var conn = new SqlConnection(target);
await conn.OpenAsync();
foreach (var table in tables)
{
    var path = Path.Combine(dataDir, $"dbo__{table}.csv.gz");
    if (!File.Exists(path)) { Console.WriteLine($"{table}: file missing, skipped"); continue; }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var (header, kinds, lengths, rowCount) = Infer(path);
    await Exec(conn, $"IF OBJECT_ID(N'dbo.[{table}]') IS NOT NULL DROP TABLE dbo.[{table}]");
    var ddl = new StringBuilder($"CREATE TABLE dbo.[{table}] (");
    for (var c = 0; c < header.Count; c++)
    {
        if (c > 0) ddl.Append(", ");
        ddl.Append('[').Append(header[c]).Append("] ").Append(kinds[c] switch
        {
            Kind.Guid => "uniqueidentifier",
            Kind.Int => "int",
            Kind.BigInt => "bigint",
            Kind.Decimal => "decimal(38,10)",
            Kind.Date => "datetime2(7)",
            _ => lengths[c] <= 4000 ? $"nvarchar({Math.Max(1, lengths[c])})" : "nvarchar(max)",
        }).Append(" NULL");
    }
    ddl.Append(')');
    await Exec(conn, ddl.ToString());

    var data = new DataTable();
    for (var c = 0; c < header.Count; c++) data.Columns.Add(header[c], kinds[c] switch
    {
        Kind.Guid => typeof(Guid), Kind.Int => typeof(int), Kind.BigInt => typeof(long), Kind.Decimal => typeof(decimal), Kind.Date => typeof(DateTime), _ => typeof(string),
    });
    using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, null) { DestinationTableName = $"dbo.[{table}]", BulkCopyTimeout = 600, BatchSize = 20000 };
    for (var c = 0; c < header.Count; c++) bulk.ColumnMappings.Add(c, c);
    var loaded = 0L;
    foreach (var fields in Records(path).Skip(1))
    {
        var row = data.NewRow();
        for (var c = 0; c < header.Count; c++)
        {
            var raw = fields[c];
            if (raw is null) { row[c] = DBNull.Value; continue; }
            row[c] = kinds[c] switch
            {
                Kind.Guid => Guid.Parse(raw), Kind.Int => int.Parse(raw, CultureInfo.InvariantCulture), Kind.BigInt => long.Parse(raw, CultureInfo.InvariantCulture),
                Kind.Decimal => decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture),
                Kind.Date => DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), _ => raw,
            };
        }
        data.Rows.Add(row);
        if (data.Rows.Count >= 20000) { await bulk.WriteToServerAsync(data); loaded += data.Rows.Count; data.Clear(); }
    }
    if (data.Rows.Count > 0) { await bulk.WriteToServerAsync(data); loaded += data.Rows.Count; }
    Console.WriteLine($"{table}: {loaded}/{rowCount} rows, {header.Count} cols, {sw.Elapsed.TotalSeconds:0.0}s");
    if (loaded != rowCount) throw new InvalidOperationException($"{table}: loaded {loaded} but file has {rowCount} rows.");
}
Console.WriteLine("DONE");
return 0;

static async Task Exec(SqlConnection c, string sql)
{
    await using var cmd = c.CreateCommand();
    cmd.CommandText = sql; cmd.CommandTimeout = 300;
    await cmd.ExecuteNonQueryAsync();
}

// Two-pass inference: every value of every column decides the type. A null (unquoted empty) field is returned as null.
static (List<string> Header, Kind[] Kinds, int[] Lengths, long Rows) Infer(string path)
{
    List<string>? header = null; Kind[] kinds = []; int[] lengths = []; long rows = 0;
    bool[] sawValue = [];
    foreach (var fields in Records(path))
    {
        if (header is null)
        {
            header = fields.Select(f => f ?? string.Empty).ToList();
            kinds = new Kind[header.Count]; lengths = new int[header.Count]; sawValue = new bool[header.Count];
            continue;
        }
        rows++;
        for (var c = 0; c < header.Count; c++)
        {
            var v = fields[c];
            if (v is null) continue;
            lengths[c] = Math.Max(lengths[c], v.Length);
            var k = Classify(v);
            kinds[c] = sawValue[c] ? Merge(kinds[c], k) : k;
            sawValue[c] = true;
        }
    }
    for (var c = 0; c < kinds.Length; c++) if (!sawValue[c]) { kinds[c] = Kind.Text; lengths[c] = 1; }
    return (header!, kinds, lengths, rows);
}

static Kind Classify(string v)
{
    if (v.Length == 0) return Kind.Text;
    if (Regex.IsMatch(v, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")) return Kind.Guid;
    if (Regex.IsMatch(v, @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?$")) return Kind.Date;
    if (Regex.IsMatch(v, @"^-?(0|[1-9]\d*)$")) return v.Length <= 9 ? Kind.Int : v.Length <= 18 ? Kind.BigInt : Kind.Text;
    if (Regex.IsMatch(v, @"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$") && (v.Contains('.') || v.Contains('e') || v.Contains('E')) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return Kind.Decimal;
    return Kind.Text;
}

static Kind Merge(Kind a, Kind b)
{
    if (a == b) return a;
    if (a == Kind.Text || b == Kind.Text) return Kind.Text;
    var numeric = new[] { Kind.Int, Kind.BigInt, Kind.Decimal };
    if (numeric.Contains(a) && numeric.Contains(b)) return (Kind)Math.Max((int)a, (int)b);
    return Kind.Text;
}

// RFC 4180 reader for the export contract: quoted = value, unquoted empty = NULL, "" = empty string; multiline fields allowed.
static IEnumerable<List<string?>> Records(string path)
{
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var gz = new GZipStream(file, CompressionMode.Decompress);
    using var reader = new StreamReader(gz, new UTF8Encoding(false, true), true, 1 << 16);
    var fields = new List<string?>(); var sb = new StringBuilder(); bool quoted = false, wasQuoted = false, any = false; int next;
    while ((next = reader.Read()) >= 0)
    {
        var ch = (char)next; any = true;
        if (quoted)
        {
            if (ch != '"') { sb.Append(ch); continue; }
            if (reader.Peek() == '"') { reader.Read(); sb.Append('"'); } else quoted = false;
            continue;
        }
        switch (ch)
        {
            case '"': quoted = true; wasQuoted = true; break;
            case ',': fields.Add(wasQuoted || sb.Length > 0 ? sb.ToString() : null); sb.Clear(); wasQuoted = false; break;
            case '\r': break;
            case '\n':
                fields.Add(wasQuoted || sb.Length > 0 ? sb.ToString() : null); sb.Clear(); wasQuoted = false;
                yield return fields; fields = []; any = false; break;
            default: sb.Append(ch); break;
        }
    }
    if (quoted) throw new InvalidDataException("Unterminated quoted field in " + path);
    if (any) { fields.Add(wasQuoted || sb.Length > 0 ? sb.ToString() : null); yield return fields; }
}

internal enum Kind { Guid = 0, Date = 1, Int = 2, BigInt = 3, Decimal = 4, Text = 5 }
