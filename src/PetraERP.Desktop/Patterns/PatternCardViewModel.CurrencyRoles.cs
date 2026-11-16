using CommunityToolkit.Mvvm.ComponentModel;

namespace PetraERP.Desktop.Patterns;

public sealed partial class PatternCardViewModel
{
    [ObservableProperty] private Guid? draftFxGainAccountId;
    [ObservableProperty] private Guid? draftFxLossAccountId;
    [ObservableProperty] private bool draftFxGainEnforced;
    [ObservableProperty] private bool draftFxLossEnforced;

    partial void OnDraftFxGainAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftFxLossAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftFxGainEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftFxLossEnforcedChanged(bool value) => DraftChanged();

    private bool FxMappingsChanged => FxChanged("FxGain", DraftFxGainAccountId, DraftFxGainEnforced)
        || FxChanged("FxLoss", DraftFxLossAccountId, DraftFxLossEnforced);

    private bool FxChanged(string role, Guid? account, bool enforced)
    {
        var original = Pattern?.ActiveRevision?.Configuration?.AccountMappings?
            .FirstOrDefault(m => string.Equals(m.Role?.Trim(), role, StringComparison.OrdinalIgnoreCase));
        return account != original?.AccountId || enforced != (original?.IsRequired ?? false);
    }
}
