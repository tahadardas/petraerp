using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// A report that fails must tell the operator what to fix. These lock the two ways the reporting repair order found
/// the shell hiding the reason: a shared validation string that quoted the password rule everywhere, and diagnostics
/// read only from the raw response body the typed client never fills in.
/// </summary>
public sealed class ReportFailureDiagnosticsTests
{
    [Fact]
    public void ServerProblemDetailFromATypedClientCallReachesTheOperator()
    {
        // The generated client deserializes the problem into Result and leaves Response empty.
        var exception = new PetraApiException<ProblemDetails>(
            "POST /reporting/execute", 400, null, new Dictionary<string, IEnumerable<string>>(),
            new ProblemDetails
            {
                Status = 400,
                Code = "reporting.parameters_invalid",
                Detail = "اختيار الحساب إلزامي لدفتر الأستاذ. / An account is required.",
                CorrelationId = "corr-4242",
                Errors = new Dictionary<string, IReadOnlyList<string>> { ["parameters"] = ["reporting.parameters_invalid"] },
            }, null);

        var diagnostics = DesktopErrors.Diagnostics(exception);

        Assert.NotNull(diagnostics);
        Assert.Contains("اختيار الحساب إلزامي", diagnostics, StringComparison.Ordinal);
        Assert.Contains("parameters: reporting.parameters_invalid", diagnostics, StringComparison.Ordinal);
        Assert.Contains("corr-4242", diagnostics, StringComparison.Ordinal);
        Assert.Equal("Validation", DesktopErrors.Key(exception));
    }

    [Fact]
    public void InsufficientStockProblemExplainsTheRemedyAndKeepsItemWarehouseAndQuantities()
    {
        var exception = new PetraApiException<ProblemDetails>(
            "POST /sales/invoices", 422, null, new Dictionary<string, IEnumerable<string>>(),
            new ProblemDetails
            {
                Status = 422,
                Code = "inventory.negative_stock_blocked",
                Detail = "الرصيد غير كافٍ للمادة CEM-50 — إسمنت بورتلاندي 50كغ في المستودع WH-CEMENT — مستودع الإسمنت: المطلوب 10 والمتاح 2 بالوحدة الأساسية.",
                Errors = new Dictionary<string, IReadOnlyList<string>> { ["lines"] = ["inventory.negative_stock_blocked"] },
            }, null);

        Assert.Equal("InsufficientStock", DesktopErrors.Key(exception));
        Assert.Equal("D_Toast_Validation", DesktopOperation.TitleKey("InsufficientStock"));

        var details = DesktopOperation.Details(exception);
        Assert.NotNull(details);
        Assert.Contains("HTTP 422", details, StringComparison.Ordinal);
        Assert.Contains("inventory.negative_stock_blocked", details, StringComparison.Ordinal);
        Assert.Contains("CEM-50", details, StringComparison.Ordinal);
        Assert.Contains("WH-CEMENT", details, StringComparison.Ordinal);
        Assert.Contains("المتاح 2", details, StringComparison.Ordinal);

        var arabic = new LocalizationService("ar")["D_InsufficientStock"];
        var english = new LocalizationService("en")["D_InsufficientStock"];
        Assert.Contains("المستودع", arabic, StringComparison.Ordinal);
        Assert.Contains("warehouse", english, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory.", arabic, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory.", english, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationDeniedProblemExplainsTheStateRejectionAndKeepsCorrelation()
    {
        var exception = Problem(422, "accounting.operation_denied",
            "The document state does not permit this posting operation.", "corr-denied");

        Assert.Equal("OperationDenied", DesktopErrors.Key(exception));
        Assert.Equal("D_Toast_Validation", DesktopOperation.TitleKey("OperationDenied"));
        var details = DesktopOperation.Details(exception);
        Assert.NotNull(details);
        Assert.Contains("HTTP 422", details, StringComparison.Ordinal);
        Assert.Contains("accounting.operation_denied", details, StringComparison.Ordinal);
        Assert.Contains("corr-denied", details, StringComparison.Ordinal);
        Assert.True(new LocalizationService("ar")["D_OperationDenied"].Contains("المستند", StringComparison.Ordinal));
        Assert.Contains("document", new LocalizationService("en")["D_OperationDenied"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("inventory.negative_reason_required", "NegativeStockReasonRequired", "D_Toast_Validation")]
    [InlineData("inventory.negative_override_denied", "NegativeStockApprovalDenied", "D_Toast_Permission")]
    [InlineData("inventory.negative_cost_unknown", "NegativeStockCostUnknown", "D_Toast_Validation")]
    [InlineData("inventory.valuation_mismatch", "ValuationMismatch", "D_Toast_Validation")]
    public void NegativeIssueFailuresMapToLocalizedActionableGuidance(string code, string key, string title)
    {
        var exception = Problem(422, code, "server detail", null);

        Assert.Equal(key, DesktopErrors.Key(exception));
        Assert.Equal(title, DesktopOperation.TitleKey(key));
        foreach (var language in new[] { "ar", "en" })
        {
            var message = new LocalizationService(language)["D_" + key];
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.NotEqual("D_" + key, message);
            Assert.DoesNotContain("inventory.", message, StringComparison.Ordinal);
        }
    }

    private static PetraApiException<ProblemDetails> Problem(int status, string code, string detail, string? correlation) =>
        new("POST /sales/invoices", status, null, new Dictionary<string, IEnumerable<string>>(),
            new ProblemDetails
            {
                Status = status,
                Code = code,
                Detail = detail,
                CorrelationId = correlation ?? string.Empty,
            }, null);

    [Fact]
    public void GenericValidationMessageStopsQuotingThePasswordRule()
    {
        foreach (var language in new[] { "ar", "en" })
        {
            var localization = new LocalizationService(language);
            Assert.DoesNotContain("كلمة المرور", localization["D_Validation"], StringComparison.Ordinal);
            Assert.DoesNotContain("assword", localization["D_Validation"], StringComparison.Ordinal);
            Assert.NotEmpty(localization["D_PasswordRule"]);
        }

        Assert.Equal("D_Toast_Validation", DesktopOperation.TitleKey("PasswordRule"));
    }
}
