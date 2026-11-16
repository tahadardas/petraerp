namespace PetraERP.Desktop.Shell;

/// <summary>
/// The save-time prompts of the program options: a repeated item on an invoice, general/addition/editing confirmations,
/// and advisory price alerts. Both are advisory questions to the user of this computer; the server rules (stock, credit, posting) are never overridden here.
/// </summary>
public static class EntrySaveGuard
{
    public static bool HasDuplicateItem(IEnumerable<Guid?> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        return itemIds.Where(id => id is not null).GroupBy(id => id).Any(group => group.Count() > 1);
    }

    /// <summary>True when the save may go on: each enabled option that applies has been answered with yes.</summary>
    public static async Task<bool> AllowsAsync(IEnumerable<Guid?> itemIds, IUserConfirmation confirmation, EntryOptions? entryOptions = null, bool isNew = true)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var options = entryOptions ?? EntryOptionsHub.Current;
        if (options.DuplicateItemWarning && HasDuplicateItem(itemIds) && !await confirmation.ConfirmAsync("D_ConfirmDuplicateItem").ConfigureAwait(true)) return false;

        if (isNew)
        {
            if (options.ConfirmOnAdd && !await confirmation.ConfirmAsync("D_ConfirmAdd").ConfigureAwait(true)) return false;
        }
        else
        {
            if (options.ConfirmOnEdit && !await confirmation.ConfirmAsync("D_ConfirmEdit").ConfigureAwait(true)) return false;
        }

        return !options.ConfirmOnSave || await confirmation.ConfirmAsync("D_ConfirmSave").ConfigureAwait(true);
    }

    /// <summary>Advisory confirmation for unit price edited below cost or changed from default price.</summary>
    public static async Task<bool> AllowsPriceAsync(bool isBelowCost, bool isChangedFromQuote, IUserConfirmation confirmation, EntryOptions? entryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var options = entryOptions ?? EntryOptionsHub.Current;
        if (options.WarnOnBelowCost && isBelowCost && !await confirmation.ConfirmAsync("D_ConfirmBelowCost").ConfigureAwait(true)) return false;
        if (options.WarnOnPriceChange && isChangedFromQuote && !await confirmation.ConfirmAsync("D_ConfirmPriceChange").ConfigureAwait(true)) return false;
        return true;
    }
}
