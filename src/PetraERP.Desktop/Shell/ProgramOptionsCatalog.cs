using System.Text;

namespace PetraERP.Desktop.Shell;

/// <summary>Who owns an option, so the window can say it plainly (local user preference vs company server policy vs a link).</summary>
public enum OptionScope
{
    /// <summary>Presentation preference of this user on this computer. Never financial.</summary>
    LocalUser = 0,
    /// <summary>Company financial policy: saved on the server, needs a permission and an audit reason, applied only on Apply/OK.</summary>
    Company = 1,
    /// <summary>Not an option page: a shortcut to the management screen that owns the settings.</summary>
    Link = 2,
}

/// <summary>A category of the program options window. <see cref="Id"/> matches the <c>Section_{Id}</c> panel of the window.</summary>
public sealed record OptionCategory(string Id, string TitleKey, OptionScope Scope, string? Route = null);

/// <summary>One searchable option; <see cref="ElementName"/> is the XAML name of its field inside the category panel.</summary>
public sealed record OptionEntry(string CategoryId, string TitleKey, string ElementName);

/// <summary>A search hit: the category, the option (null when only the category matched), its caption and its breadcrumb.</summary>
public sealed record OptionSearchResult(OptionCategory Category, OptionEntry? Entry, string Title, string Path)
{
    public string Display => Path;
}

/// <summary>
/// What the program options window contains. Only options that exist and work are listed; areas owned by another screen
/// (users, branches) are links, never copies of settings. The catalogue holds no UI, so search is unit-testable.
/// </summary>
public static class ProgramOptionsCatalog
{
    public static readonly IReadOnlyList<OptionCategory> Categories =
    [
        new("addition_policies", "OPT_Cat_AdditionPolicies", OptionScope.LocalUser),
        new("editing_policies", "OPT_Cat_EditingPolicies", OptionScope.LocalUser),
        new("appearance", "OPT_Cat_Appearance", OptionScope.LocalUser),
        new("language", "OPT_Cat_Language", OptionScope.LocalUser),
        new("numbers", "OPT_Cat_Numbers", OptionScope.LocalUser),
        new("posting", "OPT_Cat_Posting", OptionScope.Company),
        new("numbering", "OPT_Cat_Numbering", OptionScope.Company),
        new("documents", "OPT_Cat_Documents", OptionScope.Company),
        new("accounts", "OPT_Cat_Accounts", OptionScope.Company),
        new("items", "OPT_Cat_Items", OptionScope.Company),
        new("layouts", "OPT_Cat_Layouts", OptionScope.LocalUser),
        new("entry", "OPT_Cat_Entry", OptionScope.LocalUser),
        new("warnings", "OPT_Cat_Warnings", OptionScope.LocalUser),
        new("shortcuts", "OPT_Cat_Shortcuts", OptionScope.LocalUser),
        new("users", "OPT_Cat_Users", OptionScope.Link, "users"),
        new("branches", "OPT_Cat_Branches", OptionScope.Link, "branches"),
    ];

