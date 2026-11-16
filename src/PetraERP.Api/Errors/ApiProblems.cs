using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PetraERP.Api.Hosting;

namespace PetraERP.Api.Errors;

/// <summary>Creates and writes RFC 9457 problem responses with PetraERP code, trace and correlation identifiers.</summary>
internal static class ApiProblems
{
    internal const string CodeExtension = "code";
    internal const string ErrorsExtension = "errors";

    internal static ProblemDetails Create(HttpContext httpContext, int status, string code, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var problem = new ProblemDetails
        {
            Status = status,
            Type = "urn:petraerp:problem:" + code,
            Title = ApiErrorCatalog.Title(code, status, PrefersArabic(httpContext)),
            Detail = detail,
            Instance = httpContext.Request.Path.Value,
        };
        problem.Extensions[CodeExtension] = code;
        if (errors is not null && errors.Count > 0)
        {
            problem.Extensions[ErrorsExtension] = errors;
        }

        return problem;
    }

    internal static IResult Result(HttpContext httpContext, int status, string code, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        TypedResults.Problem(Create(httpContext, status, code, detail, errors));

    internal static async Task WriteAsync(HttpContext httpContext, int status, string code, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.StatusCode = status;
        var writer = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await writer.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = Create(httpContext, status, code, detail),
        }).ConfigureAwait(false);
    }

    /// <summary>Adds trace/correlation identifiers and a default code to every problem the framework writes.</summary>
    internal static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Status = status;
        if (!problem.Extensions.ContainsKey(CodeExtension))
        {
            var code = ApiErrorCatalog.DefaultCode(status);
            problem.Extensions[CodeExtension] = code;
            problem.Type = "urn:petraerp:problem:" + code;
            problem.Title = ApiErrorCatalog.Title(code, status, PrefersArabic(context.HttpContext));
        }

        problem.Extensions.Remove("exception");
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext);
    }

    private static bool PrefersArabic(HttpContext httpContext)
    {
        var languages = httpContext.Request.GetTypedHeaders().AcceptLanguage;
        if (languages is null || languages.Count == 0)
        {
            return false;
        }

        var preferred = languages
            .OrderByDescending(static value => value.Quality ?? 1.0)
            .Select(static value => value.Value.Value)
            .FirstOrDefault();
        return preferred is not null && preferred.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
    }

    internal static void SetRetryAfter(HttpResponse response, int seconds) =>
        response.Headers[HeaderNames.RetryAfter] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
