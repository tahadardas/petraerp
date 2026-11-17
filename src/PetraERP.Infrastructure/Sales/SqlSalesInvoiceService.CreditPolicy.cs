using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Sales;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Sales;

public sealed partial class SqlSalesInvoiceService
{
    private const string CashCustomerCode = "CASH-CUSTOMER";

    // Called only inside CurrencyWriteTransaction: its company update lock serializes lazy creation.
    private async Task<PartyRow> ResolveSalesCustomerAsync(Guid companyId, Guid customerId, PaymentMethod method, CancellationToken token)
    {
        if (!Enum.IsDefined(method))
            throw new BusinessRuleException("sales.payment_method_invalid", "طريقة الدفع غير صالحة / Invalid payment method.");
        PartyRow? customer;
        if (customerId == Guid.Empty)
        {
            if (method != PaymentMethod.Cash)
                throw new BusinessRuleException("sales.customer_required", "الزبون مطلوب للبيع الآجل / A customer is required for credit sales.");
            customer = await context.Parties.SingleOrDefaultAsync(p => p.CompanyId == companyId && p.IsCashCustomer, token).ConfigureAwait(false);
            if (customer is null)
            {
                if (await context.Parties.AnyAsync(p => p.CompanyId == companyId && p.Code == CashCustomerCode, token).ConfigureAwait(false))
                    throw new BusinessRuleException("sales.cash_customer_code_reserved", "رمز الزبون النقدي العام مستخدم؛ صحح البطاقة دون تحويلها تلقائياً / The reserved walk-in code is in use; resolve the existing card first.");
                customer = new PartyRow
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, Code = CashCustomerCode,
                    NameArabic = "زبون نقدي عام", NameEnglish = "Walk-in cash customer",
                    NormalizedSearchText = ArabicTextNormalizer.Normalize(CashCustomerCode + " زبون نقدي عام Walk-in cash customer"),
                    Role = (int)PartyRole.Customer, IsActive = true, IsCashCustomer = true,
                    CreditPolicyKind = (int)CreditPolicyKind.NoLimit, CreditLimit = 0m,
                    CreatedAtUtc = timeProvider.GetUtcNow(), UpdatedAtUtc = timeProvider.GetUtcNow()
                };
                context.Parties.Add(customer);
                await context.SaveChangesAsync(token).ConfigureAwait(false);
            }
        }
        else
        {
            customer = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == customerId && p.CompanyId == companyId, token).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("Customer");
        }
        RequireSalesCustomer(customer, method);
        return customer;
    }

    private static void RequireSalesCustomer(PartyRow customer, PaymentMethod method)
    {
        if (!customer.IsActive || customer.Role is not ((int)PartyRole.Customer or (int)PartyRole.Both))
            throw new BusinessRuleException("sales.customer_invalid", "البطاقة يجب أن تكون زبوناً فعالاً / The party must be an active customer.");
        if (method == PaymentMethod.Credit && customer.IsCashCustomer)
            throw new BusinessRuleException("sales.cash_customer_credit_forbidden", "الزبون النقدي العام لا يقبل بيعاً آجلاً / The walk-in customer cannot receive credit sales.");
    }

    private sealed record CreditSnapshot(decimal Exposure, decimal InvoiceAmount, decimal Projected, bool RequiresAction);

    /// <param name="invoice">The invoice; when it was loaded with its customer the card is reused instead of read again.</param>
    /// <param name="customerInvoices">The customer's open-item composition when the caller already loaded it (same arguments as below).</param>
    private async Task<CreditSnapshot> LoadCreditSnapshotAsync(SalesInvoiceRow invoice, CancellationToken token,
        IReadOnlyList<SettlementInvoice>? customerInvoices = null)
    {
        var customer = invoice.Customer is { } loaded && loaded.Id == invoice.CustomerId && loaded.CompanyId == invoice.CompanyId
            ? loaded
            : await context.Parties.AsNoTracking().SingleAsync(p => p.Id == invoice.CustomerId && p.CompanyId == invoice.CompanyId, token).ConfigureAwait(false);
        var invoices = customerInvoices
            ?? await SettlementQueries.LoadInvoicesAsync(context, invoice.CompanyId, null, VoucherKind.Receipt, customer.Id, null, null, token).ConfigureAwait(false);
        var availableAdvance = await SettlementQueries.AvailableAdvanceBaseAsync(context, invoice.CompanyId, null, VoucherKind.Receipt, customer.Id, token).ConfigureAwait(false);
        decimal exposure, amount, projected;
        try
        {
            exposure = checked(invoices.Sum(i => i.OutstandingBase) - availableAdvance);
            amount = invoice.Status is (int)SalesInvoiceStatus.Draft or (int)SalesInvoiceStatus.Approved
                ? Math.Round(checked(invoice.GrandTotal * invoice.ExchangeRate), 4, MidpointRounding.ToEven) : 0m;
            projected = checked(exposure + amount);
            const decimal maximum = 999999999999999.9999m;
            if (exposure < -maximum || exposure > maximum || amount < 0m || amount > maximum || projected < -maximum || projected > maximum)
                throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException("sales.credit_amount_out_of_range", "قيمة التعرض الائتماني خارج حدود الدقة المالية / Credit exposure is outside the supported financial precision.");
        }
        var action = customer.IsCreditBlocked || (customer.CreditPolicyKind != (int)CreditPolicyKind.NoLimit && projected > customer.CreditLimit);
        return new(exposure, amount, projected, action);
    }

    private async Task EnforceSalesCreditPolicyAsync(SalesInvoiceRow invoice, Guid actorId, Guid requestKey,
        PostSalesInvoiceCommand command, CancellationToken token)
    {
        var customer = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == invoice.CustomerId && p.CompanyId == invoice.CompanyId, token).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Customer");
        RequireSalesCustomer(customer, (PaymentMethod)invoice.PaymentMethod);
        if (invoice.PaymentMethod == (int)PaymentMethod.Cash) return; // Cash debits cash; it never consumes a credit allowance.
        if (!Enum.IsDefined((CreditPolicyKind)customer.CreditPolicyKind))
            throw new BusinessRuleException("sales.credit_policy_invalid", "سياسة الائتمان غير صالحة / Invalid credit policy.");
        var snapshot = await LoadCreditSnapshotAsync(invoice, token).ConfigureAwait(false);
        if (!snapshot.RequiresAction) return;
        var privileged = customer.IsCreditBlocked || customer.CreditPolicyKind == (int)CreditPolicyKind.BlockOnExceed;
        var reason = command.CreditOverrideReason?.Trim();
        if (privileged && !command.OverrideCreditPolicy)
            throw new BusinessRuleException("sales.credit_limit_blocked", "البيع الآجل محظور أو يتجاوز الحد؛ يلزم تجاوز مصرح مع سبب / Credit sales are blocked or exceed the limit; an authorized override and reason are required.");
        if (!privileged && !command.ConfirmCreditWarning && !command.OverrideCreditPolicy)
            throw new BusinessRuleException("sales.credit_limit_warning", "البيع يتجاوز الحد الائتماني؛ أعد تحميل الفاتورة وأكد التحذير مع سبب / The sale exceeds the credit limit; reload and acknowledge the warning with a reason.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BusinessRuleException("sales.credit_override_reason_required", "أدخل سبباً من 1 إلى 500 حرف / Enter a reason of 1 to 500 characters.");
        if (command.OverrideCreditPolicy)
            await new SqlPostingService(context).VerifySalesCreditOverrideActorAsync(PostingRequestScope.Create(invoice.CompanyId, invoice.BranchId, actorId), token).ConfigureAwait(false);
        context.SalesCreditAudit.Add(new SalesCreditAuditRow
        {
            Id = Guid.NewGuid(), CompanyId = invoice.CompanyId, BranchId = invoice.BranchId,
            InvoiceId = invoice.Id, PartyId = customer.Id, ActorUserId = actorId, IdempotencyKey = requestKey,
            PolicyKind = customer.CreditPolicyKind, WasCreditBlocked = customer.IsCreditBlocked, IsOverride = command.OverrideCreditPolicy,
            CreditLimitBase = customer.CreditLimit, ExposureBeforeBase = snapshot.Exposure,
            InvoiceBaseAmount = snapshot.InvoiceAmount, ExposureAfterBase = snapshot.Projected,
            Reason = reason, RecordedAtUtc = timeProvider.GetUtcNow()
        });
    }
}