    public static readonly IReadOnlyList<OptionEntry> Entries =
    [
        new("addition_policies", "OPT_E_AutoGenerateCode", "AutoGenerateCodeField"),
        new("addition_policies", "OPT_E_SalesCredit", "AddSalesCreditField"),
        new("addition_policies", "OPT_E_PurchaseCredit", "AddPurchaseCreditField"),
        new("addition_policies", "OPT_E_InvoiceRows", "AddInvoiceRowsField"),
        new("addition_policies", "OPT_E_DuplicateItem", "AddDuplicateItemField"),
        new("addition_policies", "OPT_E_MaxStock", "AddMaxStockField"),
        new("addition_policies", "OPT_E_PastDate", "AddPastDateField"),
        new("addition_policies", "OPT_E_ConfirmAdd", "ConfirmAddField"),
        new("editing_policies", "OPT_E_PreventCodeMod", "PreventCodeModField"),
        new("editing_policies", "OPT_E_PreventPostedEdit", "PreventPostedEditField"),
        new("editing_policies", "OPT_E_WarnBelowCost", "WarnBelowCostField"),
        new("editing_policies", "OPT_E_WarnPriceChange", "WarnPriceChangeField"),
        new("editing_policies", "OPT_E_ConfirmEdit", "ConfirmEditField"),
        new("editing_policies", "OPT_E_ConfirmDiscard", "ConfirmDiscardField"),
        new("appearance", "UI_Theme", "ThemeField"),
        new("appearance", "UI_Skin", "SkinField"),
        new("appearance", "UI_Density", "DensityField"),
        new("appearance", "UI_WindowMode", "WindowModeField"),
        new("language", "UX_Language", "LanguageField"),
        new("numbers", "UI_NumberDisplay", "NumbersField"),
        new("numbers", "OPT_E_Decimal", "DecimalField"),
        new("posting", "OPT_E_PostingMode", "PostingModeField"),
        new("posting", "OPT_E_Draft", "DraftField"),
        new("posting", "OPT_E_Approval", "ApprovalField"),
        new("numbering", "OPT_E_NumberingScope", "NumberingScopeField"),
        new("numbering", "OPT_E_InitialSeq", "InitialSeqField"),
        new("numbering", "OPT_E_ManualNumbering", "ManualNumberingField"),
        new("numbering", "OPT_E_PatternOverride", "PatternOverrideField"),
        new("documents", "OPT_E_DefaultPatterns", "PatternsField"),
        new("documents", "OPT_E_CreditSales", "CreditSalesField"),
        new("documents", "OPT_E_CreditPurchases", "CreditPurchasesField"),
        new("accounts", "OPT_E_CostCenterPL", "CostCenterField"),
        new("items", "OPT_E_NegativeIssue", "NegativeIssueField"),
        new("entry", "OPT_E_SalesCredit", "SalesCreditField"),
        new("entry", "OPT_E_PurchaseCredit", "PurchaseCreditField"),
        new("entry", "OPT_E_InvoiceRows", "InvoiceRowsField"),
        new("warnings", "OPT_E_MaxStock", "MaxStockField"),
        new("warnings", "OPT_E_PastDate", "PastDateField"),
        new("warnings", "OPT_E_DuplicateItem", "DuplicateItemField"),
        new("warnings", "OPT_E_ConfirmSave", "ConfirmSaveField"),
        new("warnings", "OPT_E_ConfirmExit", "ConfirmExitField"),
        new("layouts", "UI_ResetLayouts", "LayoutsField"),
        new("layouts", "UI_SavedFilters", "FiltersField"),
        new("shortcuts", "UI_KeyboardTitle", "ShortcutsField"),
    ];

    /// <summary>
    /// Case-insensitive search that ignores Arabic diacritics, tatweel and the usual letter variants (أ إ آ ا، ى ي، ة ه), over the
    /// option captions and the category captions. An empty query returns nothing.
    /// </summary>
    /// <param name="query">Text typed by the user.</param>
    /// <param name="label">Localizes a caption key.</param>
    /// <param name="available">Hides categories the user cannot use (no company open, no permission); null shows all.</param>
    public static IReadOnlyList<OptionSearchResult> Search(string? query, Func<string, string> label, Func<OptionCategory, bool>? available = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        var needle = Normalize(query);
        if (needle.Length == 0) return [];
        var results = new List<OptionSearchResult>();
        foreach (var category in Categories.Where(category => available?.Invoke(category) ?? true))
        {
            var categoryTitle = label(category.TitleKey);
            if (Normalize(categoryTitle).Contains(needle, StringComparison.Ordinal))
                results.Add(new OptionSearchResult(category, null, categoryTitle, categoryTitle));
            foreach (var entry in Entries.Where(entry => entry.CategoryId == category.Id))
            {
                var title = label(entry.TitleKey);
                if (Normalize(title).Contains(needle, StringComparison.Ordinal))
                    results.Add(new OptionSearchResult(category, entry, title, categoryTitle + " ◂ " + title));
            }
        }

        return results;
    }

    /// <summary>Upper-cases, strips Arabic marks and unifies the common letter variants.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.Trim())
        {
            if (raw is (>= 'ً' and <= 'ٟ') or 'ـ') continue;
            var ch = raw switch { 'أ' or 'إ' or 'آ' => 'ا', 'ى' => 'ي', 'ة' => 'ه', _ => raw };
            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }
}
