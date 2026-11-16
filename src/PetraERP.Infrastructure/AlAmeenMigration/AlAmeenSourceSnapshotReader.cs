using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// One consistent, cutoff-bounded read of the Al-Ameen master data and ledger that reconciliation compares against.
/// The rows are fixed at capture time; later source writes do not change this evidence.
/// </summary>
public sealed record SourceSnapshot(
    string SourceFingerprint,
    SourceReadGuarantee Guarantee,
    DateTime CutoffUtc,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<SourceAccount> Accounts,
    IReadOnlyList<SourceCurrency> Currencies,
    IReadOnlyList<SourceParty> Parties,
    IReadOnlyList<SourceWarehouse> Warehouses,
    IReadOnlyList<SourceItem> Items,
    IReadOnlyList<SourceStockBalance> Stock,
    IReadOnlyList<SourceVoucher> Vouchers);

/// <summary>How consistent the capture is. Recorded so the evidence never overstates what a read can prove.</summary>
public enum SourceReadGuarantee
{
    Snapshot = 1,
    Serializable = 2,
}

/// <summary>
/// Reads the Al-Ameen source once, in one transaction, with no unlocked reads. A failed capture yields no partial
/// snapshot, so a reconciliation can never mix two points in time. Column names are those of a real Al-Ameen database
/// (verified against the Dardas 2025/2026 exports); Al-Ameen stores an all-zero GUID where it means "none".
/// </summary>
public sealed class AlAmeenSourceSnapshotReader(string connectionString, TimeProvider timeProvider, string? baseCurrencyCode = null)
{
    private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.Ordinal)
    {
        ["$"] = "USD", ["€"] = "EUR", ["£"] = "GBP",
    };

    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly string? _baseCurrencyCode = string.IsNullOrWhiteSpace(baseCurrencyCode) ? null : baseCurrencyCode.Trim().ToUpperInvariant();

    public async Task<SourceSnapshot> CaptureAsync(DateOnly cutoffDate, CancellationToken cancellationToken = default)
    {
        var cutoff = cutoffDate.ToDateTime(new TimeOnly(23, 59, 59));
        var capture = new SourceCapture();

        var connection = new SqlConnection(_connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var guarantee = await AlAmeenSourceSchemaValidator.BeginConsistentReadAsync(connection, cancellationToken).ConfigureAwait(false);
            var transaction = guarantee.Transaction;
            await using (transaction.ConfigureAwait(false))
            {
                await ReadAccounts(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadCurrencies(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadParties(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadWarehouses(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadItems(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadStock(connection, transaction, capture, cancellationToken).ConfigureAwait(false);
                await ReadVouchersAsync(connection, transaction, cutoff, capture, cancellationToken).ConfigureAwait(false);
                capture.MarkContraryBalances();
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new SourceSnapshot(
                capture.Fingerprint(_connectionString),
                guarantee.Kind,
                cutoff,
                _timeProvider.GetUtcNow(),
                capture.Accounts,
                capture.Currencies,
                capture.Parties,
                capture.Warehouses,
                capture.Items,
                capture.Stock,
                capture.Vouchers);
        }
    }

    private sealed class SourceCapture
    {
        public List<SourceAccount> Accounts { get; set; } = [];
        public List<SourceCurrency> Currencies { get; } = [];
        public List<SourceParty> Parties { get; } = [];
        public List<SourceWarehouse> Warehouses { get; } = [];
        public List<SourceItem> Items { get; } = [];
        public List<SourceStockBalance> Stock { get; } = [];
        public List<SourceVoucher> Vouchers { get; } = [];

        /// <summary>
        /// A customer card can end with a credit balance (overpaid) and a supplier card with a debit balance (advance paid).
        /// Such a party is marked as both, so its balance migrates in the direction it really has instead of being dropped.
        /// </summary>
        public void MarkContraryBalances()
        {
            var nets = new Dictionary<(Guid AccountId, Guid CurrencyId), decimal>();
            foreach (var voucher in Vouchers)
            {
                foreach (var line in voucher.Lines)
                {
                    var key = (line.AccountId, line.CurrencyId != Guid.Empty ? line.CurrencyId : voucher.CurrencyId);
                    nets[key] = nets.GetValueOrDefault(key) + line.Debit - line.Credit;
                }
            }

            for (var i = 0; i < Parties.Count; i++)
            {
                var party = Parties[i];
                var supplierOnly = party.IsSupplier && !party.IsCustomer;
                var contrary = nets.Any(n => n.Key.AccountId == party.AccountId && Math.Round(n.Value, 4, MidpointRounding.ToEven) != 0m
                    && (supplierOnly ? n.Value > 0m : n.Value < 0m));
                if (contrary && !(party.IsCustomer && party.IsSupplier))
                    Parties[i] = party with { IsCustomer = true, IsSupplier = true };
            }
        }
        /// <summary>Stable identity of what was read: source database plus captured row counts and ledger totals.</summary>
        public string Fingerprint(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var debit = Vouchers.SelectMany(v => v.Lines).Sum(l => l.Debit);
            var credit = Vouchers.SelectMany(v => v.Lines).Sum(l => l.Credit);
            var payload = string.Create(
                CultureInfo.InvariantCulture,
                $"db={builder.InitialCatalog}|accounts={Accounts.Count}|currencies={Currencies.Count}|parties={Parties.Count}|warehouses={Warehouses.Count}|items={Items.Count}|stock={Stock.Count}|vouchers={Vouchers.Count}|lines={Vouchers.Sum(v => v.Lines.Count)}|debit={debit:F4}|credit={credit:F4}");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        }
    }

    private static async Task ReadAsync(
        string sql,
        SqlConnection connection,
        SqlTransaction transaction,
        Action<SqlDataReader> consume,
        CancellationToken cancellationToken)
    {
        var command = NewCommand(connection, transaction, sql);
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    consume(reader);
            }
        }
    }

    private static SqlCommand NewCommand(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 300;
        return command;
    }

    private static string Text(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!.Trim();

    private static string? OptionalText(SqlDataReader reader, int ordinal)
    {
        var value = Text(reader, ordinal);
        return value.Length == 0 ? null : value;
    }

    /// <summary>Al-Ameen writes an all-zero GUID for "no reference".</summary>
    private static Guid? Reference(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) || reader.GetGuid(ordinal) == Guid.Empty ? null : reader.GetGuid(ordinal);

    private static decimal Number(SqlDataReader reader, int ordinal, decimal fallback = 0m)
        => reader.IsDBNull(ordinal) ? fallback : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private Task ReadCurrencies(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
        => ReadAsync("SELECT [GUID], [Code], [Name], [CurrencyVal], [PartPrecision] FROM [my000] ORDER BY [Number]",
            connection, transaction, reader =>
            {
                var rawCode = Text(reader, 1);
                var rate = Number(reader, 3, 1m);
                var code = CurrencySymbols.TryGetValue(rawCode, out var iso) ? iso
                    : rawCode.Length == 3 && rawCode.All(char.IsAsciiLetter) ? rawCode.ToUpperInvariant()
                    : rate == 1m && _baseCurrencyCode is not null ? _baseCurrencyCode
                    : rawCode;
                var precision = Number(reader, 4, 100m) >= 1000m ? 3 : 2;
                capture.Currencies.Add(new SourceCurrency(reader.GetGuid(0), code, Text(reader, 2), rate, precision));
            }, cancellationToken);

    private static async Task ReadAccounts(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
    {
        // Type 2 rows are the statement headings (balance sheet, profit and loss, trading, operating), not posting accounts.
        var rows = new List<AlAmeenAccountClassifier.Row>();
        await ReadAsync("SELECT [GUID], [ParentGUID], [Code], [Name] FROM [ac000] WHERE [Type] = 1 ORDER BY [Code]",
            connection, transaction, reader => rows.Add(new AlAmeenAccountClassifier.Row(
                reader.GetGuid(0), Reference(reader, 1), Text(reader, 2), Text(reader, 3))), cancellationToken).ConfigureAwait(false);
        capture.Accounts = AlAmeenAccountClassifier.Classify(rows);
    }

    private static Task ReadParties(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
    {
        // Al-Ameen has one customer card type; the linked control account decides the role (liabilities = supplier).
        var accountTypes = capture.Accounts.ToDictionary(a => a.Id, a => a.AccountType);
        return ReadAsync("SELECT [GUID], [AccountGUID], [Number], [CustomerName], [Phone1], [Address] FROM [cu000] ORDER BY [Number]",
            connection, transaction, reader =>
            {
                var accountId = Reference(reader, 1) ?? Guid.Empty;
                var supplier = accountTypes.TryGetValue(accountId, out var type) && type == AlAmeenAccountClassifier.Liabilities;
                capture.Parties.Add(new SourceParty(
                    reader.GetGuid(0), accountId, Text(reader, 2), Text(reader, 3), OptionalText(reader, 4), OptionalText(reader, 5),
                    IsCustomer: !supplier, IsSupplier: supplier));
            }, cancellationToken);
    }

    private static Task ReadWarehouses(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
        => ReadAsync("SELECT [GUID], [AccountGUID], [Code], [Name] FROM [st000] ORDER BY [Number]",
            connection, transaction, reader => capture.Warehouses.Add(new SourceWarehouse(
                reader.GetGuid(0), Reference(reader, 1), Text(reader, 2), Text(reader, 3))), cancellationToken);

    // GroupGUID points at an item group (gr000), not at another item, so items carry no parent link.
    private static Task ReadItems(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
        => ReadAsync("SELECT [GUID], [GroupGUID], [Code], [Name], [BarCode], [Unity], [Retail] FROM [mt000] ORDER BY [Number]",
            connection, transaction, reader => capture.Items.Add(new SourceItem(
                reader.GetGuid(0), null, Text(reader, 2), Text(reader, 3), OptionalText(reader, 4), Text(reader, 5), Number(reader, 6))), cancellationToken);

    /// <summary>
    /// The Al-Ameen stock table holds quantities only (its book column is not a cost). The unit cost is the item card's
    /// average price, falling back to its last price; an item with neither is valued at zero and reported as such.
    /// </summary>
    private static Task ReadStock(SqlConnection connection, SqlTransaction transaction, SourceCapture capture, CancellationToken cancellationToken)
        => ReadAsync("SELECT s.[MatGUID], s.[StoreGUID], s.[Qty], m.[AvgPrice], m.[LastPrice] FROM [ms000] s JOIN [mt000] m ON m.[GUID] = s.[MatGUID] WHERE s.[Qty] <> 0",
            connection, transaction, reader =>
            {
                var quantity = Number(reader, 2);
                var average = Number(reader, 3);
                var cost = average > 0m ? average : Number(reader, 4);
                capture.Stock.Add(new SourceStockBalance(
                    reader.GetGuid(0), reader.GetGuid(1), quantity, cost, Math.Round(quantity * cost, 4, MidpointRounding.ToEven)));
            }, cancellationToken);

    private static async Task ReadVouchersAsync(SqlConnection connection, SqlTransaction transaction, DateTime cutoff, SourceCapture capture, CancellationToken cancellationToken)
    {
        var linesByVoucher = new Dictionary<Guid, List<SourceVoucherLine>>();
        var headerCommand = NewCommand(connection, transaction, "SELECT [GUID], [Number], [Date], [CurrencyGUID], [CurrencyVal], [Notes] FROM [ce000] WHERE [Date] <= @cutoff ORDER BY [Date], [Number]");
        await using (headerCommand.ConfigureAwait(false))
        {
            headerCommand.Parameters.Add("@cutoff", SqlDbType.DateTime2).Value = cutoff;
            var headerReader = await headerCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (headerReader.ConfigureAwait(false))
            {
                while (await headerReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = headerReader.GetGuid(0);
                    var lines = new List<SourceVoucherLine>();
                    linesByVoucher[id] = lines;
                    capture.Vouchers.Add(new SourceVoucher(
                        id,
                        Text(headerReader, 1),
                        headerReader.GetDateTime(2),
                        Reference(headerReader, 3) ?? Guid.Empty,
                        Number(headerReader, 4, 1m),
                        OptionalText(headerReader, 5),
                        lines));
                }
            }
        }

        var lineCommand = NewCommand(connection, transaction, "SELECT [GUID], [ParentGUID], [AccountGUID], [CurrencyGUID], [CurrencyVal], [Debit], [Credit], [Notes], [CostGUID] FROM [en000] ORDER BY [ParentGUID], [Number]");
        await using (lineCommand.ConfigureAwait(false))
        {
            var lineReader = await lineCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (lineReader.ConfigureAwait(false))
            {
                while (await lineReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var parentId = lineReader.GetGuid(1);
                    if (!linesByVoucher.TryGetValue(parentId, out var lines))
                        continue;
                    lines.Add(new SourceVoucherLine(
                        lineReader.GetGuid(0),
                        parentId,
                        lineReader.GetGuid(2),
                        Reference(lineReader, 3) ?? Guid.Empty,
                        Number(lineReader, 4, 1m),
                        Number(lineReader, 5),
                        Number(lineReader, 6),
                        OptionalText(lineReader, 7),
                        Reference(lineReader, 8)));
                }
            }
        }
    }
}
