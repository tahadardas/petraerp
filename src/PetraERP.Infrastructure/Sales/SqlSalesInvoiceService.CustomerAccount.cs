using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Parties;

namespace PetraERP.Infrastructure.Sales;

/// <summary>
/// The invoice-level account ("حساب العميل"): the account that carries the invoice total instead of the default one. It is an
/// authorized document override in the posting-rule order, so it is accepted only where the invoice pattern permits it for the
/// role (Cash for a cash invoice, Receivable for a credit invoice). Party balances are unaffected: they derive from the source
/// document, and settlements meet the invoice on the account it was actually posted to.
/// </summary>
public sealed partial class SqlSalesInvoiceService
{
    /// <summary>
    /// Validates a requested account and returns what to store: null when nothing was asked or the request equals the default
    /// account (no override is needed then), otherwise the account id. Refused early with a stable code; posting decides again.
    /// </summary>
    private async Task<Guid?> ResolveCustomerAccountOverrideAsync(Guid companyId, Guid? requestedAccountId, PaymentMethod method, PartyRow customer,
        string? patternConfigurationJson, CancellationToken cancellationToken)
    {
        if (requestedAccountId is not { } accountId) return null;
        if (accountId == Guid.Empty)
            throw new BusinessRuleException("sales.customer_account_invalid", "حساب العميل غير صالح / The invoice account is invalid.", "customerAccountId");

        var config = CanonicalSalesRoles(string.IsNullOrWhiteSpace(patternConfigurationJson)
            ? new PatternConfiguration()
            : JsonSerializer.Deserialize<PatternConfiguration>(patternConfigurationJson) ?? new PatternConfiguration());
        var cash = method == PaymentMethod.Cash;
        var role = cash ? "Cash" : "Receivable";
        var defaultAccountId = cash
            ? config.AccountMappings.FirstOrDefault(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase))?.AccountId
            : customer.ReceivableAccountId;
        if (defaultAccountId == accountId) return null;

        var account = await context.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && a.CompanyId == companyId)
            .Select(a => new { a.IsActive, a.PostingAllowed })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("sales.customer_account_invalid",
                "حساب العميل غير موجود في هذه الشركة / The invoice account does not exist in this company.", "customerAccountId");
        if (!account.IsActive || !account.PostingAllowed)
            throw new BusinessRuleException("sales.customer_account_not_postable",
                "حساب العميل يجب أن يكون حسابًا فعالًا يقبل الترحيل / The invoice account must be an active posting account.", "customerAccountId");
        if (!PostingRuleResolver.AllowsDocumentOverride(role, config))
            throw new BusinessRuleException("sales.customer_account_override_not_allowed", cash
                ? "نمط الفاتورة لا يسمح بتغيير حساب الصندوق من الفاتورة؛ ألغِ «إلزامي» عن حساب الصندوق في النمط / The invoice pattern does not permit changing the cash account on the invoice."
                : "نمط الفاتورة لا يسمح بتغيير حساب الذمة من الفاتورة؛ عرّف حساب الزبائن في النمط دون «إلزامي» / The invoice pattern does not permit changing the receivable account on the invoice.",
                "customerAccountId");
        return accountId;
    }

    /// <summary>Turns the resolver's refusal of an override into the same stable business error the save path reports.</summary>
    private static Guid ControlAccount(Func<Guid> resolve)
    {
        try { return resolve(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) == RuleViolation.PatternOverrideNotAllowed)
        {
            throw new BusinessRuleException("sales.customer_account_override_not_allowed",
                "نمط الفاتورة لا يسمح بتغيير حساب العميل من الفاتورة / The invoice pattern does not permit an invoice-level account.", "customerAccountId");
        }
    }
}
