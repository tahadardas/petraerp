using PetraERP.Domain.Primitives;

namespace PetraERP.Api.Errors;

/// <summary>
/// Maps tagged accounting rule violations to stable problem codes. Diagnostic exception text is never
/// returned: clients localize by <c>code</c>; the server log keeps the diagnostic message.
/// </summary>
internal static class LedgerProblems
{
    internal static (int Status, string Code) Classify(Exception exception, string fallbackCode)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var code = RuleViolation.CodeOf(exception);
        if (code is null)
        {
            return (StatusCodes.Status422UnprocessableEntity, fallbackCode);
        }

        return (code == RuleViolation.PostingConflict ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity, code);
    }

    internal static IResult Rejected(HttpContext httpContext, Exception exception, string fallbackCode)
    {
        var (status, code) = Classify(exception, fallbackCode);
        return ApiProblems.Result(httpContext, status, code);
    }
}
