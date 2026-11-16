using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Reporting;

public sealed class ProfessionalReportChoice<T>(T value, string label, bool supported, string? reason, bool selected) : ObservableObject where T : struct, Enum
{
    private bool isSelected = supported && selected;
    private string displayLabel = label;
    public T Value { get; } = value;
    public string Label { get => displayLabel; set => SetProperty(ref displayLabel, value); }
    public bool IsSupported { get; } = supported;
    public string? Reason { get; } = reason;
    public bool IsSelected
    {
        get => isSelected;
        set { if (IsSupported) SetProperty(ref isSelected, value); }
    }
}

public sealed record ProfessionalReportSelection<T>(T Value, string Label) where T : struct, Enum;

/// <summary>Optional enum choice; a null value means "no restriction".</summary>
public sealed record ProfessionalOptionalSelection<T>(T? Value, string Label) where T : struct, Enum;

/// <summary>A server-listed document pattern revision. Selecting any pattern narrows only its own source.</summary>
public sealed class ProfessionalPatternChoice(ProfessionalPatternDto pattern, bool selected) : ObservableObject
{
    private bool isSelected = selected;
    public Guid RevisionId { get; } = pattern.RevisionId;
    public ProfessionalReportSource Source { get; } = pattern.Source;
    public string Code { get; } = pattern.Code;
    public string Name { get; } = pattern.NameArabic;
    public string Label => Code + " · " + Name;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}
