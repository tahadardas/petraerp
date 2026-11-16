using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>What is wrong with a document date, if anything.</summary>
public enum DocumentDateIssue
{
    None,
    BeforeFiscalYear,
    AfterFiscalYear,
    EarlierThanToday,
}

/// <summary>
/// The Al-Ameen date rules for every document (invoice, voucher, entry): a date outside the working fiscal year is refused, and a date
/// earlier than today is allowed only after the user confirms. The server stays authoritative; this is the immediate, readable check.
/// </summary>
public static class DocumentDateGuard
{
    public static DocumentDateIssue Evaluate(DateOnly date, DateOnly today, DateOnly? yearStart, DateOnly? yearEnd)
    {
        if (yearStart is { } start && date < start) return DocumentDateIssue.BeforeFiscalYear;
        if (yearEnd is { } end && date > end) return DocumentDateIssue.AfterFiscalYear;
        return date < today ? DocumentDateIssue.EarlierThanToday : DocumentDateIssue.None;
    }

    /// <summary>
    /// Returns true when the document may be saved with this date. A refused date reports its message through <paramref name="reject"/>;
    /// an earlier-than-today date asks the user first.
    /// </summary>
    public static async Task<bool> AllowsAsync(DateTime? date, ShellContext? context, ILocalizationService localization, IUserConfirmation confirmation, Action<string> reject, EntryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(reject);
        if (date is null) return true;
        var issue = Evaluate(DateOnly.FromDateTime(date.Value), DateOnly.FromDateTime(DateTime.Today), context?.FiscalYearStart, context?.FiscalYearEnd);
        switch (issue)
        {
            case DocumentDateIssue.BeforeFiscalYear:
            case DocumentDateIssue.AfterFiscalYear:
                reject(string.Format(System.Globalization.CultureInfo.InvariantCulture, localization["D_DateOutsideFiscalYear"],
                    context?.FiscalYearStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    context?.FiscalYearEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
                return false;
            case DocumentDateIssue.EarlierThanToday:
                return !(options ?? EntryOptionsHub.Current).PastDateWarning || await confirmation.ConfirmAsync("D_ConfirmPastDate").ConfigureAwait(true);
            default:
                return true;
        }
    }
}
