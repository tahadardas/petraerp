namespace PetraERP.Desktop.Shell;

/// <summary>The answer to a three-way prompt: Cancel (the default), the confirm action, or the alternate action.</summary>
public enum DialogChoice
{
    Cancel,
    Confirm,
    Alternate,
}

public interface IUserConfirmation
{
    Task<bool> ConfirmAsync(string key);

    /// <summary>
    /// Asks with a third, alternate action (for example Save when closing a dirty document). Implementations without an
    /// alternate fall back to the two-way confirmation, so callers never get <see cref="DialogChoice.Alternate"/> from them.
    /// </summary>
    async Task<DialogChoice> ChooseAsync(string key, string alternateKey) =>
        await ConfirmAsync(key).ConfigureAwait(true) ? DialogChoice.Confirm : DialogChoice.Cancel;
}
