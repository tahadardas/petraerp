using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace PetraERP.Api.OpenApi;

/// <summary>
/// Normalizes the generated contract: stable document info, no host-specific servers, and a bearer security
/// scheme applied to every operation that is not anonymous. The output must be deterministic because the
/// committed contract and the generated desktop client are compared against it.
/// </summary>
internal sealed class PetraOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    internal const string BearerScheme = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        document.Info = new OpenApiInfo
        {
            Title = "PetraERP API",
            Version = "v1",
            Description = "PetraERP server API. Financial values are invariant decimal strings; UUIDs are strings; " +
                "audit timestamps are UTC. Errors are RFC 9457 problem details with a stable 'code'.",
        };
        document.Servers = [];
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
        {
            [BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Short-lived access token from /api/v1/auth/login or /api/v1/auth/refresh.",
            },
        };

        var anonymous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var description in context.DescriptionGroups.SelectMany(static group => group.Items))
        {
            var metadata = description.ActionDescriptor.EndpointMetadata;
            var name = metadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName;
            if (name is not null && metadata.OfType<IAllowAnonymous>().Any())
            {
                anonymous.Add(name);
            }
        }

        foreach (var path in document.Paths.Values)
        {
            if (path.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Operations.Values)
            {
                if (operation.OperationId is { } id && anonymous.Contains(id))
                {
                    continue;
                }

                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
                    },
                ];
            }
        }

        return Task.CompletedTask;
    }
}
