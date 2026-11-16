using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PetraERP.Api.OpenApi;

/// <summary>
/// Documents the PetraERP extensions that every problem response carries (see ApiProblems): the stable
/// machine-readable <c>code</c>, <c>traceId</c>, <c>correlationId</c> and, for validation failures, <c>errors</c>.
/// Without this the contract would claim additionalProperties=false and generated clients would drop the code.
/// </summary>
internal sealed class PetraProblemSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (context.JsonTypeInfo.Type != typeof(ProblemDetails))
        {
            return Task.CompletedTask;
        }

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        schema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable machine-readable error code, for example 'auth.invalid_credentials'.",
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "W3C trace identifier of the failed request.",
        };
        schema.Properties["correlationId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Value of the X-Correlation-Id response header; quote it when reporting a problem.",
        };
        schema.Properties["errors"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            Description = "Validation failures keyed by field name (400 responses only).",
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add("code");
        schema.Required.Add("traceId");
        schema.Required.Add("correlationId");
        return Task.CompletedTask;
    }
}
