using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

public static class DesktopErrors
{
    public static string Key(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is SessionUnavailableException) return "SessionEnded";
        if (exception is PostingOutcomeException outcome) return outcome.Posted ? "PostingAlreadyCompleted" : "PostingNotCompleted";
        if (exception is HttpRequestException http)
            return http.HttpRequestError == HttpRequestError.SecureConnectionError || http.InnerException is AuthenticationException ? "TlsFailure" : "ServerUnavailable";
        if (exception is OperationCanceledException or TimeoutException) return "ServerUnavailable";
        if (exception is IOException or UnauthorizedAccessException or CryptographicException) return "StorageFailure";
        if (exception is not PetraApiException api) return "RequestFailed";
        return Code(api) switch
        {
            "auth.invalid_credentials" => "InvalidCredentials",
            "auth.invalid_current_password" => "CurrentPasswordInvalid",
            "security.cannot_disable_self" => "CannotDisableSelf",
            "security.last_administrator" => "LastAdministrator",
            "auth.locked_out" => "AccountLocked",
            "auth.account_disabled" or "auth.user_inactive" => "AccountDisabled",
            "auth.session_revoked" or "auth.session_expired" or "auth.invalid_refresh_token" or "auth.refresh_token_reused" => "SessionEnded",
            "auth.password_change_required" => "PasswordRequired",
            "auth.company_scope_denied" or "auth.branch_scope_denied" => "ContextUnavailable",
            "ledger.report_rate_invalid" or "ledger.report_currency_invalid" => "Validation",
            "ledger.period_closed" => "PeriodClosed",
            "ledger.period_missing" => "PeriodMissing",
            "ledger.account_invalid" => "AccountInvalid",
            "ledger.cost_center_required" => "CostCenterRequired",
            "ledger.cost_center_not_allowed" => "CostCenterNotAllowed",
            "ledger.cost_center_invalid" => "CostCenterInvalid",
            "ledger.currency_invalid" => "CurrencyInvalid",
            "ledger.journal_state_invalid" => "JournalStateInvalid",
            "ledger.journal_unbalanced" => "JournalUnbalanced",
            "ledger.posting_conflict" => "PostingConflict",
            "ledger.idempotency_conflict" => "IdempotencyConflict",
            "ledger.scope_inactive" => "ScopeInactive",
            "concurrency.conflict" => "Conflict",
            "ledger.account_code_taken" => "AccountCodeTaken",
            "ledger.cost_center_code_taken" => "CostCenterCodeTaken",
            "ledger.account_parent_invalid" => "AccountParentInvalid",
            "ledger.account_hierarchy_cycle" => "AccountHierarchyCycle",
            "ledger.account_in_use" => "AccountInUse",
            "ledger.account_has_children" => "AccountHasChildren",
            "ledger.cost_center_in_use" => "CostCenterInUse",
            "ledger.fiscal_year_overlap" => "FiscalYearOverlap",
            "ledger.fiscal_year_code_taken" => "FiscalYearOverlap",
            "ledger.fiscal_year_invalid" => "FiscalYearInvalid",
            "ledger.period_transition_invalid" => "PeriodTransitionInvalid",
            "ledger.report_range_invalid" => "ReportRangeInvalid",
            "currency.invalid" or "currency.not_enabled" or "currency.not_defined" or "currency.inactive"
                or "sales.currency_invalid" or "purchases.currency_invalid" or "treasury.currency_invalid" or "inventory.currency_invalid" => "CurrencyInvalid",
            "currency.rate_invalid" or "sales.rate_invalid" or "purchases.rate_invalid" or "treasury.rate_invalid" => "CurrencyRateInvalid",
            "currency.rate_missing" => "CurrencyRateMissing",
            "backup.password_required" => "BackupPasswordRequired",
            "backup.password_invalid" => "BackupPasswordInvalid",
            "backup.archive_corrupt" => "BackupArchiveCorrupt",
            "backup.password_too_short" => "BackupPasswordShort",
            "currency.base_locked" => "CurrencyBaseLocked",
            "currency.fx_mapping_missing" => "CurrencyFxMapping",
            "inventory.conversion_dimension" or "inventory.unit_dimension_invalid" or "inventory.unit_dimension_locked" => "UnitDimensionInvalid",
            "inventory.negative_stock_blocked" => "InsufficientStock",
            "inventory.negative_reason_required" => "NegativeStockReasonRequired",
            "inventory.negative_override_denied" => "NegativeStockApprovalDenied",
            "inventory.negative_cost_unknown" => "NegativeStockCostUnknown",
            "inventory.valuation_mismatch" or "inventory.valuation_precision" => "ValuationMismatch",
            "accounting.operation_denied" => "OperationDenied",
            "inventory.quantity_unit_precision" => "UnitPrecisionInvalid",
            "inventory.conversion_duplicate" => "UnitConversionDuplicate",
            "inventory.price_invalid" or "inventory.price_validity_invalid" or "inventory.price_list_invalid" => "PriceInvalid",
            _ => api.StatusCode switch { 401 => "SessionEnded", 403 => "PermissionDenied", 409 => "Conflict", 423 => "AccountLocked", 429 => "RateLimited", 400 or 422 => "Validation", >= 500 => "ServerUnavailable", _ => "RequestFailed" },
        };
    }
    public static string? Code(PetraApiException exception)
    {
        // Streamed generated-client responses keep the problem in Result and leave Response empty.
        if (exception is PetraApiException<ProblemDetails> { Result.Code: { Length: > 0 } problemCode }) return problemCode;
        try
        {
            using var document = JsonDocument.Parse(exception.Response ?? "{}");
            return document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
    public static bool EndsSession(Exception exception) => exception is SessionUnavailableException ||
        exception is PetraApiException api && (api.StatusCode == 401 || Code(api) is "auth.account_disabled" or "auth.user_inactive");

    /// <summary>Server-provided problem detail and correlation identifier (RFC 9457) for actionable user-facing diagnostics.</summary>
    public static string? Diagnostics(PetraApiException exception)
    {
        // A typed client call deserializes the body into Result and leaves Response empty, so reading only the raw
        // JSON would drop the server's reason for every 400 and leave the operator with a message with nothing to fix.
        if (exception is PetraApiException<ProblemDetails> { Result: { } problem })
        {
            var fieldErrors = problem.Errors is { Count: > 0 } errors
                ? string.Join(" · ", errors.Select(entry => entry.Key + ": " + string.Join(", ", entry.Value)))
                : null;
            return Join(problem.Detail, fieldErrors, Label(problem.CorrelationId));
        }

        try
        {
            using var document = JsonDocument.Parse(exception.Response ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var detail = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var correlation = root.TryGetProperty("extensions", out var e) && e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("correlationId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            correlation ??= root.TryGetProperty("correlationId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            return Join(detail, null, Label(correlation));
        }
        catch (JsonException) { return null; }
    }

    private static string? Label(string? correlationId) =>
        correlationId is { Length: > 0 } id ? "معرف الارتباط / Correlation ID: " + id : null;

    private static string? Join(params string?[] parts)
    {
        var combined = string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return combined.Length == 0 ? null : combined;
    }
}
