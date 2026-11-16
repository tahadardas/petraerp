using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

public sealed class SqlCommercialPaperService : ICommercialPaperService
{
    private readonly FinancialDbContext context;
    private readonly TimeProvider timeProvider;

    public SqlCommercialPaperService(FinancialDbContext context, TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
    }

    public async Task<CommercialPaperResult> RegisterAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        RegisterCommercialPaperCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.PaperNumber))
            throw new RequestValidationException("treasury.paper_number_required", "Paper number is required.", nameof(command.PaperNumber));
        if (string.IsNullOrWhiteSpace(command.BankName))
            throw new RequestValidationException("treasury.bank_name_required", "Bank name is required.", nameof(command.BankName));
        if (command.Amount <= 0)
            throw new RequestValidationException("treasury.amount_invalid", "Amount must be strictly positive.", nameof(command.Amount));
        // Same resolution as every financial document: enabled currency only, base = 1, else explicit or register rate on the issue date.
        var partyCurrency = command.PartyId is { } partyId
            ? await context.Parties.AsNoTracking().Where(p => p.Id == partyId && p.CompanyId == companyId).Select(p => p.DefaultCurrencyCode)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var currency = await CompanyCurrencies.ResolveAsync(context, companyId, "treasury", command.CurrencyCode, command.ExchangeRate, command.IssueDate,
            partyCurrency, null, cancellationToken).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var baseAmount = Math.Round(command.Amount * currency.Rate, 4, MidpointRounding.ToEven);

        var row = new CommercialPaperRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = branchId,
            PaperKind = (byte)command.PaperKind,
            PaperNumber = command.PaperNumber.Trim(),
            BankName = command.BankName.Trim(),
            BranchName = command.BranchName?.Trim(),
            DrawerName = command.DrawerName.Trim(),
            PayeeName = command.PayeeName?.Trim(),
            IssueDate = command.IssueDate,
            DueDate = command.DueDate,
            Amount = command.Amount,
            CurrencyCode = currency.CurrencyCode,
            ExchangeRate = currency.Rate,
            BaseAmount = baseAmount,
            Status = (byte)CommercialPaperStatus.Received,
            StatusDate = command.IssueDate,
            PartyId = command.PartyId,
            PortfolioAccountId = command.PortfolioAccountId,
            Notes = command.Notes?.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.CommercialPapers.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetByIdAsync(companyId, branchId, row.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommercialPaperResult> TransitionStatusAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        Guid paperId,
        TransitionCommercialPaperStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var paper = await context.CommercialPapers
            .Include(p => p.Party)
            .Include(p => p.PortfolioAccount)
            .Include(p => p.IntermediaryAccount)
            .Include(p => p.BankAccount)
            .SingleOrDefaultAsync(p => p.Id == paperId && p.CompanyId == companyId && p.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (paper is null)
            throw new BusinessRuleException("treasury.paper_not_found", "Commercial paper not found.");

        if (paper.Status == (byte)CommercialPaperStatus.Cancelled)
            throw new BusinessRuleException("treasury.paper_already_cancelled", "Cancelled paper cannot change state.");

        var oldStatus = (CommercialPaperStatus)paper.Status;
        var newStatus = command.NewStatus;
        if (oldStatus == newStatus)
            return MapToResult(paper, DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

        var now = timeProvider.GetUtcNow();
        var date = command.TransitionDate != default ? command.TransitionDate : DateOnly.FromDateTime(now.DateTime);

        // State Machine & Accounting Posting
        Guid? createdJournalId = null;

        if (paper.PaperKind == (byte)CommercialPaperKind.ChequeReceived)
        {
            if (newStatus == CommercialPaperStatus.UnderCollection)
            {
                // Move from Portfolio (أوراق قبض) to Intermediary (شيكات برسم التحصيل)
                var intermediaryAccountId = command.TargetAccountId ?? paper.IntermediaryAccountId;
                var portfolioAccountId = paper.PortfolioAccountId;
                if (intermediaryAccountId.HasValue && portfolioAccountId.HasValue)
                {
                    createdJournalId = await PostTransitionJournalAsync(
                        companyId, branchId, userId, paper, date,
                        $"إيداع شيك برسم التحصيل رقم {paper.PaperNumber} — {paper.BankName}",
                        intermediaryAccountId.Value, portfolioAccountId.Value, newStatus, cancellationToken).ConfigureAwait(false);
                }
                paper.IntermediaryAccountId = intermediaryAccountId;
            }
            else if (newStatus == CommercialPaperStatus.Cleared)
            {
                // Move from Intermediary (شيكات برسم التحصيل) to Bank (الحساب البنكي)
                var bankAccountId = command.TargetAccountId ?? paper.BankAccountId;
                var sourceAccountId = paper.IntermediaryAccountId ?? paper.PortfolioAccountId;
                if (bankAccountId.HasValue && sourceAccountId.HasValue)
                {
                    createdJournalId = await PostTransitionJournalAsync(
                        companyId, branchId, userId, paper, date,
                        $"تحصيل شيك رقم {paper.PaperNumber} في البنك — {paper.BankName}",
                        bankAccountId.Value, sourceAccountId.Value, newStatus, cancellationToken).ConfigureAwait(false);
                }
                paper.BankAccountId = bankAccountId;
            }
            else if (newStatus == CommercialPaperStatus.Bounced)
            {
                // Bounced: Return to Customer control account from Intermediary/Portfolio
                var sourceAccountId = paper.IntermediaryAccountId ?? paper.PortfolioAccountId;
                if (paper.PartyId.HasValue && sourceAccountId.HasValue)
                {
                    var party = await context.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paper.PartyId.Value, cancellationToken).ConfigureAwait(false);
                    var controlAccountId = party?.ReceivableAccountId ?? party?.PayableAccountId;
                    if (controlAccountId.HasValue)
                    {
                        createdJournalId = await PostTransitionJournalAsync(
                            companyId, branchId, userId, paper, date,
                            $"شيك مرتجع رقم {paper.PaperNumber} — {party?.NameArabic}",
                            controlAccountId.Value, sourceAccountId.Value, newStatus, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (newStatus == CommercialPaperStatus.Endorsed)
            {
                // Endorsed: Debit supplier, credit portfolio
                var sourceAccountId = paper.PortfolioAccountId;
                if (command.EndorsedPartyId.HasValue && sourceAccountId.HasValue)
                {
                    var party = await context.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.EndorsedPartyId.Value, cancellationToken).ConfigureAwait(false);
                    var controlAccountId = party?.PayableAccountId ?? party?.ReceivableAccountId;
                    if (controlAccountId.HasValue)
                    {
                        createdJournalId = await PostTransitionJournalAsync(
                            companyId, branchId, userId, paper, date,
                            $"تظهير شيك رقم {paper.PaperNumber} لصالح {party?.NameArabic}",
                            controlAccountId.Value, sourceAccountId.Value, newStatus, cancellationToken).ConfigureAwait(false);
                    }
                    paper.EndorsedPartyId = command.EndorsedPartyId;
                }
            }
        }
        else if (paper.PaperKind == (byte)CommercialPaperKind.ChequeIssued)
        {
            if (newStatus == CommercialPaperStatus.Cleared)
            {
                // Issued Cheque Cleared: Debit Payable intermediary, Credit Bank
                var bankAccountId = command.TargetAccountId ?? paper.BankAccountId;
                var sourceAccountId = paper.PortfolioAccountId ?? paper.IntermediaryAccountId;
                if (bankAccountId.HasValue && sourceAccountId.HasValue)
                {
                    createdJournalId = await PostTransitionJournalAsync(
                        companyId, branchId, userId, paper, date,
                        $"صرف شيك مسحوب رقم {paper.PaperNumber} من البنك",
                        sourceAccountId.Value, bankAccountId.Value, newStatus, cancellationToken).ConfigureAwait(false);
                }
                paper.BankAccountId = bankAccountId;
            }
        }

        paper.Status = (byte)newStatus;
        paper.StatusDate = date;
        paper.UpdatedAtUtc = now;
        if (createdJournalId.HasValue)
        {
            paper.LastJournalEntryId = createdJournalId;
        }
        if (!string.IsNullOrWhiteSpace(command.Reason))
        {
            paper.Notes = string.IsNullOrWhiteSpace(paper.Notes)
                ? $"[{date:yyyy-MM-dd}] {command.Reason}"
                : $"{paper.Notes}\n[{date:yyyy-MM-dd}] {command.Reason}";
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetByIdAsync(companyId, branchId, paper.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommercialPaperResult>> ListAsync(
        Guid companyId,
        Guid branchId,
        CommercialPaperSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = context.CommercialPapers
            .AsNoTracking()
            .Include(p => p.Party)
            .Include(p => p.EndorsedParty)
            .Where(p => p.CompanyId == companyId && p.BranchId == branchId);

        if (query.Status.HasValue)
            q = q.Where(p => p.Status == (byte)query.Status.Value);
        if (query.Kind.HasValue)
            q = q.Where(p => p.PaperKind == (byte)query.Kind.Value);
        if (query.PartyId.HasValue)
            q = q.Where(p => p.PartyId == query.PartyId.Value);
        if (query.FromDueDate.HasValue)
            q = q.Where(p => p.DueDate >= query.FromDueDate.Value);
        if (query.ToDueDate.HasValue)
            q = q.Where(p => p.DueDate <= query.ToDueDate.Value);
        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var text = query.Query.Trim();
            q = q.Where(p => p.PaperNumber.Contains(text) || p.BankName.Contains(text) || p.DrawerName.Contains(text));
        }

        var items = await q.OrderBy(p => p.DueDate).ThenBy(p => p.PaperNumber).ToListAsync(cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        return items.Select(item => MapToResult(item, today)).ToList();
    }

    public async Task<CommercialPaperAlertsResult> GetAlertsAsync(
        Guid companyId,
        Guid branchId,
        int daysAhead = 7,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var cutoff = today.AddDays(daysAhead);

        var openPapers = await context.CommercialPapers
            .AsNoTracking()
            .Include(p => p.Party)
            .Include(p => p.EndorsedParty)
            .Where(p => p.CompanyId == companyId && p.BranchId == branchId
                && (p.Status == (byte)CommercialPaperStatus.Received || p.Status == (byte)CommercialPaperStatus.UnderCollection)
                && p.DueDate <= cutoff)
            .OrderBy(p => p.DueDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = openPapers.Select(p => MapToResult(p, today)).ToList();
        return new CommercialPaperAlertsResult
        {
            OverdueCount = mapped.Count(p => p.Severity == CommercialPaperAlertSeverity.Overdue),
            DueTodayCount = mapped.Count(p => p.Severity == CommercialPaperAlertSeverity.DueToday),
            DueSoonCount = mapped.Count(p => p.Severity == CommercialPaperAlertSeverity.DueSoon),
            Alerts = mapped
        };
    }

    public async Task<CommercialPaperResult> GetByIdAsync(
        Guid companyId,
        Guid branchId,
        Guid paperId,
        CancellationToken cancellationToken = default)
    {
        var paper = await context.CommercialPapers
            .AsNoTracking()
            .Include(p => p.Party)
            .Include(p => p.EndorsedParty)
            .SingleOrDefaultAsync(p => p.Id == paperId && p.CompanyId == companyId && p.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false);

        if (paper is null)
            throw new BusinessRuleException("treasury.paper_not_found", "Commercial paper not found.");

        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return MapToResult(paper, today);
    }

    private async Task<Guid> PostTransitionJournalAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        CommercialPaperRow paper,
        DateOnly accountingDate,
        string description,
        Guid debitAccountId,
        Guid creditAccountId,
        CommercialPaperStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var journalService = new SqlJournalEntryService(context, timeProvider);
        var journalScope = JournalEntryScope.Create(companyId, branchId, userId);

        var draft = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
        {
            AccountingDate = accountingDate,
            Description = description,
            Lines =
            [
                new JournalDraftLineInput
                {
                    AccountId = debitAccountId,
                    TransactionCurrencyCode = paper.CurrencyCode,
                    TransactionDebit = paper.Amount,
                    TransactionCredit = 0m,
                    RateValue = paper.ExchangeRate,
                    BaseDebit = paper.BaseAmount,
                    BaseCredit = 0m,
                    Description = description
                },
                new JournalDraftLineInput
                {
                    AccountId = creditAccountId,
                    TransactionCurrencyCode = paper.CurrencyCode,
                    TransactionDebit = 0m,
                    TransactionCredit = paper.Amount,
                    RateValue = paper.ExchangeRate,
                    BaseDebit = 0m,
                    BaseCredit = paper.BaseAmount,
                    Description = description
                }
            ]
        }, cancellationToken).ConfigureAwait(false);

        var validated = await journalService.ValidateDraftAsync(journalScope, draft.Id, new JournalDraftActionCommand
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(draft.VersionBase64)),
            Reason = description
        }, cancellationToken).ConfigureAwait(false);

        var approved = await journalService.ApproveDraftAsync(journalScope, draft.Id, new JournalDraftActionCommand
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)),
            Reason = description
        }, cancellationToken).ConfigureAwait(false);

        var postingKey = new Guid(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"PetraERP.Treasury.CommercialPaper:{paper.Id:N}:{(byte)targetStatus}"))[..16]);

        var postingRequest = PostingRequest.Post(
            PostingRequestScope.Create(companyId, branchId, userId),
            postingKey,
            approved.Id,
            ExpectedJournalVersion.FromBase64(approved.VersionBase64),
            AuditReason.Create(description));

        var postingService = new SqlPostingService(context);
        await postingService.PostAsync(postingRequest, cancellationToken).ConfigureAwait(false);
        return approved.Id;
    }

    private static CommercialPaperResult MapToResult(CommercialPaperRow paper, DateOnly today)
    {
        var status = (CommercialPaperStatus)paper.Status;
        var severity = CommercialPaperAlertSeverity.Normal;
        if (status == CommercialPaperStatus.Received || status == CommercialPaperStatus.UnderCollection)
        {
            if (paper.DueDate < today)
                severity = CommercialPaperAlertSeverity.Overdue;
            else if (paper.DueDate == today)
                severity = CommercialPaperAlertSeverity.DueToday;
            else if (paper.DueDate <= today.AddDays(7))
                severity = CommercialPaperAlertSeverity.DueSoon;
        }

        return new CommercialPaperResult
        {
            Id = paper.Id,
            CompanyId = paper.CompanyId,
            BranchId = paper.BranchId,
            PaperKind = (CommercialPaperKind)paper.PaperKind,
            PaperNumber = paper.PaperNumber,
            BankName = paper.BankName,
            BranchName = paper.BranchName,
            DrawerName = paper.DrawerName,
            PayeeName = paper.PayeeName,
            IssueDate = paper.IssueDate,
            DueDate = paper.DueDate,
            Amount = paper.Amount,
            CurrencyCode = paper.CurrencyCode,
            ExchangeRate = paper.ExchangeRate,
            BaseAmount = paper.BaseAmount,
            Status = status,
            PartyId = paper.PartyId,
            PartyName = paper.Party?.NameArabic,
            IntermediaryAccountId = paper.IntermediaryAccountId,
            BankAccountId = paper.BankAccountId,
            EndorsedPartyId = paper.EndorsedPartyId,
            EndorsedPartyName = paper.EndorsedParty?.NameArabic,
            VoucherId = paper.VoucherId,
            LastJournalEntryId = paper.LastJournalEntryId,
            StatusDate = paper.StatusDate,
            Notes = paper.Notes,
            Severity = severity
        };
    }
}
