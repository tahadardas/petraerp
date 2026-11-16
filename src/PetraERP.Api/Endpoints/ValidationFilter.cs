using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PetraERP.Api.Errors;

namespace PetraERP.Api.Endpoints;

/// <summary>Runs DataAnnotations validation on the request body and returns field-level 400 problems.</summary>
internal sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var body = context.Arguments.OfType<T>().FirstOrDefault();
        if (body is null)
        {
            return ApiProblems.Result(context.HttpContext, StatusCodes.Status400BadRequest, ApiErrorCodes.RequestMalformed);
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(body, new ValidationContext(body), results, validateAllProperties: true))
        {
            var errors = results
                .SelectMany(static result => result.MemberNames.DefaultIfEmpty(string.Empty))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(static member => JsonNamingPolicy.CamelCase.ConvertName(member), static _ => new[] { ApiErrorCodes.RequestInvalid },
                    StringComparer.Ordinal);
            return ApiProblems.Result(context.HttpContext, StatusCodes.Status400BadRequest, ApiErrorCodes.RequestInvalid, null, errors);
        }

        return await next(context).ConfigureAwait(false);
    }
}

internal static class ValidationFilterExtensions
{
    internal static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}
