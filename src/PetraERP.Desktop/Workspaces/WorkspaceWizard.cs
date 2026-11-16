using CommunityToolkit.Mvvm.ComponentModel;

namespace PetraERP.Desktop.Workspaces;

public enum WorkspaceWizardKind { None, Company, FiscalYear }

/// <summary>One numbered step in the wizard header.</summary>
public sealed partial class WorkspaceWizardStep(int number, string title) : ObservableObject
{
    [ObservableProperty] private bool isCurrent;
    [ObservableProperty] private bool isDone;
    public int Number { get; } = number;
    public string Title { get; } = title;
}
