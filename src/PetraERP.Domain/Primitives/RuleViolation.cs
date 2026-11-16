namespace PetraERP.Domain.Primitives;

/// <summary>
/// Stable, machine-readable codes attached to accounting rule violations. The exception type stays
/// <see cref="InvalidOperationException"/>; the code travels in <see cref="Exception.Data"/> so outer layers
/// can map it to localized client messages without parsing diagnostic text.
/// </summary>
public static class RuleViolation
{
    public const string DataKey = "PetraERP.RuleCode";

    public const string PeriodClosed = "ledger.period_closed";
    public const string PeriodMissing = "ledger.period_missing";
    public const string AccountInvalid = "ledger.account_invalid";
    public const string CostCenterRequired = "ledger.cost_center_required";
    public const string CostCenterNotAllowed = "ledger.cost_center_not_allowed";
    public const string CostCenterInvalid = "ledger.cost_center_invalid";
    public const string CurrencyInvalid = "ledger.currency_invalid";
    public const string JournalStateInvalid = "ledger.journal_state_invalid";
    public const string JournalUnbalanced = "ledger.journal_unbalanced";
    public const string PostingConflict = "ledger.posting_conflict";
    public const string ScopeInactive = "ledger.scope_inactive";
    public const string PeriodTransitionInvalid = "ledger.period_transition_invalid";
    public const string AccountParentInvalid = "ledger.account_parent_invalid";
    public const string AccountHierarchyCycle = "ledger.account_hierarchy_cycle";
    public const string AccountInUse = "ledger.account_in_use";
    public const string AccountHasChildren = "ledger.account_has_children";
    public const string CostCenterInUse = "ledger.cost_center_in_use";

    public const string PatternRevisionImmutable = "patterns.revision_immutable";
    public const string PatternOperationNotRunnable = "patterns.operation_not_runnable";
    public const string PatternRevisionInvalidState = "patterns.revision_invalid_state";
    public const string PatternDraftAlreadyExists = "patterns.draft_already_exists";
    public const string PatternOverrideNotAllowed = "patterns.override_not_allowed";
    public const string PatternAccountUnresolved = "patterns.account_unresolved";
    public const string PatternNoActiveRevision = "patterns.no_active_revision";

    public const string PartyCodeDuplicate = "parties.code_duplicate";
    public const string PartyRoleInvalid = "parties.role_invalid";
    public const string PartyAccountCrossCompany = "parties.account_cross_company";
    public const string PartyAccountInvalid = "parties.account_invalid";
    public const string PartyCreditLimitExceeded = "parties.credit_limit_exceeded";
    public const string PartyInactive = "parties.inactive";
    public const string PartyInUse = "parties.in_use";

    public const string ItemCodeDuplicate = "inventory.item_code_duplicate";
    public const string ItemInactive = "inventory.item_inactive";
    public const string ItemInUse = "inventory.item_in_use";
    public const string NegativeStockBlocked = "inventory.negative_stock_blocked";
    public const string WarehouseInactive = "inventory.warehouse_inactive";
    public const string UnitConversionInvalid = "inventory.unit_conversion_invalid";
    public const string WarehouseTransferInvalid = "inventory.transfer_invalid";
    public const string CostCenterRequiredForIssue = "inventory.cost_center_required_for_issue";
    public const string BarcodeDuplicate = "inventory.barcode_duplicate";

    public static InvalidOperationException Create(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var exception = new InvalidOperationException(message);
        exception.Data[DataKey] = code;
        return exception;
    }

    /// <summary>Attaches a rule code to an exception whose public type existing callers already depend on.</summary>
    public static TException Tag<TException>(TException exception, string code) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        exception.Data[DataKey] = code;
        return exception;
    }

    public static string? CodeOf(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[DataKey] as string;
    }
}
