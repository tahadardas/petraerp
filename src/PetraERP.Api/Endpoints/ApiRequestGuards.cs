using PetraERP.Application.Common;
using PetraERP.Api.Errors;

namespace PetraERP.Api.Endpoints;

internal static class ApiRequestGuards
{
    internal static ConcurrencyToken RequireVersion(string? value, string field = "version") =>
        ConcurrencyToken.TryParse(value, out var token)
            ? token
            : throw new RequestValidationException(ApiErrorCodes.VersionInvalid,
                "The version must be the canonical 12-character Base64 value returned by the server.", field);
}
