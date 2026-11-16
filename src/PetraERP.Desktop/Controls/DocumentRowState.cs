namespace PetraERP.Desktop.Controls;

/// <summary>
/// Presentation-only row lifecycle states for high-speed multi-line accounting grids.
/// Distinguishes pristine blank entry slots from partially edited, valid complete, and persisted rows.
/// </summary>
public enum DocumentRowState
{
    /// <summary>Untouched empty entry slot. Produces no dirty state, no API/DB records, and no quotation calls.</summary>
    PristineBlank = 0,

    /// <summary>Touched/edited row missing required fields or having validation errors. Must never be silently discarded.</summary>
    Incomplete = 1,

    /// <summary>Complete valid row ready for transmission/serialization to the server.</summary>
    Complete = 2,

    /// <summary>Row loaded from an existing persisted database document.</summary>
    Persisted = 3,
}

/// <summary>
/// Common contract for document entry rows supporting the 50-row accounting buffer model.
/// </summary>
public interface IDocumentEntryRow
{
    DocumentRowState State { get; }
    bool IsPristineBlank { get; }
    bool IsIncomplete { get; }
    bool IsComplete { get; }
    bool IsPersisted { get; }
    string? RowError { get; }

    /// <summary>Stable localization key naming the single reason this row cannot be saved; null when the row is saveable.</summary>
    string? RowErrorCode { get; }
}

/// <summary>
/// Turns partially edited entry rows into the per-row wording an operator can act on, instead of one aggregate
/// "some lines are incomplete" banner that leaves them guessing which of fifty rows to fix.
/// </summary>
public static class DocumentEntryValidation
{
    private const int MaxListedRows = 6;

    public static void AddIncompleteLineErrors(
        List<string> errors,
        Func<string, string> localize,
        string linesCaptionKey,
        IReadOnlyList<IDocumentEntryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(localize);
        ArgumentNullException.ThrowIfNull(rows);

        var listed = 0;
        var hidden = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.State != DocumentRowState.Incomplete) continue;
            if (listed == MaxListedRows) { hidden++; continue; }
            listed++;
            var reason = row.RowErrorCode is { Length: > 0 } key ? localize(key) : localize("D_LineDataIncomplete");
            errors.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                localize("D_LineRowError"), localize(linesCaptionKey), index + 1, reason));
        }

        if (hidden > 0)
            errors.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, localize("D_MoreIncompleteLines"), hidden));
    }
}
