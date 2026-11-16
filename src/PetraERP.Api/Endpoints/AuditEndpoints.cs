using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Contracts.Audit;
using ContractAuditType = PetraERP.Contracts.Audit.SecurityAuditEventType;

namespace PetraERP.Api.Endpoints;

internal static class AuditEndpoints
{
    internal static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit/security-events", ListSecurityAuditEventsAsync)
            .WithTags("Audit")
            .WithName("ListSecurityAuditEvents")
            .WithSummary("Pages the append-only security audit trail, newest first, with server-side filters.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.SecurityAuditView))
            .Produces<SecurityAuditPageResponse>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> ListSecurityAuditEventsAsync(Guid? userId, ContractAuditType? eventType,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, ISecurityAuditQueries queries,
        CancellationToken cancellationToken)
    {
        if (eventType is { } requested && !Enum.IsDefined(requested))
        {
            throw new RequestValidationException("request.event_type_invalid", "Unknown security event type.", "eventType");
        }

        if (fromUtc is { } from && toUtc is { } to && to <= from)
        {
            throw new RequestValidationException("request.range_invalid", "toUtc must be later than fromUtc.", "toUtc");
        }

        var result = await queries.ListAsync(new SecurityAuditQuery(userId, eventType.ToApplication(), fromUtc?.ToUniversalTime(),
            toUtc?.ToUniversalTime(), PageRequest.Create(page, pageSize)), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }
}
