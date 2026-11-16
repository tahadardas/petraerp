using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.Parties;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Parties;

public sealed partial class SqlPartyService
{
    private static void RequireMutableParty(PartyRow row)
    {
        if (row.IsCashCustomer)
            throw new BusinessRuleException("parties.system_cash_read_only", "بطاقة الزبون النقدي العام محمية ولا تقبل التعديل أو التعطيل / The system walk-in cash customer is protected from editing or deactivation.");
    }

    private static void ValidateCreditPolicy(decimal limit, int days, CreditPolicyKind policy)
    {
        if (!Enum.IsDefined(policy) || limit < 0m || limit > 999999999999999.9999m || decimal.Round(limit,4) != limit || days < 0)
            throw new BusinessRuleException("parties.credit_policy_invalid", "الحد الائتماني غير سالب وبأربع منازل وأيام الاستحقاق غير سالبة والسياسة صالحة / Credit limit must be nonnegative with four decimals, payment days nonnegative, and policy valid.");
    }

    private async Task AddPolicyAuditAsync(PartyRow row, decimal limit, int policy, bool blocked, Guid actor, Guid branch,
        bool initial, CancellationToken cancellationToken)
    {
        var beforeLimit = initial ? limit : row.CreditLimit;
        var beforePolicy = initial ? policy : row.CreditPolicyKind;
        var beforeBlocked = initial ? blocked : row.IsCreditBlocked;
        var afterLimit = initial ? row.CreditLimit : limit;
        var afterPolicy = initial ? row.CreditPolicyKind : policy;
        var afterBlocked = initial ? row.IsCreditBlocked : blocked;
        if (beforeLimit == afterLimit && beforePolicy == afterPolicy && beforeBlocked == afterBlocked) return;
        if (actor == Guid.Empty || branch == Guid.Empty)
            throw new BusinessRuleException("parties.policy_actor_required", "تغيير سياسة الائتمان يتطلب مستخدماً وفرعاً مصرحين / Credit-policy changes require an authorized actor and branch.");
        await new SqlPostingService(context).VerifyTreasuryActorAsync(row.CompanyId, branch, actor,
            PermissionCatalog.PartiesManage, cancellationToken).ConfigureAwait(false);
        context.PartyPolicyAudit.Add(new PartyPolicyAuditRow
        {
            Id = Guid.NewGuid(), CompanyId = row.CompanyId, PartyId = row.Id, ActorId = actor,
            BeforeCreditLimit = beforeLimit, AfterCreditLimit = afterLimit, BeforePolicy = beforePolicy, AfterPolicy = afterPolicy,
            BeforeBlocked = beforeBlocked, AfterBlocked = afterBlocked, DateUtc = DateTimeOffset.UtcNow,
        });
    }
}
