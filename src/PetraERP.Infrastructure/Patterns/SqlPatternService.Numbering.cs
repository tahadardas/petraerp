using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Security;
using PetraERP.Domain.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Patterns;

public sealed partial class SqlPatternService
{
    private async Task<PatternNumberResultDto> ReserveDocumentNumberAsync(PatternScope scope,GetNextPatternNumberCommand command,
        DocumentPatternRow pattern,PatternRevisionRow revision,PatternNumberingPolicy policy,CancellationToken token)
    {
        await ValidateCompanyAndBranchAsync(scope.CompanyId,scope.BranchId,token).ConfigureAwait(false);
        if(!pattern.IsActive || pattern.BranchGrants.Count>0 && !pattern.BranchGrants.Any(g=>g.BranchId==scope.BranchId))
            throw new BusinessRuleException("patterns.numbering_scope_denied","The pattern is not available in the selected branch.");
        var yearRequired=policy.Scope is PatternNumberingScope.FiscalYear or PatternNumberingScope.BranchAndFiscalYear;
        var years=await context.FiscalYears.AsNoTracking().Where(y=>y.CompanyId==scope.CompanyId && (command.FiscalYearId==null || y.Id==command.FiscalYearId))
            .Take(2).ToListAsync(token).ConfigureAwait(false);
        var year=years.Count==1?years[0]:null;
        if((yearRequired || command.FiscalYearId is not null || policy.ResetPolicy==PatternNumberResetPolicy.Monthly)
            && (year is null || year.Id==Guid.Empty))
            throw new BusinessRuleException("patterns.fiscal_year_required","Select a valid fiscal year of the authorized workspace; no current-date fallback is allowed.");
        if(command.AccountingDate is {} date && (year is null || date<year.RangeStart || date>year.RangeEnd))
            throw new BusinessRuleException("patterns.numbering_date_invalid","The document date must belong to the selected fiscal year.");
        if(policy.ResetPolicy==PatternNumberResetPolicy.Monthly && command.AccountingDate is null)
            throw new BusinessRuleException("patterns.numbering_date_required","Monthly reset requires the actual accounting date.");
        var scopeYear=yearRequired?year!.Id:Guid.Empty;
        var scopeBranch=policy.Scope is PatternNumberingScope.Branch or PatternNumberingScope.BranchAndFiscalYear?scope.BranchId:Guid.Empty;
        var period=policy.ResetPolicy==PatternNumberResetPolicy.Monthly?command.AccountingDate!.Value.Year*100+command.AccountingDate.Value.Month:0;
        var key=command.AllocationKey ?? Guid.NewGuid();
        if(key==Guid.Empty) throw new BusinessRuleException("patterns.allocation_key_invalid","Allocation key cannot be empty.");
        var companyPolicy=await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,scope.CompanyId,token).ConfigureAwait(false);
        if(!companyPolicy.AllowPatternNumberingOverrides && ((int)policy.Scope!=companyPolicy.DefaultNumberingScope || policy.InitialSequence!=companyPolicy.InitialNumberSequence))
            throw new BusinessRuleException("patterns.company_numbering_policy","This pattern conflicts with the company's enforced numbering policy; publish an authorized revision.");
        var manual=string.IsNullOrWhiteSpace(command.ManualNumber)?null:command.ManualNumber.Trim();
        var authority=context.Identity;
        if(manual is not null && (!companyPolicy.AllowManualNumbering || !policy.AllowManualNumbering || await new SqlAccessEvaluator(authority)
            .EvaluatePermissionAsync(scope.ActorUserId,PermissionCatalog.Get(PermissionCatalog.PatternsManualNumbering),scope.CompanyId,scope.BranchId,token).ConfigureAwait(false)!=AccessDecision.Granted))
            throw new BusinessRuleException("patterns.manual_number_denied","Manual numbering requires both the published pattern option and its specific permission.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {scope.CompanyId,scope.BranchId,Pattern=pattern.Id,Revision=revision.Id,Year=year?.Id,command.AccountingDate,Manual=manual,ScopeYear=scopeYear,ScopeBranch=scopeBranch,Period=period})));
        var transaction=await OwnedDatabaseTransaction.BeginAsync(authority.Context,IsolationLevel.Serializable,token).ConfigureAwait(false);
        await using var lifetime=transaction.ConfigureAwait(false);
        var resource=$"PetraERP.DocumentNumbering:{scope.CompanyId:N}";
        await authority.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000; IF @r<0 THROW 51035,N'Number allocator is busy.',1;",token).ConfigureAwait(false);
        var reservations=authority.Set<DocumentNumberReservationRow>();
        var existing=await reservations.AsNoTracking().SingleOrDefaultAsync(r=>r.CompanyId==scope.CompanyId && r.AllocationKey==key,token).ConfigureAwait(false);
        if(existing is not null)
        {
            if(existing.Fingerprint!=fingerprint) throw new BusinessRuleException("patterns.numbering_key_conflict","Allocation key was already used with different numbering content.");
            return new PatternNumberResultDto {SequenceNumber=existing.Sequence,FormattedNumber=existing.Number};
        }
        var counters=authority.Set<DocumentNumberCounterRow>();
        var counter=await counters.SingleOrDefaultAsync(r=>r.CompanyId==scope.CompanyId && r.PatternId==pattern.Id && r.ScopeBranchId==scopeBranch && r.ScopeFiscalYearId==scopeYear && r.PeriodKey==period,token).ConfigureAwait(false);
        if(counter is null)
        {
            var floor=period==0?await LegacyCounterFloorAsync(scope.CompanyId,pattern.Id,scopeBranch,scopeYear,authority,token).ConfigureAwait(false):0;
            counter=new DocumentNumberCounterRow {CompanyId=scope.CompanyId,PatternId=pattern.Id,ScopeBranchId=scopeBranch,ScopeFiscalYearId=scopeYear,PeriodKey=period,LastSequence=Math.Max(floor,policy.InitialSequence-1)};
            counters.Add(counter);
        }
        var family=BusinessOperationCatalog.CategoryOf((BusinessOperationKind)pattern.OperationKind) switch
        {
            BusinessOperationCategory.Invoice => pattern.OperationKind is (int)BusinessOperationKind.PurchaseInvoice or (int)BusinessOperationKind.PurchaseReturn?2:1,
            BusinessOperationCategory.Voucher=>3,BusinessOperationCategory.Journal=>4,_=>5,
        };
        var priorDatabases = scopeYear == Guid.Empty
            ? await authority.FiscalDatabases.AsNoTracking().Where(r=>r.CompanyId==scope.CompanyId && r.State=="Active").Select(r=>r.DatabaseName).ToListAsync(token).ConfigureAwait(false)
            : [];
        var prefix=policy.Prefix.Replace("{yyyyMM}",period.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal);
        var suffix=policy.Suffix.Replace("{yyyyMM}",period.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal);
        string number;long sequence;
        if(manual is not null)
        {
            number=manual;sequence=0;
            if(number.Length>128 || number.Any(char.IsControl)) throw new BusinessRuleException("patterns.number_invalid","Document numbers are bounded printable text.");
            if(await reservations.AnyAsync(r=>r.CompanyId==scope.CompanyId && r.DocumentFamily==family && r.ScopeBranchId==scopeBranch && r.ScopeFiscalYearId==scopeYear && r.Number==number,token).ConfigureAwait(false)
                || await CurrentDocumentNumberExistsAsync(scope.CompanyId,scopeBranch,family,number,priorDatabases,token).ConfigureAwait(false))
                throw new BusinessRuleException("patterns.number_conflict","The requested document number is already in use.");
            if(number.StartsWith(prefix,StringComparison.Ordinal) && number.EndsWith(suffix,StringComparison.Ordinal)
                && number.Length>=prefix.Length+suffix.Length
                && long.TryParse(number.AsSpan(prefix.Length,number.Length-prefix.Length-suffix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out var entered))
                counter.LastSequence=Math.Max(counter.LastSequence,entered);
        }
        else
        {
            var attempts=0;
            do
            {
                if(counter.LastSequence==long.MaxValue || ++attempts>1000) throw new BusinessRuleException("patterns.numbering_exhausted","Choose a non-conflicting prefix or scope; automatic allocation is exhausted.");
                sequence=++counter.LastSequence;
                number=prefix+sequence.ToString(CultureInfo.InvariantCulture).PadLeft(policy.MinimumDigits,'0')+suffix;
            } while(await reservations.AnyAsync(r=>r.CompanyId==scope.CompanyId && r.DocumentFamily==family && r.ScopeBranchId==scopeBranch && r.ScopeFiscalYearId==scopeYear && r.Number==number,token).ConfigureAwait(false)
                || await CurrentDocumentNumberExistsAsync(scope.CompanyId,scopeBranch,family,number,priorDatabases,token).ConfigureAwait(false));
        }
        reservations.Add(new DocumentNumberReservationRow {CompanyId=scope.CompanyId,AllocationKey=key,PatternId=pattern.Id,RevisionId=revision.Id,
            BranchId=scope.BranchId,ScopeBranchId=scopeBranch,ScopeFiscalYearId=scopeYear,PeriodKey=period,DocumentFamily=family,
            RequestedManualNumber=manual,Number=number,Sequence=sequence,Fingerprint=fingerprint,ActorUserId=scope.ActorUserId,ReservedAtUtc=DateTimeOffset.UtcNow});
        await authority.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        // A central reservation is never recycled after a failed annual posting. Retrying the same document key reuses it.
        // This permits gaps but requires neither a cross-database transaction nor premature commitment of financial effects.
        return new PatternNumberResultDto {SequenceNumber=sequence,FormattedNumber=number};
    }

    /// <summary>
    /// Reads the number the pattern would issue next without reserving it, so a draft the operator abandons cannot burn a
    /// sequence. The value is advisory — a concurrent document can be allocated first — and saving still allocates
    /// authoritatively. Anything that makes automatic numbering impossible reports an unavailable estimate instead of
    /// throwing, because no part of a preview may block the document.
    /// </summary>
    public async Task<PatternNumberPreviewDto> PreviewNextNumberAsync(
        PatternScope scope,GetNextPatternNumberCommand command,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        DocumentPatternRow pattern;PatternNumberingPolicy policy;
        try
        {
            (pattern,_,policy)=await ResolveNumberingAsync(scope,command.PatternId,command.RevisionId,cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is BusinessRuleException or ResourceNotFoundException)
        {
            return PatternNumberPreviewDto.Unavailable("patterns.numbering_unavailable");
        }

        if(!pattern.IsActive || pattern.BranchGrants.Count>0 && !pattern.BranchGrants.Any(g=>g.BranchId==scope.BranchId))
            return PatternNumberPreviewDto.Unavailable("patterns.numbering_scope_denied");
        var companyPolicy=await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,scope.CompanyId,cancellationToken).ConfigureAwait(false);
        if(!companyPolicy.AllowPatternNumberingOverrides && ((int)policy.Scope!=companyPolicy.DefaultNumberingScope || policy.InitialSequence!=companyPolicy.InitialNumberSequence))
            return PatternNumberPreviewDto.Unavailable("patterns.company_numbering_policy");
        var yearRequired=policy.Scope is PatternNumberingScope.FiscalYear or PatternNumberingScope.BranchAndFiscalYear;
        // A draft card knows its date but not the internal year id, so the date picks the year when no id was sent;
        // an explicit id still wins, and a company with a single year needs neither.
        var accountingDate=command.AccountingDate;
        var years=await context.FiscalYears.AsNoTracking().Where(y=>y.CompanyId==scope.CompanyId && (command.FiscalYearId==null || y.Id==command.FiscalYearId)
            && (command.FiscalYearId!=null || accountingDate==null || y.RangeStart<=accountingDate && y.RangeEnd>=accountingDate))
            .Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        var year=years.Count==1?years[0]:null;
        if((yearRequired || command.FiscalYearId is not null || policy.ResetPolicy==PatternNumberResetPolicy.Monthly)
            && (year is null || year.Id==Guid.Empty))
            return PatternNumberPreviewDto.Unavailable("patterns.fiscal_year_required");
        if(command.AccountingDate is {} dated && (year is null || dated<year.RangeStart || dated>year.RangeEnd))
            return PatternNumberPreviewDto.Unavailable("patterns.numbering_date_invalid");
        if(policy.ResetPolicy==PatternNumberResetPolicy.Monthly && command.AccountingDate is null)
            return PatternNumberPreviewDto.Unavailable("patterns.numbering_date_required");
        var scopeYear=yearRequired?year!.Id:Guid.Empty;
        var scopeBranch=policy.Scope is PatternNumberingScope.Branch or PatternNumberingScope.BranchAndFiscalYear?scope.BranchId:Guid.Empty;
        var period=policy.ResetPolicy==PatternNumberResetPolicy.Monthly?command.AccountingDate!.Value.Year*100+command.AccountingDate.Value.Month:0;
        var authority=context.Identity;
        var counter=await authority.Set<DocumentNumberCounterRow>().AsNoTracking().SingleOrDefaultAsync(r=>r.CompanyId==scope.CompanyId && r.PatternId==pattern.Id
            && r.ScopeBranchId==scopeBranch && r.ScopeFiscalYearId==scopeYear && r.PeriodKey==period,cancellationToken).ConfigureAwait(false);
        var lastSequence=counter?.LastSequence
            ?? Math.Max(await LegacyCounterFloorAsync(scope.CompanyId,pattern.Id,scopeBranch,scopeYear,authority,cancellationToken).ConfigureAwait(false),policy.InitialSequence-1);
        if(lastSequence==long.MaxValue)
            return PatternNumberPreviewDto.Unavailable("patterns.numbering_exhausted");
        var prefix=policy.Prefix.Replace("{yyyyMM}",period.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal);
        var suffix=policy.Suffix.Replace("{yyyyMM}",period.ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal);
        var sequence=lastSequence+1;
        return new PatternNumberPreviewDto {IsAvailable=true,SequenceNumber=sequence,
            FormattedNumber=prefix+sequence.ToString(CultureInfo.InvariantCulture).PadLeft(policy.MinimumDigits,'0')+suffix};
    }

    internal static Guid NumberAllocationKey(string purpose,Guid documentId,string requestKey) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Number:{purpose}:{documentId:D}:{requestKey}"))[..16]);

    internal async Task VerifyManualReplayAsync(Guid company,Guid key,string? manual,CancellationToken token)
    {
        var receipt=await context.Identity.Set<DocumentNumberReservationRow>().AsNoTracking().SingleOrDefaultAsync(r=>r.CompanyId==company && r.AllocationKey==key,token).ConfigureAwait(false);
        var requested=string.IsNullOrWhiteSpace(manual)?null:manual.Trim();
        if(receipt?.RequestedManualNumber!=requested) throw new BusinessRuleException("patterns.numbering_key_conflict","The completed document used different manual-number content.");
    }

    private async Task<bool> CurrentDocumentNumberExistsAsync(Guid company,Guid branch,int family,string number,IReadOnlyList<string> priorDatabases,CancellationToken token)
    {
        var local = family switch
        {
            1=>await context.SalesInvoices.AsNoTracking().AnyAsync(i=>i.CompanyId==company && (branch==Guid.Empty || i.BranchId==branch) && i.InvoiceNumber==number,token).ConfigureAwait(false),
            2=>await context.PurchaseInvoices.AsNoTracking().AnyAsync(i=>i.CompanyId==company && (branch==Guid.Empty || i.BranchId==branch) && i.InvoiceNumber==number,token).ConfigureAwait(false),
            3=>await context.Vouchers.AsNoTracking().AnyAsync(i=>i.CompanyId==company && (branch==Guid.Empty || i.BranchId==branch) && i.VoucherNumber==number,token).ConfigureAwait(false),
            _=>false,
        };
        if(local || family is not (1 or 2 or 3)) return local;
        var current=new SqlConnectionStringBuilder(context.Database.GetConnectionString()).InitialCatalog;
        foreach(var name in priorDatabases.Where(n=>!n.Equals(current,StringComparison.OrdinalIgnoreCase)))
        {
            using var connection=new SqlConnection(new SqlConnectionStringBuilder(context.Database.GetConnectionString()){InitialCatalog=name}.ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command=connection.CreateCommand();
            // Identifiers come exclusively from this fixed family switch; all values remain SQL parameters.
            var table=family==1?"[sales].[SalesInvoices]":family==2?"[purchases].[PurchaseInvoices]":"[treasury].[Vouchers]";
            var column=family==3?"[VoucherNumber]":"[InvoiceNumber]";
            command.CommandText="SELECT TOP(1) 1 FROM "+table+" WHERE [CompanyId]=@c AND (@b='00000000-0000-0000-0000-000000000000' OR [BranchId]=@b) AND "+column+"=@n;";
            command.Parameters.AddWithValue("@c",company);command.Parameters.AddWithValue("@b",branch);command.Parameters.AddWithValue("@n",number);
            if(await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not null) return true;
        }
        return false;
    }

    private async Task<long> LegacyCounterFloorAsync(Guid company,Guid pattern,Guid branch,Guid fiscalYear,CentralDataContext central,CancellationToken token)
    {
        var floor=await context.PatternNumberSequences.AsNoTracking().Where(r=>r.CompanyId==company && r.PatternId==pattern && r.BranchId==branch && r.FiscalYearId==fiscalYear)
            .Select(r=>(long?)r.LastSequence).SingleOrDefaultAsync(token).ConfigureAwait(false) ?? 0;
        if(fiscalYear!=Guid.Empty) return floor;
        // One-time adoption of explicit prior allocator watermarks, never MAX(document number)+1.
        // Prior-version posting processes must be stopped before deploying this allocator.
        var names=await central.FiscalDatabases.AsNoTracking().Where(r=>r.CompanyId==company && r.State=="Active").Select(r=>r.DatabaseName).ToListAsync(token).ConfigureAwait(false);
        var current=new SqlConnectionStringBuilder(context.Database.GetConnectionString()).InitialCatalog;
        foreach(var name in names.Where(n=>!n.Equals(current,StringComparison.OrdinalIgnoreCase)))
        {
            using var connection=new SqlConnection(new SqlConnectionStringBuilder(context.Database.GetConnectionString()){InitialCatalog=name}.ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command=connection.CreateCommand();
            command.CommandText="SELECT [LastSequence] FROM [patterns].[PatternNumberSequences] WHERE [CompanyId]=@c AND [PatternId]=@p AND [BranchId]=@b AND [FiscalYearId]=@y;";
            command.Parameters.AddWithValue("@c",company);command.Parameters.AddWithValue("@p",pattern);command.Parameters.AddWithValue("@b",branch);command.Parameters.AddWithValue("@y",Guid.Empty);
            if(await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long previous) floor=Math.Max(floor,previous);
        }
        return floor;
    }
}
