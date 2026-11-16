using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Api.Hosting;

namespace PetraERP.Api.Errors;

/// <summary>
/// Maps known application exceptions to stable problem responses. Unknown exceptions become a 500 with a
/// correlation identifier only; stack traces, SQL text and connection details never reach the client.
/// </summary>
internal sealed partial class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> logger;
    private readonly IHostEnvironment environment;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; nothing useful can be written.
            return true;
        }

        var (status, code, detail, errors) = Map(exception);
        if (status >= 500)
        {
            LogUnhandled(logger, exception, CorrelationIdMiddleware.Get(httpContext));
            if (status == StatusCodes.Status503ServiceUnavailable)
            {
                ApiProblems.SetRetryAfter(httpContext.Response, 1);
            }

            detail = environment.IsDevelopment() ? exception.GetType().FullName : null;
        }

        httpContext.Response.StatusCode = status;
        var problem = ApiProblems.Create(httpContext, status, code, detail, errors);
        await TypedResults.Problem(problem).ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }

    private static (int Status, string Code, string? Detail, IReadOnlyDictionary<string, string[]>? Errors) Map(Exception exception) =>
        exception switch
        {
            RequestValidationException validation => (StatusCodes.Status400BadRequest, validation.Code, validation.Message,
                validation.Field is null ? null : new Dictionary<string, string[]> { [validation.Field] = [validation.Code] }),
            BusinessRuleException rule => (StatusCodes.Status422UnprocessableEntity, rule.Code, rule.Message,
                rule.Field is null ? null : new Dictionary<string, string[]> { [rule.Field] = [rule.Code] }),
            ResourceNotFoundException => (StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, null, null),
            ResourceConflictException conflict => (StatusCodes.Status409Conflict, conflict.Code, conflict.Message, null),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, ApiErrorCodes.ConcurrencyConflict, null, null),
            PostingIdempotencyConflictException => (StatusCodes.Status409Conflict, ApiErrorCodes.IdempotencyConflict, null, null),
            PostingConcurrencyException => (StatusCodes.Status409Conflict, ApiErrorCodes.ConcurrencyConflict, null, null),
            PostingAuthorizationException => (StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, null, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, ApiErrorCodes.ConcurrencyConflict, "تغيرت البيانات؛ أعد التحميل / Data changed; reload it.", null),
            DbUpdateException { InnerException: SqlException { Number: 51021 } } =>
                (StatusCodes.Status422UnprocessableEntity, "currency.base_locked", "العملة الأساسية مقفلة لوجود بيانات مالية / Base currency is locked by existing financial data.", null),
            DbUpdateException { InnerException: SqlException { Number: 51020 } } =>
                (StatusCodes.Status422UnprocessableEntity, "currency.history_immutable", "سجل العملات لا يعدل؛ أضف تصحيحاً جديداً / Currency history is immutable; append a correction.", null),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } currencyDuplicate when currencyDuplicate.Entries.Any(e =>
                e.Metadata.GetSchema() == "system" && e.Metadata.GetTableName() is "Currencies" or "CompanyAdditionalCurrencies") =>
                (StatusCodes.Status409Conflict, "currency.catalog_conflict", "أُضيفت العملة بالتزامن؛ أعد التحميل / Currency was added concurrently; reload.", null),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } duplicate when duplicate.Entries.Any(e => e.Metadata.GetSchema() == "inventory") =>
                (StatusCodes.Status409Conflict, "inventory.request_conflict", "تعارض تكرار بيانات مخزون أو طلب أو حركة؛ أعد التحميل قبل المحاولة / Duplicate inventory data, request or movement; reload before retrying.", null),
            DbUpdateException { InnerException: SqlException { Number: 1205 or -2 } } =>
                (StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.TransientFailure, "تعارض مؤقت؛ أعد المحاولة بالمفتاح نفسه / Temporary conflict; retry with the same request key.", null),
            PetraERP.Application.GeneralLedger.Reports.ReportScopeDeniedException { Permission: { Length: > 0 } } permission =>
                (StatusCodes.Status403Forbidden, ApiErrorCodes.PermissionDenied, permission.Message, null),
            PetraERP.Application.GeneralLedger.Reports.ReportScopeDeniedException => (StatusCodes.Status403Forbidden, ApiErrorCodes.BranchScopeDenied, null, null),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, ApiErrorCodes.RequestTooLarge, null, null),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, ApiErrorCodes.RequestMalformed, null, null),
            _ when PetraERP.Domain.Primitives.RuleViolation.CodeOf(exception) is not null =>
                LedgerRule(exception),
            SqlException { Number: 1205 or -2 } => (StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.TransientFailure, null, null),
            PetraERP.Api.Security.ServerBusyException => (StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.TransientFailure, "الخادم مشغول؛ أعد المحاولة بالمفتاح نفسه / Server busy; retry with the same key.", null),
            InvalidOperationException poolTimeout when PetraERP.Api.Security.DeadlockRetryPolicy.IsPoolTimeout(poolTimeout) =>
                (StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.TransientFailure, "الخادم مشغول؛ أعد المحاولة بالمفتاح نفسه / Server busy; retry with the same key.", null),
            _ => (StatusCodes.Status500InternalServerError, ApiErrorCodes.Unexpected, null, null),
        };

    private static (int, string, string?, IReadOnlyDictionary<string, string[]>?) LedgerRule(Exception exception)
    {
        var (status, code) = LedgerProblems.Classify(exception, ApiErrorCodes.RequestInvalid);
        return (status, code, null, null);
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled request failure. CorrelationId={CorrelationId}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string correlationId);
}
