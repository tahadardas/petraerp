using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PetraERP.ApiClient;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

public sealed class FilterOperatorItem
{
    public PetraReportFilterOperator Operator { get; }
    public string Label { get; }
    public FilterOperatorItem(PetraReportFilterOperator op, string label) { Operator = op; Label = label; }
}

public sealed class FilterConditionViewModel : INotifyPropertyChanged
{
    private string columnKey;
    private PetraReportFilterOperator op;
    private string? val;
    private string? secondVal;
    private bool isEnabled = true;

    public FilterConditionViewModel(string columnKey, PetraReportFilterOperator op = PetraReportFilterOperator.Equals, string? value = null, string? secondValue = null)
    {
        this.columnKey = columnKey;
        this.op = op;
        val = value;
        secondVal = secondValue;
    }

    public string ColumnKey
    {
        get => columnKey;
        set { if (columnKey != value) { columnKey = value; OnPropertyChanged(); } }
    }

    public PetraReportFilterOperator Operator
    {
        get => op;
        set { if (op != value) { op = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBetween)); OnPropertyChanged(nameof(NeedsValue)); } }
    }

    public string? Value
    {
        get => val;
        set { if (val != value) { val = value; OnPropertyChanged(); } }
    }

    public string? SecondValue
    {
        get => secondVal;
        set { if (secondVal != value) { secondVal = value; OnPropertyChanged(); } }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set { if (isEnabled != value) { isEnabled = value; OnPropertyChanged(); } }
    }

    public bool IsBetween => op == PetraReportFilterOperator.Between;
    public bool NeedsValue => op is not PetraReportFilterOperator.IsEmpty and not PetraReportFilterOperator.IsNotEmpty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FilterGroupViewModel : INotifyPropertyChanged
{
    private PetraReportFilterLogic logic;
    private bool isEnabled = true;

    public FilterGroupViewModel(PetraReportFilterLogic logic = PetraReportFilterLogic.And, FilterGroupViewModel? parent = null)
    {
        this.logic = logic;
        Parent = parent;
        Conditions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
        SubGroups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public FilterGroupViewModel? Parent { get; }

    public PetraReportFilterLogic Logic
    {
        get => logic;
        set { if (logic != value) { logic = value; OnPropertyChanged(); OnPropertyChanged(nameof(LogicText)); } }
    }

    public string LogicText => logic == PetraReportFilterLogic.And ? "AND (كل الشروط)" : "OR (أي شرط)";

    public bool IsEnabled
    {
        get => isEnabled;
        set { if (isEnabled != value) { isEnabled = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<FilterConditionViewModel> Conditions { get; } = [];
    public ObservableCollection<FilterGroupViewModel> SubGroups { get; } = [];

    public bool HasChildren => Conditions.Count > 0 || SubGroups.Count > 0;

    public void AddCondition(string columnKey, PetraReportFilterOperator op = PetraReportFilterOperator.Equals, string? value = null)
    {
        var condition = new FilterConditionViewModel(columnKey, op, value);
        condition.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Conditions));
        Conditions.Add(condition);
    }

    public FilterGroupViewModel AddSubGroup(PetraReportFilterLogic subLogic = PetraReportFilterLogic.Or)
    {
        var group = new FilterGroupViewModel(subLogic, this);
        group.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SubGroups));
        SubGroups.Add(group);
        return group;
    }

    public void RemoveCondition(FilterConditionViewModel condition) => Conditions.Remove(condition);
    public void RemoveSubGroup(FilterGroupViewModel group) => SubGroups.Remove(group);

    public void Clear()
    {
        Conditions.Clear();
        SubGroups.Clear();
    }

    public PetraReportFilterNode? Build()
    {
        if (!IsEnabled) return null;
        var nodes = new List<PetraReportFilterNode>();
        foreach (var cond in Conditions)
        {
            if (cond.IsEnabled && (!cond.NeedsValue || !string.IsNullOrWhiteSpace(cond.Value)))
            {
                nodes.Add(new PetraReportFilterCondition(
                    cond.ColumnKey,
                    cond.Operator,
                    cond.Value,
                    cond.SecondValue,
                    Enabled: cond.IsEnabled));
            }
        }
        foreach (var sub in SubGroups)
        {
            var builtSub = sub.Build();
            if (builtSub is not null) nodes.Add(builtSub);
        }
        if (nodes.Count == 0) return null;
        if (nodes.Count == 1 && SubGroups.Count == 0) return nodes[0];
        return new PetraReportFilterGroup(Logic, nodes.AsReadOnly(), IsEnabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PetraReportFilterBuilderViewModel : INotifyPropertyChanged
{
    private ObservableCollection<ReportColumn> availableColumns = [];

    public PetraReportFilterBuilderViewModel()
    {
        Root = new FilterGroupViewModel(PetraReportFilterLogic.And);
        Root.PropertyChanged += (_, _) => RefreshPreview();
    }

    public FilterGroupViewModel Root { get; }

    public ObservableCollection<ReportColumn> AvailableColumns
    {
        get => availableColumns;
        set
        {
            availableColumns = value;
            OnPropertyChanged();
        }
    }

    public string ExpressionPreview => PetraReportFilterEngine.FormatExpression(Root.Build(),
        AvailableColumns.ToDictionary(c => c.Key, c => c.Heading, StringComparer.Ordinal));

    public void SetAvailableColumns(IEnumerable<ReportColumn> columns)
    {
        AvailableColumns = new ObservableCollection<ReportColumn>(columns);
        RefreshPreview();
    }

    public void Clear()
    {
        Root.Clear();
        RefreshPreview();
    }

    public void LoadFrom(PetraReportFilterNode? node)
    {
        Root.Clear();
        if (node is null) { RefreshPreview(); return; }
        PopulateGroup(Root, node);
        RefreshPreview();
    }

    private static void PopulateGroup(FilterGroupViewModel target, PetraReportFilterNode node)
    {
        if (node is PetraReportFilterCondition c)
        {
            target.AddCondition(c.ColumnKey, c.Operator, c.Value);
        }
        else if (node is PetraReportFilterGroup g)
        {
            target.Logic = g.Logic;
            foreach (var child in g.Children)
            {
                if (child is PetraReportFilterCondition childCond)
                {
                    target.AddCondition(childCond.ColumnKey, childCond.Operator, childCond.Value);
                }
                else if (child is PetraReportFilterGroup childGroup)
                {
                    var sub = target.AddSubGroup(childGroup.Logic);
                    PopulateGroup(sub, childGroup);
                }
            }
        }
    }

    public PetraReportFilterNode? Build() => Root.Build();

    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(ExpressionPreview));
        OnPropertyChanged(nameof(HasFilters));
    }

    public bool HasFilters => Root.HasChildren;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
