using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Api.Security;

internal sealed class FinancialWorkspaceMetadata
{
    internal static readonly FinancialWorkspaceMetadata Instance = new();
}

/// <summary>One immutable annual EF unit of work per authenticated HTTP request, never a global selection.</summary>
internal sealed class FinancialRequestScope
{
    private AccountingDbContext? context;
    internal AccountingDbContext Context => context ?? throw new BusinessRuleException("workspace.selection_required", "Select an authorized fiscal workspace before accessing financial data.");

    internal Guid FiscalYearId { get; private set; }
    internal void Bind(AccountingDbContext value, Guid fiscalYearId)
    {
        if (context is not null) throw new InvalidOperationException("A financial request cannot switch workspaces.");
        context = value;
        FiscalYearId = fiscalYearId;
    }
}

internal sealed class FinancialWorkspaceMiddleware(RequestDelegate next)
{
    internal const string WorkspaceHeader = "X-Petra-Workspace";

    public async Task InvokeAsync(HttpContext http, FinancialRequestScope execution, IFiscalDatabaseResolver resolver,
        IWorkspaceAuthorizationService workspaces, CentralDataContext central, IAccountingDbContextFactory factory)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<FinancialWorkspaceMetadata>() is null)
        {
            await next(http).ConfigureAwait(false);
            return;
        }
        // Idempotent financial writes queue for a turn instead of storming the connection pool.
        using var turn = http.GetEndpoint()?.Metadata.GetMetadata<DeadlockRetryMetadata>() is null
            ? null
            : await FinancialWriteGate.EnterAsync(http.RouteGuid("companyId") ?? Guid.Empty, http.RequestAborted).ConfigureAwait(false);
        var actor = http.RequireSession().UserId;
        var company = http.RouteGuid("companyId") ?? throw new RequestValidationException("workspace.company_required", "A company route is required.", "companyId");
        var branch = http.RouteGuid("branchId");
        if (http.Request.Headers[WorkspaceHeader].Count != 1
            || !Guid.TryParse(http.Request.Headers[WorkspaceHeader], out var workspace) || workspace == Guid.Empty)
            throw new RequestValidationException("workspace.selection_required", "Supply the registered fiscal workspace ID in X-Petra-Workspace.", WorkspaceHeader);
        if (!await workspaces.CanAccessWorkspaceAsync(actor, workspace, http.RequestAborted).ConfigureAwait(false))
            throw new BusinessRuleException("workspace.access_denied", "The selected fiscal workspace is unavailable or not authorized.");
        var resolved = await resolver.ResolveAsync(workspace, http.RequestAborted).ConfigureAwait(false);
        if (resolved.CompanyId != company || branch is null
            || !await central.HasAuthorizedBranchAsync(actor, company, branch.Value, http.RequestAborted).ConfigureAwait(false))
            throw new BusinessRuleException("workspace.scope_mismatch", "The selected fiscal workspace does not match the authorized route scope.");

        var candidates = new List<AccountingOperation>();
        foreach (var policy in http.GetEndpoint()!.Metadata.GetOrderedMetadata<IAuthorizeData>())
        {
            const string prefix = "petra:permission:";
            if (policy.Policy?.StartsWith(prefix, StringComparison.Ordinal) != true) continue;
            if (AccountingOperationPolicy.TryFromPermission(policy.Policy[prefix.Length..], out var candidate))
                candidates.Add(candidate);
        }
        // An endpoint may require several permissions (a return needs both Reverse and Post); the governing
        // composed write operation is bound. Read-only endpoints map to none and bind no write scope.
        AccountingOperation? operation = AccountingOperationPolicy.ResolveEndpointWrite(candidates);
        if (operation is not null && resolved.State != "Active" && http.Request.Path.Value?.Contains("/opening-balances", StringComparison.Ordinal) != true)
            throw new BusinessRuleException("workspace.opening_balances_pending", "تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية. Posting remains unavailable.");
        http.Response.Headers["X-Petra-Workspace-Readiness"] = resolved.State;
        if (resolved.State != "Active" && (http.Request.Path.Value?.Contains("/reports/", StringComparison.Ordinal) == true
            || http.Request.Path.Value?.Contains("/reporting/execute", StringComparison.Ordinal) == true))
            throw new BusinessRuleException("workspace.opening_balances_pending", "Financial reports are unavailable until opening balances are verified; master data only was copied.");
        void BindContext(AccountingDbContext context, FinancialRequestScope target, IServiceProvider services)
        {
            if (operation is { } write)
                context.BindWriteScope(new(company, resolved.FiscalYearId, actor, write, branch));
            target.Bind(context, resolved.FiscalYearId);
            // Resolving the scoped alias registers the context with request-scope disposal even for an empty response.
            _ = services.GetRequiredService<FinancialDbContext>();
        }

        var annual = factory.CreateDbContext(resolved.ConnectionString);
        try
        {
            BindContext(annual, execution, http.RequestServices);
        }
        catch
        {
            await annual.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // Only an endpoint that is idempotent end to end, and a request that provably carries its key, may be re-run whole
        // after a deadlock victim rollback; everything else keeps the single attempt and the controlled 503.
        if (http.GetEndpoint()?.Metadata.GetMetadata<DeadlockRetryMetadata>() is null
            || !await IdempotencyKeyProbe.HasValidKeyAsync(http.Request, http.RequestAborted).ConfigureAwait(false))
        {
            await next(http).ConfigureAwait(false);
            return;
        }

        var original = http.RequestServices;
        var retryScopes = new List<AsyncServiceScope>();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await next(http).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception) when (attempt < DeadlockRetryPolicy.MaximumRetries && DeadlockRetryPolicy.IsDeadlockVictim(exception)
                    && !http.Response.HasStarted && !http.RequestAborted.IsCancellationRequested && http.Request.Body.CanSeek)
                {
                    // The failed unit of work was rolled back whole and its context is poisoned. Re-run the entire unit on a new
                    // DI scope and a new annual context with the same body and the same idempotency key, so the durable
                    // idempotency record - not luck - guarantees one financial effect.
                    DeadlockRetryLog.Retrying(original.GetRequiredService<ILoggerFactory>().CreateLogger("PetraERP.DeadlockRetry"),
                        attempt + 1, http.Request.Path.Value ?? string.Empty);
                    await Task.Delay(DeadlockRetryPolicy.Delay(attempt), http.RequestAborted).ConfigureAwait(false);
                    http.Request.Body.Position = 0;
                    var retryScope = original.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                    retryScopes.Add(retryScope);
                    var retryContext = factory.CreateDbContext(resolved.ConnectionString);
                    try
                    {
                        BindContext(retryContext, retryScope.ServiceProvider.GetRequiredService<FinancialRequestScope>(), retryScope.ServiceProvider);
                    }
                    catch
                    {
                        await retryContext.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }

                    http.RequestServices = retryScope.ServiceProvider;
                }
            }
        }
        finally
        {
            http.RequestServices = original;
            foreach (var scope in retryScopes) await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
