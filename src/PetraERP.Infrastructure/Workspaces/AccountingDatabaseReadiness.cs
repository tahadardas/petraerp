using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Globalization;
using System.Text.RegularExpressions;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Workspaces;

public static class AccountingDatabaseReadiness
{
    public static void RequireApprovedMigrations(AccountingDbContext context)
    {
        if (!context.Database.GetMigrations().SequenceEqual(AccountingSchema.ApprovedMigrations, StringComparer.Ordinal))
            throw new BusinessRuleException("workspace.schema_not_approved", "The accounting migration set is not the explicitly approved independent schema.");
    }

    public static async Task ValidateMigrationTargetAsync(AccountingDbContext context, CancellationToken cancellationToken)
    {
        RequireApprovedMigrations(context);
        try
        {
            // Probe through ADO before EF's execution strategy wraps SQL 4060 as a transient exception.
            // Only a missing/inaccessible target is allowed through to EF's guarded database creator;
            // an existing target is still inspected below and is never adopted implicitly.
            using var probe = new SqlConnection(context.Database.GetConnectionString());
            await probe.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tables = await TablesAsync(context, cancellationToken).ConfigureAwait(false);
            if (tables.Count != 0 && !tables.Contains($"{AccountingSchema.HistorySchema}.{AccountingSchema.HistoryTable}", StringComparer.Ordinal))
                throw new BusinessRuleException("workspace.unowned_schema", "Refusing to migrate an existing database without independent accounting migration history.");
            if (tables.Any(t => t.StartsWith("security.", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)))
                throw new BusinessRuleException("workspace.unowned_schema", "A Legacy or security-bearing database cannot be adopted as an annual database.");
            if (tables.Count != 0)
            {
                var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
                var expectedTables = context.Model.GetEntityTypes().Select(t => $"{t.GetSchema() ?? "dbo"}.{t.GetTableName()}")
                    .Append($"{AccountingSchema.HistorySchema}.{AccountingSchema.HistoryTable}").ToHashSet(StringComparer.Ordinal);
                if (!applied.SequenceEqual(AccountingSchema.ApprovedMigrations.Take(applied.Length), StringComparer.Ordinal)
                    || tables.Any(t => !expectedTables.Contains(t)))
                    throw new BusinessRuleException("workspace.unowned_schema", "Existing accounting history or tables are not an approved migration prefix.");
            }
        }
        catch (SqlException exception) when (exception.Number == 4060)
        {
            // EF migrations may create a new server-generated target. Other errors are not treated as missing databases.
        }
    }

