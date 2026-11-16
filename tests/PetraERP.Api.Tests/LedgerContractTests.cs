using System.Text.Json;
using System.Text.RegularExpressions;

namespace PetraERP.Api.Tests;

/// <summary>Static guards over the committed OpenAPI contract used to generate typed clients.</summary>
public sealed partial class LedgerContractTests
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex RouteParameter();

    private static JsonDocument Contract() => JsonDocument.Parse(File.ReadAllText(OpenApiContract.CommittedPath()));

    [Fact]
    public void EveryRouteParameterIsDeclaredSoGeneratedClientsBuildValidUrls()
    {
        using var contract = Contract();
        var missing = new List<string>();
        foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
        {
            var names = RouteParameter().Matches(path.Name).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (var operation in path.Value.EnumerateObject().Where(o => o.Value.ValueKind == JsonValueKind.Object && o.Value.TryGetProperty("operationId", out _)))
            {
                var declared = operation.Value.TryGetProperty("parameters", out var parameters)
                    ? parameters.EnumerateArray().Where(p => p.GetProperty("in").GetString() == "path").Select(p => p.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal)
                    : [];
                missing.AddRange(names.Except(declared).Select(name => operation.Value.GetProperty("operationId").GetString() + ":" + name));
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void JournalStateIsANamedEnumAndNoJournalStateIsNumeric()
    {
        using var contract = Contract();
        var schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        var state = schemas.GetProperty("JournalState");
        Assert.Equal(["Draft", "Validated", "Approved", "Posted", "Reversed"], state.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        foreach (var name in new[] { "JournalDetailsResponse", "JournalSummaryItemResponse", "GeneralJournalLineResponse" })
        {
            var property = schemas.GetProperty(name).GetProperty("properties").GetProperty("state");
            Assert.Contains("JournalState", property.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LedgerMoneyInReportsIsTransportedAsDecimalStrings()
    {
        using var contract = Contract();
        var schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var (schema, field) in new[] { ("TrialBalanceResponse", "totalEndingDebit"), ("TrialBalanceRowResponse", "periodCredit"),
            ("AccountLedgerLineResponse", "runningBalance"), ("GeneralJournalResponse", "totalBaseDebit") })
        {
            var property = schemas.GetProperty(schema).GetProperty("properties").GetProperty(field);
            Assert.Equal("string", property.GetProperty("type").GetString());
        }
    }
}
