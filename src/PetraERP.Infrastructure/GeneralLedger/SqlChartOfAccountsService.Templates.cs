using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.GeneralLedger.Templates;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed partial class SqlChartOfAccountsService : IChartTemplateImportService
{
    public async Task<TemplateImportPreview> PreviewAsync(LedgerMasterScope scope, ChartOfAccountsTemplate chartTemplate, CancellationToken cancellationToken)
    {
        var template = chartTemplate;
        ArgumentNullException.ThrowIfNull(template);
        var errors = template.Validate().ToList();
        if (template.Accounts is null || template.DefaultAccountRoles is null) return new(template, errors);
        var codes = template.Accounts.Select(a => a.AccountCode).ToArray();
        var duplicates = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && codes.Contains(a.Code))
            .Select(a => a.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        errors.AddRange(duplicates.Select(c => "template.destination_code_exists:" + c));
        if (errors.Count == 0)
        {
            try { _ = await TemplateRowsAsync(scope, template, cancellationToken).ConfigureAwait(false); }
            catch (ArgumentException) { errors.Add("template.account_policy_invalid"); }
        }
        return new(template, errors.AsReadOnly());
    }

    public async Task<TemplateImportResult> ImportAsync(LedgerMasterScope scope, ChartOfAccountsTemplate chartTemplate, CancellationToken cancellationToken)
    {
        var template = chartTemplate;
        ArgumentNullException.ThrowIfNull(template);
        if (template.Validate().Count != 0) throw new BusinessRuleException("template.invalid", "The template validation report must be resolved before import.");
        var canonical = template with { Accounts = template.OrderedAccounts(), DefaultAccountRoles = new SortedDictionary<string,string>(template.DefaultAccountRoles.ToDictionary(p=>p.Key,p=>p.Value), StringComparer.Ordinal) };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
        var correlation = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"TemplateImport:{scope.CompanyId:D}:{template.TemplateId:D}:{template.TemplateVersion}"))[..16]);
        var auditReason = $"Template {template.TemplateCode} v{template.TemplateVersion}; {hash}; {template.TemplateSource}";
        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        await InventoryWriteScope.LockCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var previous = await context.AccountAudit.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && a.CorrelationId == correlation)
            .Select(a => a.Reason).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            if (previous != auditReason) throw new BusinessRuleException("template.version_conflict", "This template ID/version was already imported with different content. Supply a new version.");
            return new(0, true, hash);
        }
        var preview = await PreviewAsync(scope, template, cancellationToken).ConfigureAwait(false);
        if (!preview.CanImport) throw new BusinessRuleException("template.import_rejected", string.Join("; ", preview.Errors));
        var rows = await TemplateRowsAsync(scope, template, cancellationToken).ConfigureAwait(false);
        var auditScope = LedgerMasterScope.Create(scope.CompanyId, scope.ActorUserId, scope.WorkingBranchId, correlation);
        foreach (var row in rows)
        {
            context.Accounts.Add(row);
            AuditAccount(auditScope, row.Id, AccountAuditActionCode.Created, null, State(row), auditReason);
        }
        await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.AccountCodeTaken, "account", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(rows.Count, false, hash);
    }

    private async Task<List<AccountRow>> TemplateRowsAsync(LedgerMasterScope scope, ChartOfAccountsTemplate template, CancellationToken token)
    {
        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, token).ConfigureAwait(false);
        var domain = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AccountRow>();
        foreach (var a in template.OrderedAccounts())
        {
            var input = new AccountInput { Code=a.AccountCode, NameArabic=a.AccountNameArabic, NameEnglish=a.AccountNameEnglish,
                AccountType=(AccountTypeCode)a.AccountType, PostingAllowed=a.PostingAllowed, CurrencyMode=(CurrencyModeCode)a.CurrencyMode, CostCenterPolicy=(CostCenterPolicyCode)a.CostCenterPolicy };
            var account = Account.Create(company, LedgerRowMapping.Code(input.Code), LedgerRowMapping.Name(input.NameArabic,input.NameEnglish),
                a.AccountType, a.PostingAllowed, a.CostCenterPolicy, Policy(input), a.ParentCode is null ? null : domain[a.ParentCode]);
            domain.Add(a.AccountCode,account);
            var row = new AccountRow {Id=account.Id,CompanyId=scope.CompanyId,Code=account.Code.Value,NameArabic=account.Name.Arabic,NameEnglish=account.Name.English};
            Apply(row, account); result.Add(row);
        }
        return result;
    }

    public async Task<TemplateImportPreview> ReadFileAsync(LedgerMasterScope scope, string fileName, Stream stream, ChartOfAccountsTemplate metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read=await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false))!=0)
        {
            if (buffer.Length+read > 5*1024*1024) throw new BusinessRuleException("template.file_too_large", "Template files are limited to 5 MiB.");
            buffer.Write(chunk,0,read);
        }
        buffer.Position=0;
        var rows=new List<string[]>();
        var extension=Path.GetExtension(fileName);
        if (extension.Equals(".csv",StringComparison.OrdinalIgnoreCase))
        {
            using var parser=new TextFieldParser(buffer,new UTF8Encoding(false,true),true,true) { TextFieldType=FieldType.Delimited, HasFieldsEnclosedInQuotes=true };
            parser.SetDelimiters(",");
            while (!parser.EndOfData)
            {
                if (rows.Count>20000) throw new BusinessRuleException("template.account_count_invalid","At most 20000 accounts may be imported.");
                rows.Add(parser.ReadFields() ?? []);
            }
        }
        else if (extension.Equals(".xlsx",StringComparison.OrdinalIgnoreCase))
        {
            using (var zip=new ZipArchive(buffer,ZipArchiveMode.Read,true))
                if (zip.Entries.Count>1024 || zip.Entries.Sum(e=>e.Length)>32L*1024*1024) throw new BusinessRuleException("template.file_too_large","Expanded workbook exceeds the import limit.");
            buffer.Position=0;
            using var workbook=new XLWorkbook(buffer);
            var sheet=workbook.Worksheets.Single();
            var range=sheet.RangeUsed() ?? throw new BusinessRuleException("template.file_empty","The workbook has no account rows.");
            if (range.RowCount()>20001 || range.ColumnCount()!=8) throw new BusinessRuleException("template.file_shape_invalid","Use one sheet with exactly eight template columns and at most 20000 accounts.");
            foreach (var row in range.Rows())
            {
                if (row.Cells().Any(c=>c.HasFormula)) throw new BusinessRuleException("template.formula_not_allowed","Import values, not formulas.");
                rows.Add(row.Cells().Select(c=>c.GetFormattedString(CultureInfo.InvariantCulture)).ToArray());
            }
        }
        else throw new BusinessRuleException("template.file_type_invalid","Only UTF-8 CSV and XLSX are accepted.");
        string[] columns=["AccountCode","AccountNameArabic","AccountNameEnglish","ParentCode","AccountType","PostingAllowed","CurrencyMode","CostCenterPolicy"];
        if (rows.Count<2 || !rows[0].SequenceEqual(columns,StringComparer.OrdinalIgnoreCase)) throw new BusinessRuleException("template.header_invalid",string.Join(",",columns));
        var accounts=new List<TemplateAccount>();
        foreach(var row in rows.Skip(1))
        {
            if(row.Length!=8 || !Enum.TryParse<AccountType>(row[4],true,out var type) || !bool.TryParse(row[5],out var posting)
                || !Enum.TryParse<AccountCurrencyMode>(row[6],true,out var currency) || !Enum.TryParse<CostCenterPolicy>(row[7],true,out var center))
                throw new BusinessRuleException("template.row_invalid","Account type, boolean posting and policy columns must contain valid values.");
            accounts.Add(new(row[0].Trim(),row[1].Trim(),string.IsNullOrWhiteSpace(row[2])?null:row[2].Trim(),string.IsNullOrWhiteSpace(row[3])?null:row[3].Trim(),type,posting,currency,center));
        }
        return await PreviewAsync(scope,metadata with { Accounts=accounts },cancellationToken).ConfigureAwait(false);
    }
}


