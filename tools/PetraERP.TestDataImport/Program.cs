using System.Globalization;
using PetraERP.TestDataImport;

// Usage: PetraERP.TestDataImport --legacy <DB_2026 dir> --api <https uri> --user <name> --company <code> --state <dir>  (password on stdin)
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index + 1 < args.Length; index += 2) options[args[index]] = args[index + 1];
string Option(string name) => options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing option " + name);
// Some PowerShell hosts prepend a UTF-8 BOM to piped text; it is never part of the password.
var password = (Console.In.ReadLine() ?? throw new ArgumentException("Password expected on standard input.")).TrimStart('\uFEFF');
Directory.CreateDirectory(Option("--state"));

using var session = new ApiSession(new Uri(Option("--api")), Option("--user"), password);
using var map = new ImportMap(Path.Combine(Option("--state"), "import-map.tsv"));
var importer = new LegacyImporter(session, map, Option("--legacy"), Option("--company"), Path.Combine(Option("--state"), "import-report.md"));
try
{
    var rejectedCount = await importer.RunAsync(CancellationToken.None).ConfigureAwait(false);
    return rejectedCount == 0 ? 0 : 2;
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"IMPORT FAILED: {exception}"));
    return 1;
}
