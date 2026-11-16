using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Platform;

namespace PetraERP.Api.Tests;

/// <summary>API behaviour that is verifiable without a database: errors, headers, token validation and contract shape.</summary>
public sealed class ApiContractTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnauthenticatedRequestsReceiveProblemDetailsWithCorrelation()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancellation).ConfigureAwait(true);
        await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated").ConfigureAwait(true);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuppliedCorrelationIdIsEchoedAndMalformedOnesAreReplaced()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        using var good = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/status", UriKind.Relative));
        good.Headers.Add("X-Correlation-Id", "desktop-0123456789");
        using var goodResponse = await client.SendAsync(good, Cancellation).ConfigureAwait(true);
        Assert.Equal("desktop-0123456789", goodResponse.Headers.GetValues("X-Correlation-Id").Single());

        using var bad = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/system/status", UriKind.Relative));
        bad.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script>");
        using var badResponse = await client.SendAsync(bad, Cancellation).ConfigureAwait(true);
        Assert.NotEqual("<script>", badResponse.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task StatusEndpointIsAnonymousAndResponsesCarrySecurityHeaders()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/v1/system/status", UriKind.Relative), Cancellation).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<ServiceStatusResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
        Assert.Equal("ok", status!.Status);
        Assert.Equal("v1", status.ApiVersion);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task MalformedAndInvalidBodiesAreRejectedBeforeAnyDatabaseAccess()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        using var malformed = new StringContent("{ \"userName\": ", Encoding.UTF8, "application/json");
        using var malformedResponse = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), malformed, Cancellation)
            .ConfigureAwait(true);
        await ProblemAssertions.AssertProblemAsync(malformedResponse, HttpStatusCode.BadRequest, "request.malformed").ConfigureAwait(true);

        using var unknownMember = new StringContent("{\"userName\":\"admin\",\"password\":\"x\",\"companyId\":\"00000000-0000-0000-0000-000000000001\"}",
            Encoding.UTF8, "application/json");
        using var unknownResponse = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), unknownMember, Cancellation)
            .ConfigureAwait(true);
        await ProblemAssertions.AssertProblemAsync(unknownResponse, HttpStatusCode.BadRequest, "request.malformed").ConfigureAwait(true);

        using var empty = new StringContent("{\"userName\":\"\",\"password\":\"\"}", Encoding.UTF8, "application/json");
        using var emptyResponse = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), empty, Cancellation)
            .ConfigureAwait(true);
        var problem = await ProblemAssertions.AssertProblemAsync(emptyResponse, HttpStatusCode.BadRequest, "request.invalid")
            .ConfigureAwait(true);
        Assert.True(problem.GetProperty("errors").TryGetProperty("userName", out _));

        using var numericEnum = new StringContent("{\"userName\":\"admin\",\"password\":\"x\",\"clientKind\":1}", Encoding.UTF8,
            "application/json");
        using var numericResponse = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), numericEnum, Cancellation)
            .ConfigureAwait(true);
        await ProblemAssertions.AssertProblemAsync(numericResponse, HttpStatusCode.BadRequest, "request.malformed").ConfigureAwait(true);
    }

    [Fact]
    public async Task ForgedExpiredAndForeignTokensAreRejected()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        var now = DateTime.UtcNow;
        var ownKey = Convert.FromBase64String(factory.SigningKey);

        var expired = CreateToken(ownKey, PetraApiFactory.Issuer, PetraApiFactory.Audience, now.AddMinutes(-30), now.AddMinutes(-20));
        await AssertRejectedAsync(client, expired, "auth.token_expired").ConfigureAwait(true);

        var otherKey = CreateToken(RandomNumberGenerator.GetBytes(32), PetraApiFactory.Issuer, PetraApiFactory.Audience,
            now.AddMinutes(-1), now.AddMinutes(5));
        await AssertRejectedAsync(client, otherKey, "auth.token_invalid").ConfigureAwait(true);

        var wrongAudience = CreateToken(ownKey, PetraApiFactory.Issuer, "SomeOtherApi", now.AddMinutes(-1), now.AddMinutes(5));
        await AssertRejectedAsync(client, wrongAudience, "auth.token_invalid").ConfigureAwait(true);

        var unsigned = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." +
            Base64UrlEncoder.Encode("{\"sub\":\"" + Guid.NewGuid() + "\",\"sid\":\"" + Guid.NewGuid() + "\",\"iss\":\"" +
                PetraApiFactory.Issuer + "\",\"aud\":\"" + PetraApiFactory.Audience + "\"}") + ".";
        await AssertRejectedAsync(client, unsigned, "auth.token_invalid").ConfigureAwait(true);
    }

    [Fact]
    public async Task OpenApiDocumentMatchesTheCommittedContract()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        var actual = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancellation).ConfigureAwait(true);
        var normalized = OpenApiContract.Normalize(actual);
        var path = OpenApiContract.CommittedPath();
        if (Environment.GetEnvironmentVariable("PETRAERP_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, normalized, Cancellation).ConfigureAwait(true);
        }

        Assert.True(File.Exists(path), "The committed contract is missing. Run tools/Update-ApiContract.ps1.");
        var committed = await File.ReadAllTextAsync(path, Cancellation).ConfigureAwait(true);
        if (!string.Equals(committed.ReplaceLineEndings("\n"), normalized, StringComparison.Ordinal))
        {
            var drift = Path.Combine(OpenApiContract.RepositoryRoot(), ".artifacts", "phase4", "openapi-actual.json");
            Directory.CreateDirectory(Path.GetDirectoryName(drift)!);
            await File.WriteAllTextAsync(drift, normalized, Cancellation).ConfigureAwait(true);
            Assert.Fail("The API contract drifted from openapi/petraerp-v1.json. Review .artifacts/phase4/openapi-actual.json and run tools/Update-ApiContract.ps1.");
        }
    }

    [Fact]
    public async Task OpenApiContractDescribesSecurityAndHasNoBinaryFloatingPointNumbers()
    {
        using var factory = new PetraApiFactory();
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancellation).ConfigureAwait(true);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.StartsWith("3.0", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        Assert.Empty(FindFloatingPointSchemas(root, "$"));

        var login = root.GetProperty("paths").GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.Equal("Login", login.GetProperty("operationId").GetString());
        Assert.False(login.TryGetProperty("security", out _));
        var me = root.GetProperty("paths").GetProperty("/api/v1/me").GetProperty("get");
        Assert.True(me.TryGetProperty("security", out _));
    }

    [Fact]
    public void ContractsExposeNoBinaryFloatingPointOrRawDecimalMembers()
    {
        var forbidden = new[] { typeof(double), typeof(float), typeof(decimal), typeof(Half) };
        var offenders = typeof(ServiceStatusResponse).Assembly.GetExportedTypes()
            .SelectMany(static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => forbidden.Contains(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .Select(static property => property.DeclaringType!.Name + "." + property.Name)
            .ToArray();
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1234.5600", "1234.56")]
    [InlineData("-0.0001", "-0.0001")]
    [InlineData("999999999999999.9999", "999999999999999.9999")]
    public void FinancialDecimalTextParsesExactInvariantStrings(string text, string expected)
    {
        // Expected values are given as text: a C# double literal could not represent them exactly.
        Assert.True(FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("1,234.56")]
    [InlineData("1e3")]
    [InlineData(" 1.00")]
    [InlineData("+1.00")]
    [InlineData("01.00")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1.23456")]
    [InlineData("1000000000000000")]
    [InlineData("١٢٣")]
    [InlineData("NaN")]
    public void FinancialDecimalTextRejectsAmbiguousOrRoundedInput(string text)
    {
        Assert.False(FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out _));
    }

    [Fact]
    public void FinancialDecimalTextFormatsWithFixedScaleAndNeverRounds()
    {
        Assert.Equal("1234.5600", FinancialDecimalText.Format(1234.56m, 4));
        Assert.Equal("-0.0001", FinancialDecimalText.Format(-0.0001m, 4));
        Assert.Throws<ArgumentOutOfRangeException>(static () => FinancialDecimalText.Format(0.00001m, 4));
    }

    private static List<string> FindFloatingPointSchemas(JsonElement element, string path)
    {
        var found = new List<string>();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var isNumberType = property.NameEquals("type") && property.Value.ValueKind == JsonValueKind.String
                        && property.Value.GetString() == "number";
                    var isFloatFormat = property.NameEquals("format") && property.Value.ValueKind == JsonValueKind.String
                        && property.Value.GetString() is "double" or "float";
                    if (isNumberType || isFloatFormat)
                    {
                        found.Add(path);
                    }

                    found.AddRange(FindFloatingPointSchemas(property.Value, path + "." + property.Name));
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    found.AddRange(FindFloatingPointSchemas(item, path + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"));
                    index++;
                }

                break;
        }

        return found;
    }

    private static string CreateToken(byte[] key, string issuer, string audience, DateTime notBefore, DateTime expires)
    {
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            IssuedAt = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["sid"] = Guid.NewGuid().ToString("D"),
            },
        });
    }

    private static async Task AssertRejectedAsync(HttpClient client, string token, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false);
        await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Unauthorized, code).ConfigureAwait(false);
    }
}
