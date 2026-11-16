using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.GeneralLedger;

public sealed partial class GeneralLedgerViewModel
{
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (AccountingDate == default)
            errors.Add(localization["D_DateTime"] + ": " + localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(JournalDescription))
            errors.Add(localization["D_DescriptionRequired"]);
        var completeLines = Lines.Where(line => line.IsComplete).ToList();
        if (completeLines.Count < 2)
            errors.Add(localization["D_JournalMustHaveAtLeastTwoLines"]);
        Controls.DocumentEntryValidation.AddIncompleteLineErrors(errors, key => localization[key], "D_JournalLines", Lines);
        if (!IsBalanced)
            errors.Add(localization["D_JournalNotBalanced"]);
        return errors;
    }

    private async Task SaveDraftAsync()
    {
        var token = context.Token;
        if (!CanEditDraft || operation.IsBusy) return;

        var errors = GetValidationErrors();
        if (errors.Count > 0 || HasInputErrors || !IsPreviewValid)
        {
            ValidationErrors = errors;
            ValidationMessage = errors.Count > 0 ? string.Join("\n", errors) : localization["D_JournalNumericValidation"];
            return;
        }
        ValidationErrors = [];
        ValidationMessage = string.Empty;

        var completeLines = Lines.Where(line => line.IsComplete).ToList();

        await RunAsync(OperationKind.Save, async () =>
        {
            var lineDtos = completeLines.Select(line => new JournalLineDraftDto
            {
                AccountId = line.AccountId,
                CostCenterId = line.CostCenterId,
                TransactionCurrencyCode = line.CurrencyCode,
                TransactionDebit = FormatMoney(line.Debit),
                TransactionCredit = FormatMoney(line.Credit),
                RateValue = FormatRate(line.Rate),
                RateSourceCurrencyCode = line.CurrencyCode,
                RateTargetCurrencyCode = BaseCurrencyCode,
                BaseDebit = FormatMoney(line.BaseDebit),
                BaseCredit = FormatMoney(line.BaseCredit),
                Rounding = 0,
                Description = string.IsNullOrWhiteSpace(line.Description) ? JournalDescription : line.Description,
            }).ToList();

            if (IsNewDraft)
            {
                var request = new CreateJournalDraftRequest
                {
                    AccountingDate = AccountingDate,
                    Description = JournalDescription.Trim(),
                    Lines = lineDtos,
                };
                var created = await client.CreateDraftAsync(currentCompanyId, currentBranchId, request, token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                DisplayJournal(created);
            }
            else if (currentJournal is not null)
            {
                var request = new ReviseJournalDraftRequest
                {
                    ExpectedVersion = currentJournal.Version,
                    AccountingDate = AccountingDate,
                    Description = JournalDescription.Trim(),
                    Lines = lineDtos,
                    Reason = string.IsNullOrWhiteSpace(ActionReason) ? localization["D_ReasonRevision"] : ActionReason.Trim(),
                };
                var revised = await client.ReviseDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, request, token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                DisplayJournal(revised);
            }

            await LoadJournalsAsync(Page, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ValidateDraftAsync()
    {
        var token = context.Token;
        if (currentJournal is null || !CanValidateDraft || operation.IsBusy) return;
        await RunAsync(OperationKind.Save, async () =>
        {
            var request = new JournalActionRequest
            {
                ExpectedVersion = currentJournal.Version,
                Reason = string.IsNullOrWhiteSpace(ActionReason) ? localization["D_ReasonValidation"] : ActionReason.Trim(),
            };
            var validated = await client.ValidateDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, request, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            DisplayJournal(validated);
            await LoadJournalsAsync(Page, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ApproveDraftAsync()
    {
        var token = context.Token;
        if (currentJournal is null || !CanApproveDraft || operation.IsBusy) return;
        await RunAsync(OperationKind.Save, async () =>
        {
            var request = new JournalActionRequest
            {
                ExpectedVersion = currentJournal.Version,
                Reason = string.IsNullOrWhiteSpace(ActionReason) ? localization["D_ReasonApproval"] : ActionReason.Trim(),
            };
            var approved = await client.ApproveDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, request, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            DisplayJournal(approved);
            await LoadJournalsAsync(Page, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ReturnToDraftAsync()
    {
        var token = context.Token;
        if (currentJournal is null || !CanReturnToDraft || operation.IsBusy) return;
        await RunAsync(OperationKind.Save, async () =>
        {
            var request = new JournalActionRequest
            {
                ExpectedVersion = currentJournal.Version,
                Reason = string.IsNullOrWhiteSpace(ActionReason) ? localization["D_ReasonReturn"] : ActionReason.Trim(),
            };
            var draft = await client.ReturnToDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, request, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            DisplayJournal(draft);
            await LoadJournalsAsync(Page, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task CreateReversalAsync()
    {
        var token = context.Token;
        if (currentJournal is null || !CanCreateReversal || !HasActionReason || operation.IsBusy) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmCreateReversal").ConfigureAwait(true)) return;
        if (token.IsCancellationRequested) return;
        await RunAsync(OperationKind.Save, async () =>
        {
            var request = new CreateReversalDraftRequest
            {
                OriginalJournalId = currentJournal.Id,
                OriginalExpectedVersion = currentJournal.Version,
                AccountingDate = DateOnly.FromDateTime(DateTime.Today),
                Reason = ActionReason.Trim(),
            };
            var reversalDraft = await client.CreateReversalDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, request, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            DisplayJournal(reversalDraft);
            await LoadJournalsAsync(1, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task DeleteDraftAsync()
    {
        var token = context.Token;
        if (currentJournal is null || !CanDeleteDraft || operation.IsBusy) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmDeleteDraft").ConfigureAwait(true)) return;

        if (token.IsCancellationRequested) return;
        await RunAsync(OperationKind.Save, async () =>
        {
            await client.DeleteDraftAsync(currentCompanyId, currentBranchId, currentJournal.Id, currentJournal.Version, token).ConfigureAwait(true);
            IsEditorOpen = false;
            currentJournal = null;
            await LoadJournalsAsync(Page, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

}
