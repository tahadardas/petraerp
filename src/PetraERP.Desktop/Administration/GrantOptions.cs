using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PetraERP.Desktop.Administration;

public sealed class GrantOption(string key, string label, bool selected, bool active = true) : ObservableObject
{
    private bool selected = selected;
    private string label = label;
    public string Key { get; } = key;
    public string Label { get => label; set => SetProperty(ref label, value); }
    public bool CanSelect => active || IsSelected;
    public bool IsSelected { get => selected; set { SetProperty(ref selected, value); OnPropertyChanged(nameof(CanSelect)); } }
}
public sealed class CompanyGrantOption(Guid id, string label, bool selected, IEnumerable<GrantOption> branches, bool active = true) : ObservableObject
{
    private bool selected = selected;
    private string label = label;
    public Guid Id { get; } = id;
    public string Label { get => label; set => SetProperty(ref label, value); }
    public bool CanSelect => active || IsSelected;
    public bool IsSelected { get => selected; set { SetProperty(ref selected, value); OnPropertyChanged(nameof(CanSelect)); } }
    public ObservableCollection<GrantOption> Branches { get; } = new(branches);
}
