using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Sales;

/// <summary>One document-level discount or addition as the validator sees it, whichever document it belongs to.</summary>
internal sealed record AdjustmentInput(SalesInvoiceAdjustmentKind Kind, Guid AccountId, Guid? CounterAccountId, decimal Amount, decimal Percentage, Guid? CostCenterId);

/// <summary>
/// The rules shared by the sales and purchase discounts-and-additions tables: a row names an active posting account that accepts the
/// invoice currency and respects its cost-center policy; an optional counter account books the row between two accounts outside the
/// party's balance. Error codes carry the document prefix (<c>sales.</c> / <c>purchases.</c>).
/// </summary>
internal static class InvoiceAdjustmentRules
{
    public const int MaxAdjustments = 50;

    public static async Task ValidateAsync(FinancialDbContext context, Guid companyId, string invoiceCurrency, string prefix,
        IReadOnlyList<AdjustmentInput> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;
        if (rows.Count > MaxAdjustments)
            throw new BusinessRuleException($"{prefix}.adjustments_too_many", $"An invoice carries at most {MaxAdjustments} discount or addition rows.", "adjustments");
        foreach (var row in rows)
        {
            if (!Enum.IsDefined(row.Kind))
                throw new BusinessRuleException($"{prefix}.adjustment_kind_invalid", "The adjustment must be a discount or an addition.", "adjustments");
            if (row.Amount <= 0m || decimal.Round(row.Amount, 4) != row.Amount)
                throw new BusinessRuleException($"{prefix}.adjustment_amount_invalid", "An adjustment amount must be positive with at most four decimals.", "adjustments");
            if (row.Percentage < 0m || row.Percentage > 100m || decimal.Round(row.Percentage, 4) != row.Percentage)
                throw new BusinessRuleException($"{prefix}.adjustment_percentage_invalid", "An adjustment percentage must be between 0 and 100.", "adjustments");
            if (row.AccountId == Guid.Empty)
                throw new BusinessRuleException($"{prefix}.adjustment_account_required", "Every adjustment needs an account.", "adjustments");
            if (row.CounterAccountId == row.AccountId)
                throw new BusinessRuleException($"{prefix}.adjustment_counter_account_invalid", "The counter account must differ from the adjustment account.", "adjustments");
        }

        var accountIds = rows.Select(r => r.AccountId).Concat(rows.Where(r => r.CounterAccountId.HasValue).Select(r => r.CounterAccountId!.Value)).Distinct().ToArray();
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);
        var baseCurrency = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        var centerIds = rows.Where(r => r.CostCenterId.HasValue).Select(r => r.CostCenterId!.Value).Distinct().ToArray();
        if (centerIds.Length > 0)
        {
            var known = await context.CostCenters.AsNoTracking().CountAsync(c => centerIds.Contains(c.Id) && c.CompanyId == companyId && c.IsActive, cancellationToken).ConfigureAwait(false);
            if (known != centerIds.Length)
                throw new BusinessRuleException($"{prefix}.cost_center_invalid", "Every cost center must be active in the invoice company.", "adjustments");
        }

        bool CurrencyRejected(AccountRow account) =>
            account.CurrencyMode == (byte)AccountCurrencyMode.BaseCurrencyOnly && !string.Equals(invoiceCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase)
            || account.CurrencyMode == (byte)AccountCurrencyMode.FixedCurrency && !string.Equals(account.FixedCurrencyCode, invoiceCurrency, StringComparison.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!accounts.TryGetValue(row.AccountId, out var account) || !account.IsActive || !account.PostingAllowed)
                throw new BusinessRuleException($"{prefix}.adjustment_account_invalid", "The adjustment account must be an active account that accepts postings.", "adjustments");
            if (account.CostCenterPolicy == (byte)CostCenterPolicy.NotAllowed && row.CostCenterId.HasValue)
                throw new BusinessRuleException($"{prefix}.adjustment_cost_center_not_allowed", "The adjustment account does not accept a cost center.", "adjustments");
            if (account.CostCenterPolicy == (byte)CostCenterPolicy.Required && !row.CostCenterId.HasValue)
                throw new BusinessRuleException($"{prefix}.adjustment_cost_center_required", "The adjustment account requires a cost center.", "adjustments");
            if (CurrencyRejected(account))
                throw new BusinessRuleException($"{prefix}.adjustment_currency_invalid", "The adjustment account does not accept the invoice currency.", "adjustments");
            if (row.CounterAccountId is not { } counterId) continue;
            // The counter line carries no cost center, so an account that requires one cannot be a counter account.
            if (!accounts.TryGetValue(counterId, out var counter) || !counter.IsActive || !counter.PostingAllowed
                || counter.CostCenterPolicy == (byte)CostCenterPolicy.Required || CurrencyRejected(counter))
                throw new BusinessRuleException($"{prefix}.adjustment_counter_account_invalid",
                    "The counter account must be an active posting account that accepts the invoice currency and needs no cost center.", "adjustments");
        }
    }
}