    public static async Task ValidateSchemaAsync(AccountingDbContext context, CancellationToken cancellationToken)
    {
        RequireApprovedMigrations(context);
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (!AccountingSchema.ApprovedMigrations.SequenceEqual(applied, StringComparer.Ordinal))
            throw new BusinessRuleException("workspace.upgrade_required", "The annual database has incomplete or unapproved migration history.");
        var expected = context.Model.GetEntityTypes().Select(t => $"{t.GetSchema() ?? "dbo"}.{t.GetTableName()}")
            .Append($"{AccountingSchema.HistorySchema}.{AccountingSchema.HistoryTable}").Order(StringComparer.Ordinal);
        var actual = (await TablesAsync(context, cancellationToken).ConfigureAwait(false)).Order(StringComparer.Ordinal);
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new BusinessRuleException("workspace.schema_mismatch", "The annual database table set does not match the approved business model.");
        var expectedKeys = context.Model.GetRelationalModel().Tables.SelectMany(t => t.ForeignKeyConstraints)
            .Select(k => k.Name).Order(StringComparer.Ordinal);
        var expectedUniqueKeys = context.Model.GetRelationalModel().Tables.SelectMany(t => t.UniqueConstraints)
            .Select(k => k.Name).Order(StringComparer.Ordinal);
        var actualUniqueKeys = await context.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.key_constraints WHERE parent_object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory')")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var actualKeys = await context.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var expectedChecks = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel().Tables
            .SelectMany(t => t.CheckConstraints).Select(c => c.Name).Order(StringComparer.Ordinal);
        var actualChecks = await context.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var triggers = await context.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.triggers WHERE parent_class=1 AND is_disabled=0 AND is_ms_shipped=0")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var expectedTriggers = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredTriggers()).Select(t => t.ModelName).Order(StringComparer.Ordinal);
        if (!expectedKeys.SequenceEqual(actualKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !expectedUniqueKeys.SequenceEqual(actualUniqueKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !expectedChecks.SequenceEqual(actualChecks.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !expectedTriggers.SequenceEqual(triggers.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new BusinessRuleException("workspace.schema_mismatch", "The annual database constraints or audit triggers are missing, disabled or untrusted.");
        await ValidateApprovedMetadataAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ValidateWorkspaceAsync(AccountingDbContext context, Guid companyId, Guid fiscalYearId,
        DateOnly start, DateOnly end, string yearCode, CancellationToken cancellationToken,
        string? expectedBaseCurrencyCode = null, IReadOnlyCollection<Guid>? registeredBranchIds = null)
    {
        await ValidateSchemaAsync(context, cancellationToken).ConfigureAwait(false);
        var companies = await context.Companies.AsNoTracking().Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        var years = await context.FiscalYears.AsNoTracking().Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (companies.Count != 1 || years.Count != 1) throw Mismatch();
        var company = companies[0];
        var year = years[0];
        if (company.Id != companyId || !company.IsActive || year.Id != fiscalYearId
            || year.CompanyId != companyId || year.RangeStart != start || year.RangeEnd != end || year.Code != yearCode
            || (expectedBaseCurrencyCode is not null && !string.Equals(company.BaseCurrencyCode, expectedBaseCurrencyCode, StringComparison.Ordinal)))
            throw Mismatch();
        var periods = await context.AccountingPeriods.AsNoTracking().OrderBy(p => p.RangeStart).ToListAsync(cancellationToken).ConfigureAwait(false);
        var next = start;
        if (periods.Count == 0) throw Mismatch();
        foreach (var period in periods)
        {
            if (period.CompanyId != companyId || period.FiscalYearId != fiscalYearId || period.RangeStart != next || period.RangeEnd < next || period.RangeEnd > end)
                throw Mismatch();
            if (period != periods[^1]) next = period.RangeEnd.AddDays(1);
        }
        if (periods[^1].RangeEnd != end || !await context.Branches.AnyAsync(b => b.CompanyId == companyId && b.IsActive, cancellationToken).ConfigureAwait(false)
            || await context.Branches.AnyAsync(b => b.CompanyId != companyId, cancellationToken).ConfigureAwait(false)) throw Mismatch();
        // Branch snapshots retain historical identities. New central branches need not already exist in every
        // annual database, but an annual identity must never be invented or reassigned to another company.
        if (registeredBranchIds is not null)
        {
            var annualBranches = await context.Branches.Select(b => b.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (annualBranches.Any(id => !registeredBranchIds.Contains(id))) throw Mismatch();
        }
    }

    private static async Task ValidateApprovedMetadataAsync(AccountingDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.HasPendingModelChanges())
            throw new BusinessRuleException("workspace.schema_not_approved", "The accounting EF model differs from its approved migration snapshot.");
        // Expected metadata comes from immutable migration operations, never from the database being checked.
        // The runtime model alone would let a simultaneous model/DDL mistake validate itself.
        var assembly = context.GetService<IMigrationsAssembly>();
        var operations = AccountingSchema.ApprovedMigrations.SelectMany(id => assembly.CreateMigration(
            assembly.Migrations[id], context.Database.ProviderName!).UpOperations).ToArray();
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        var tableColumns = tables.SelectMany(t => t.Columns.Select(c =>
            $"{Table(t.Schema, t.Name)}|{c.Name}|{NormalizeType(c.ColumnType!)}|{(c.IsNullable ? 1 : 0)}|{(c["SqlServer:Identity"] is null ? 0 : 1)}|{SqlTokens(c.ComputedColumnSql)}"));
        var addedColumns = operations.OfType<AddColumnOperation>().Select(c =>
            $"{Table(c.Schema, c.Table)}|{c.Name}|{NormalizeType(c.ColumnType!)}|{(c.IsNullable ? 1 : 0)}|{(c["SqlServer:Identity"] is null ? 0 : 1)}|{SqlTokens(c.ComputedColumnSql)}");
        await MatchMetadataAsync(context, "columns", tableColumns.Concat(addedColumns), """
            SELECT SCHEMA_NAME(t.schema_id) COLLATE DATABASE_DEFAULT+N'.'+t.name COLLATE DATABASE_DEFAULT+N'|'+c.name COLLATE DATABASE_DEFAULT+N'|'+
                TYPE_NAME(c.system_type_id) COLLATE DATABASE_DEFAULT+CASE
                    WHEN TYPE_NAME(c.system_type_id) COLLATE DATABASE_DEFAULT IN ('nvarchar','nchar') THEN '('+CASE WHEN c.max_length=-1 THEN 'max' ELSE CONVERT(varchar(12),c.max_length/2) END+')'
                    WHEN TYPE_NAME(c.system_type_id) COLLATE DATABASE_DEFAULT IN ('varchar','char','varbinary','binary') THEN '('+CASE WHEN c.max_length=-1 THEN 'max' ELSE CONVERT(varchar(12),c.max_length) END+')'
                    WHEN TYPE_NAME(c.system_type_id) COLLATE DATABASE_DEFAULT IN ('decimal','numeric') THEN '('+CONVERT(varchar(12),c.precision)+','+CONVERT(varchar(12),c.scale)+')'
                    WHEN TYPE_NAME(c.system_type_id) COLLATE DATABASE_DEFAULT IN ('datetime2','datetimeoffset','time') THEN '('+CONVERT(varchar(12),c.scale)+')'
                    ELSE '' END+N'|'+CONVERT(nvarchar(1),c.is_nullable)+N'|'+CONVERT(nvarchar(1),c.is_identity)+N'|'+COALESCE(cc.definition,'') AS [Value]
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
            WHERE t.is_ms_shipped=0 AND t.object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory')
            """, cancellationToken, NormalizeColumn).ConfigureAwait(false);

        await MatchMetadataAsync(context, "indexes", operations.OfType<CreateIndexOperation>().Select(i =>
            $"{Table(i.Schema, i.Table)}|{i.Name}|{(i.IsUnique ? 1 : 0)}|{string.Join(',', i.Columns.Select((c, n) => c + (i.IsDescending is { Length: > 0 } && i.IsDescending[n] ? ":DESC" : i.IsDescending is { Length: 0 } ? ":DESC" : ":ASC")))}|{string.Join(',', (string[]?)i["SqlServer:Include"] ?? [])}|{SqlTokens(i.Filter, true)}|0|0"), """
            SELECT SCHEMA_NAME(t.schema_id) COLLATE DATABASE_DEFAULT+N'.'+t.name COLLATE DATABASE_DEFAULT+N'|'+i.name COLLATE DATABASE_DEFAULT+N'|'+CONVERT(nvarchar(1),i.is_unique)+N'|'+
                COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),c.name COLLATE DATABASE_DEFAULT+CASE WHEN ic.is_descending_key=1 THEN ':DESC' ELSE ':ASC' END),',') WITHIN GROUP(ORDER BY ic.key_ordinal)
                    FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0),'')+N'|'+
                COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),c.name COLLATE DATABASE_DEFAULT),',') WITHIN GROUP(ORDER BY ic.index_column_id)
                    FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1),'')+N'|'+
                COALESCE(i.filter_definition,'')+N'|'+CONVERT(nvarchar(1),i.is_disabled)+N'|'+CONVERT(nvarchar(1),i.is_hypothetical) AS [Value]
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
            WHERE t.is_ms_shipped=0 AND i.index_id>0 AND i.is_primary_key=0 AND i.is_unique_constraint=0
                AND t.object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory')
            """, cancellationToken, NormalizeIndex).ConfigureAwait(false);

        await MatchMetadataAsync(context, "keys", tables.SelectMany(t =>
            t.UniqueConstraints.Select(k => $"{Table(t.Schema, t.Name)}|{k.Name}|UQ|{string.Join(',', k.Columns)}")
                .Concat(t.PrimaryKey is null ? [] : [$"{Table(t.Schema, t.Name)}|{t.PrimaryKey.Name}|PK|{string.Join(',', t.PrimaryKey.Columns)}"])), """
            SELECT SCHEMA_NAME(t.schema_id) COLLATE DATABASE_DEFAULT+N'.'+t.name COLLATE DATABASE_DEFAULT+N'|'+k.name COLLATE DATABASE_DEFAULT+N'|'+k.type COLLATE DATABASE_DEFAULT+N'|'+
                (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name COLLATE DATABASE_DEFAULT),',') WITHIN GROUP(ORDER BY ic.key_ordinal)
                    FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                    WHERE ic.object_id=k.parent_object_id AND ic.index_id=k.unique_index_id AND ic.key_ordinal>0) AS [Value]
            FROM sys.key_constraints k JOIN sys.tables t ON t.object_id=k.parent_object_id
            JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id=k.unique_index_id
            WHERE t.object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory') AND i.is_disabled=0
            """, cancellationToken).ConfigureAwait(false);

        var tableFks = tables.SelectMany(t => t.ForeignKeys).Select(k =>
            $"{Table(k.Schema, k.Table)}|{k.Name}|{Table(k.PrincipalSchema, k.PrincipalTable)}|{string.Join(',', k.Columns)}|{string.Join(',', k.PrincipalColumns!)}|{ReferentialActionName(k.OnDelete)}|{ReferentialActionName(k.OnUpdate)}");
        var addedFks = operations.OfType<AddForeignKeyOperation>().Select(k =>
            $"{Table(k.Schema, k.Table)}|{k.Name}|{Table(k.PrincipalSchema, k.PrincipalTable)}|{string.Join(',', k.Columns)}|{string.Join(',', k.PrincipalColumns!)}|{ReferentialActionName(k.OnDelete)}|{ReferentialActionName(k.OnUpdate)}");
        await MatchMetadataAsync(context, "foreign keys", tableFks.Concat(addedFks), """
            SELECT SCHEMA_NAME(t.schema_id) COLLATE DATABASE_DEFAULT+N'.'+t.name COLLATE DATABASE_DEFAULT+N'|'+f.name COLLATE DATABASE_DEFAULT+N'|'+SCHEMA_NAME(p.schema_id) COLLATE DATABASE_DEFAULT+N'.'+p.name COLLATE DATABASE_DEFAULT+N'|'+
                (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name COLLATE DATABASE_DEFAULT),',') WITHIN GROUP(ORDER BY fc.constraint_column_id)
                    FROM sys.foreign_key_columns fc JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id WHERE fc.constraint_object_id=f.object_id)+N'|'+
                (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name COLLATE DATABASE_DEFAULT),',') WITHIN GROUP(ORDER BY fc.constraint_column_id)
                    FROM sys.foreign_key_columns fc JOIN sys.columns c ON c.object_id=fc.referenced_object_id AND c.column_id=fc.referenced_column_id WHERE fc.constraint_object_id=f.object_id)+N'|'+
                f.delete_referential_action_desc COLLATE DATABASE_DEFAULT+N'|'+f.update_referential_action_desc COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id JOIN sys.tables p ON p.object_id=f.referenced_object_id
            WHERE f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0
            """, cancellationToken).ConfigureAwait(false);

        var tableDefaults = tables.SelectMany(t => t.Columns.Where(c => c.DefaultValueSql is not null || c.DefaultValue is not null)
            .Select(c => $"{Table(t.Schema, t.Name)}|{c.Name}|{SqlTokens(c.DefaultValueSql ?? ConstantSql(c.DefaultValue!), true)}"));
        var addedDefaults = operations.OfType<AddColumnOperation>().Where(c => c.DefaultValueSql is not null || c.DefaultValue is not null)
            .Select(c => $"{Table(c.Schema, c.Table)}|{c.Name}|{SqlTokens(c.DefaultValueSql ?? ConstantSql(c.DefaultValue!), true)}");
        await MatchMetadataAsync(context, "defaults", tableDefaults.Concat(addedDefaults), """
            SELECT SCHEMA_NAME(t.schema_id) COLLATE DATABASE_DEFAULT+N'.'+t.name COLLATE DATABASE_DEFAULT+N'|'+c.name COLLATE DATABASE_DEFAULT+N'|'+d.definition AS [Value]
            FROM sys.default_constraints d JOIN sys.tables t ON t.object_id=d.parent_object_id
            JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
            WHERE t.object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory')
            """, cancellationToken, NormalizeExpressionTail).ConfigureAwait(false);

        await MatchMetadataAsync(context, "sequences", operations.OfType<CreateSequenceOperation>().Select(s =>
            $"{Table(s.Schema, s.Name)}|{(s.ClrType == typeof(long) ? "bigint" : "int")}|{s.StartValue}|{s.IncrementBy}|{s.MinValue ?? long.MinValue}|{s.MaxValue ?? long.MaxValue}|{(s.IsCyclic ? 1 : 0)}"), """
            SELECT SCHEMA_NAME(schema_id) COLLATE DATABASE_DEFAULT+N'.'+name COLLATE DATABASE_DEFAULT+N'|'+TYPE_NAME(system_type_id) COLLATE DATABASE_DEFAULT+N'|'+CONVERT(nvarchar(40),start_value)+N'|'+
                CONVERT(nvarchar(40),increment)+N'|'+CONVERT(nvarchar(40),minimum_value)+N'|'+CONVERT(nvarchar(40),maximum_value)+N'|'+CONVERT(nvarchar(1),is_cycling) AS [Value] FROM sys.sequences
            """, cancellationToken).ConfigureAwait(false);

        var approvedTriggers = operations.OfType<SqlOperation>().SelectMany(o => Regex.Matches(o.Sql,
            @"EXEC\(N'(?<definition>(?:''|[^'])*)'\);", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(m => SqlTokens(m.Groups["definition"].Value.Replace("''", "'", StringComparison.Ordinal))).ToArray();
        var actualTriggers = await context.Database.SqlQueryRaw<string>("""
            SELECT m.definition AS [Value] FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id=t.object_id
            WHERE t.parent_class=1 AND t.is_disabled=0 AND t.is_ms_shipped=0 AND t.is_not_for_replication=0
                AND m.execute_as_principal_id IS NULL AND m.uses_ansi_nulls=1 AND m.uses_quoted_identifier=1
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!approvedTriggers.Order(StringComparer.Ordinal).SequenceEqual(actualTriggers.Select(SqlTokensTrigger).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw SchemaMismatch("trigger definitions");
    }

    private static async Task MatchMetadataAsync(AccountingDbContext context, string category, IEnumerable<string> expected,
        string sql, CancellationToken cancellationToken, Func<string, string>? normalize = null)
    {
        var actual = await context.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken).ConfigureAwait(false);
        normalize ??= static value => value.ToUpperInvariant();
        if (!expected.Select(normalize).Order(StringComparer.Ordinal).SequenceEqual(actual.Select(normalize).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw SchemaMismatch(category);
    }

    private static string NormalizeColumn(string value)
    {
        var parts = value.Split('|');
        parts[2] = NormalizeType(parts[2]);
        parts[5] = SqlTokens(parts[5]);
        return string.Join('|', parts.Select((part, index) => index == 5 ? part : part.ToUpperInvariant()));
    }
    private static string NormalizeIndex(string value)
    {
        var parts = value.Split('|');
        parts[5] = SqlTokens(parts[5], true);
        return string.Join('|', parts.Select((part, index) => index == 5 ? part : part.ToUpperInvariant()));
    }
    private static string NormalizeExpressionTail(string value)
    {
        var position = value.LastIndexOf('|');
        return value[..(position + 1)].ToUpperInvariant() + SqlTokens(value[(position + 1)..], true);
    }
    private static string NormalizeType(string value) => value.Replace(" ", "", StringComparison.Ordinal)
        .Replace("rowversion", "timestamp", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
    private static string Table(string? schema, string name) => $"{schema ?? "dbo"}.{name}";
    private static string ReferentialActionName(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "CASCADE", ReferentialAction.SetNull => "SET_NULL",
        ReferentialAction.SetDefault => "SET_DEFAULT", _ => "NO_ACTION",
    };
    private static string ConstantSql(object value) => value is bool boolean ? boolean ? "1" : "0" : Convert.ToString(value, CultureInfo.InvariantCulture)!;
    private static string SqlTokensTrigger(string value) => SqlTokens(value);

    // A token fingerprint tolerates whitespace, comments, keyword casing and identifier quoting. String
    // contents and expression grouping remain significant; removing all parentheses would hide real changes.
    private static string SqlTokens(string? sql, bool expression = false)
    {
        if (string.IsNullOrEmpty(sql)) return "";
        var tokens = Regex.Matches(sql, @"--[^\r\n]*|/\*[\s\S]*?\*/|N?'(?:''|[^'])*'|\[(?:\]\]|[^\]])*\]|\w+|<>|!=|<=|>=|[^\s]",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Select(m => m.Value)
            .Where(t => !t.StartsWith("--", StringComparison.Ordinal) && !t.StartsWith("/*", StringComparison.Ordinal) && t != ";")
            .Select(t => t.StartsWith('\'') || t.StartsWith("N'", StringComparison.OrdinalIgnoreCase)
                ? (t.StartsWith("n'", StringComparison.Ordinal) ? "N" + t[1..] : t)
                : t.StartsWith('[') ? t[1..^1].Replace("]]", "]", StringComparison.Ordinal).ToUpperInvariant() : t.ToUpperInvariant()).ToList();
        if (tokens.Count > 1 && tokens[0] == "ALTER" && tokens[1] == "TRIGGER") tokens[0] = "CREATE";
        if (tokens.Count > 3 && tokens[0] == "CREATE" && tokens[1] == "OR" && tokens[2] == "ALTER" && tokens[3] == "TRIGGER") tokens.RemoveRange(1, 2);
        if (expression)
        {
            // SQL Server renders a model default/filter with outer parentheses, single-literal parentheses and
            // CONVERT/CAST wrappers around a constant (a bit default 0 is stored as CONVERT(bit,(0))). These are
            // canonicalized so a stored constant equals the migration's model value. Only a wrapper around a lone
            // literal is unwrapped; internal grouping and non-constant expressions are preserved.
            bool removed;
            do
            {
                removed = false;
                for (var i = 0; i + 1 < tokens.Count; i++)
                {
                    if (tokens[i] is not ("CONVERT" or "CAST") || tokens[i + 1] != "(") continue;
                    var depth = 0; var close = -1;
                    for (var j = i + 1; j < tokens.Count; j++)
                    {
                        if (tokens[j] == "(") depth++;
                        else if (tokens[j] == ")") { depth--; if (depth == 0) { close = j; break; } }
                    }
                    if (close < 0) continue;
                    var inner = tokens.GetRange(i + 2, close - (i + 2));
                    if (tokens[i] == "CONVERT")
                    {
                        if (inner.Count >= 2 && inner[1] == ",") inner = inner.GetRange(2, inner.Count - 2); else continue;
                    }
                    else
                    {
                        var asIndex = inner.LastIndexOf("AS");
                        if (asIndex > 0) inner = inner.GetRange(0, asIndex); else continue;
                    }
                    var literals = inner.Where(t => t != "(" && t != ")").ToList();
                    if (literals.Count != 1 || !(Regex.IsMatch(literals[0], @"^\d+$", RegexOptions.CultureInvariant)
                        || literals[0].StartsWith('\'') || literals[0].StartsWith("N'", StringComparison.Ordinal))) continue;
                    tokens.RemoveRange(i, close - i + 1);
                    tokens.Insert(i, literals[0]);
                    removed = true;
                    break;
                }
                if (tokens.Count >= 3 && tokens[0] == "(" && tokens[^1] == ")")
                {
                    var depth = 0;
                    var wrapsAll = true;
                    for (var i = 0; i < tokens.Count - 1; i++)
                    {
                        if (tokens[i] == "(") depth++;
                        if (tokens[i] == ")") depth--;
                        if (depth == 0) { wrapsAll = false; break; }
                    }
                    if (wrapsAll) { tokens.RemoveAt(tokens.Count - 1); tokens.RemoveAt(0); removed = true; }
                }
                for (var i = 0; i + 2 < tokens.Count; i++)
                    if (tokens[i] == "(" && tokens[i + 2] == ")" && Regex.IsMatch(tokens[i + 1], @"^\d+$", RegexOptions.CultureInvariant)
                        && (i == 0 || tokens[i - 1] is "=" or ">" or "<" or ">=" or "<=" or "<>" or "("))
                    { tokens.RemoveAt(i + 2); tokens.RemoveAt(i); removed = true; }
            } while (removed);
        }
        return string.Join(' ', tokens);
    }
    private static BusinessRuleException SchemaMismatch(string category) => new("workspace.schema_mismatch", $"The annual database {category} differ from the approved immutable accounting migration.");

    private static Task<List<string>> TablesAsync(AccountingDbContext context, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<string>("SELECT SCHEMA_NAME([schema_id]) COLLATE DATABASE_DEFAULT + N'.' + [name] COLLATE DATABASE_DEFAULT AS [Value] FROM sys.tables WHERE is_ms_shipped = 0")
            .ToListAsync(cancellationToken);
    private static BusinessRuleException Mismatch() => new("workspace.scope_mismatch", "Annual database company, year, branch or period coverage does not match its registration.");
}
