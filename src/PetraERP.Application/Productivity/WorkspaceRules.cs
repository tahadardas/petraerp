using PetraERP.Application.Common;
using PetraERP.Application.Security;

namespace PetraERP.Application.Productivity;

public static class WorkspaceRules
{
    public const int MaximumRecent = 20;

    /// <summary>
    /// Whitelisted grid layouts: stable grid key → allowed column keys. Layouts are presentation metadata only;
    /// unknown grids or columns are rejected so no free-form content can be persisted.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GridColumns { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["workspace.results"] = ["Code", "Name", "Status", "Date", "Category"],
        ["document.lines"] = ["Account", "Description", "Currency", "Debit", "Credit"],
        ["ledger.journals"] = ["Date", "Number", "Description", "State", "Debit", "Credit", "Currency"],
        ["ledger.journal-lines"] = ["Account", "AccountName", "Description", "Currency", "Rate", "Debit", "Credit", "CostCenter"],
        ["system.users"] = ["UserName", "DisplayNameArabic", "DisplayNameEnglish", "IsActive", "IsLockedOut", "MustChangePassword"],
        ["system.roles"] = ["Code", "NameArabic", "NameEnglish", "PermissionCount", "UserCount"],
        ["system.audit"] = ["OccurredAtUtc", "UserNameSnapshot", "Reason", "ActorUserId", "CompanyId", "BranchId", "CorrelationId"],
        ["ledger.accounts"] = ["Code", "Name", "Type", "Posting", "Status"],
        ["ledger.cost-centers"] = ["Code", "Name", "Status"],
        ["ledger.periods"] = ["Code", "Start", "End", "State"],
        ["ledger.general-journal"] = ["Date", "Number", "Account", "AccountName", "Description", "CostCenter", "Debit", "Credit", "State"],
        ["ledger.account-ledger"] = ["Date", "Number", "Description", "CostCenter", "Debit", "Credit", "Balance"],
        // Analytical report columns are server-defined per report; layouts are positional (c0..c15).
        ["reports.analytical"] = ["c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c8", "c9", "c10", "c11", "c12", "c13", "c14", "c15"],
        ["ledger.trial-balance"] = ["Account", "Name", "OpeningDebit", "OpeningCredit", "PeriodDebit", "PeriodCredit", "EndingDebit", "EndingCredit"],
    };
    public static IReadOnlyList<WorkspaceCommand> Commands { get; } = Array.AsReadOnly(new[]
    {
        new WorkspaceCommand("workspace.search", "بحث شامل", "Global search", null),
        new WorkspaceCommand("accounts.browse", "دليل الحسابات", "Chart of accounts", SearchCategory.Accounts),
        new WorkspaceCommand("general-journal.report", "دفتر اليومية", "General journal", null),
        new WorkspaceCommand("account-ledger.report", "كشف حساب", "Account ledger", null),
        new WorkspaceCommand("trial-balance.report", "ميزان المراجعة", "Trial balance", null),
        new WorkspaceCommand("journals.browse", "القيود اليومية", "Journal entries", SearchCategory.JournalEntries),
    });
    public static IReadOnlySet<string> ReportIds { get; } = new HashSet<string>(["general-journal.report", "account-ledger.report", "trial-balance.report"], StringComparer.Ordinal);
    public static string? CommandPermission(WorkspaceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ReportIds.Contains(command.Id) ? PermissionCatalog.ReportsView : command.Category is { } category ? Permission(category) : null;
    }
    public static string? Permission(SearchCategory category) => category switch
    {
        SearchCategory.Accounts => PermissionCatalog.AccountsView,
        SearchCategory.JournalEntries => PermissionCatalog.JournalsView,
        _ => null,
    };
    public static void Validate(WorkspacePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Language is not ("ar" or "en") || preferences.Theme is not ("Light" or "Dark" or "System"))
            throw Invalid("Unknown language or theme.");
        if (preferences.FavoriteCommands is null || preferences.FavoriteReports is null || preferences.Grids is null
            || preferences.FavoriteCommands.Count > 30 || preferences.FavoriteReports.Count > ReportIds.Count || preferences.Grids.Count > GridColumns.Count)
            throw Invalid("Preference collection limits exceeded, or report limit exceeded.");
        if (preferences.FavoriteCommands.Distinct(StringComparer.Ordinal).Count() != preferences.FavoriteCommands.Count
            || preferences.FavoriteCommands.Any(id => !Commands.Any(command => command.Id == id)))
            throw Invalid("Unknown or duplicate command.");
        if (preferences.FavoriteReports.Distinct(StringComparer.Ordinal).Count() != preferences.FavoriteReports.Count
            || preferences.FavoriteReports.Any(id => !ReportIds.Contains(id))) throw Invalid("Unknown or duplicate report.");
        if (preferences.Grids.Select(grid => grid.Key).Distinct(StringComparer.Ordinal).Count() != preferences.Grids.Count)
            throw Invalid("Duplicate layout.");
        foreach (var grid in preferences.Grids)
        {
            if (grid.Key is null || !GridColumns.TryGetValue(grid.Key, out var keys) || grid.Columns is null || grid.Columns.Count > keys.Count)
                throw Invalid("Unknown grid or too many columns.");
            if (grid.Columns.Select(column => column.Key).Distinct(StringComparer.Ordinal).Count() != grid.Columns.Count
                || grid.Columns.Select(column => column.Order).Distinct().Count() != grid.Columns.Count
                || grid.Columns.Any(column => !keys.Contains(column.Key, StringComparer.Ordinal)
                    || column.Width is < 40 or > 1000 || column.Order < 0 || column.Order >= keys.Count)
                || (grid.Columns.Count > 0 && grid.Columns.All(column => !column.Visible)))
                throw Invalid("Invalid columns, dimensions, ordering or visibility.");
        }
    }
    private static RequestValidationException Invalid(string message) => new("workspace.preferences_invalid", message, "preferences");
}
