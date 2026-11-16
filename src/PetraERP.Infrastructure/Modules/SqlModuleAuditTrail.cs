using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Modules;

/// <summary>
/// Non-financial module actions go to the same durable security audit trail as core administrative actions (who, which
/// company and branch, which module, what). Financial effects are additionally audited by the journal trail itself.
/// </summary>
public sealed class SqlModuleAuditTrail(SystemDbContext db, TimeProvider time) : IAuditTrail
{
    private const int MaximumText = 200;
    private const int MaximumDetails = 1000;

    public async Task RecordAsync(ModuleRequestContext context, string action, string targetType, string targetId, string details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(targetType))
            throw new BusinessRuleException("module.audit_invalid", "an audit action and target type are required.");
        if (context.UserId == Guid.Empty || context.CompanyId == Guid.Empty)
            throw new BusinessRuleException("module.audit_invalid", "an audit entry needs the user and company.");

        var row = SecurityAuditEntries.Create(
            time.GetUtcNow(), SecurityAuditEventType.ModuleAction, ClientContext.Unknown,
            userId: context.UserId, actorUserId: context.UserId, companyId: context.CompanyId, branchId: context.BranchId,
            reason: Bound(action, MaximumText),
            details: new
            {
                module = context.ModuleId ?? "unknown",
                targetType = Bound(targetType, MaximumText),
                targetId = Bound(targetId, MaximumText),
                details = Bound(details ?? string.Empty, MaximumDetails),
            });
        db.SecurityAuditEvents.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Bound(string value, int length) => value.Length <= length ? value : value[..length];
}
