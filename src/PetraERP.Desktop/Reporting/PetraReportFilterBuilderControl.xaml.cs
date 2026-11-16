using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

public partial class PetraReportFilterBuilderControl : UserControl
{
    private PetraReportFilterBuilderViewModel? model;

    public PetraReportFilterBuilderControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelPropertyChanged;
        }
        model = args.NewValue as PetraReportFilterBuilderViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelPropertyChanged;
            RebuildUI();
        }
    }

    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PetraReportFilterBuilderViewModel.ExpressionPreview))
        {
            ExpressionPreviewText.Text = model?.ExpressionPreview ?? string.Empty;
        }
        else if (args.PropertyName == nameof(PetraReportFilterBuilderViewModel.AvailableColumns))
        {
            RebuildUI();
        }
    }

    private void RebuildUI()
    {
        ItemsHost.Children.Clear();
        if (model is null) return;

        UpdateGroupHeader(LogicToggle, model.Root);
        ExpressionPreviewText.Text = model.ExpressionPreview;

        RenderGroupChildren(ItemsHost, model.Root);
    }

    private void RenderGroupChildren(Panel container, FilterGroupViewModel group)
    {
        foreach (var cond in group.Conditions)
        {
            container.Children.Add(CreateConditionRow(group, cond));
        }

        foreach (var sub in group.SubGroups)
        {
            container.Children.Add(CreateSubGroupPanel(group, sub));
        }
    }

    private DockPanel CreateConditionRow(FilterGroupViewModel parent, FilterConditionViewModel condition)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var deleteBtn = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = "حذف الشرط / Delete condition",
        };
        DockPanel.SetDock(deleteBtn, Dock.Right);
        deleteBtn.Click += (_, _) =>
        {
            parent.RemoveCondition(condition);
            model?.RefreshPreview();
            RebuildUI();
        };
        row.Children.Add(deleteBtn);

        var enabledCheck = new CheckBox
        {
            IsChecked = condition.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "تفعيل/تعطيل / Enable condition",
        };
        enabledCheck.Checked += (_, _) => { condition.IsEnabled = true; model?.RefreshPreview(); };
        enabledCheck.Unchecked += (_, _) => { condition.IsEnabled = false; model?.RefreshPreview(); };
        row.Children.Add(enabledCheck);

        var colCombo = new ComboBox
        {
            Width = 140,
            DisplayMemberPath = "Heading",
            SelectedValuePath = "Key",
            ItemsSource = model?.AvailableColumns,
            SelectedValue = condition.ColumnKey,
            Margin = new Thickness(0, 0, 4, 0),
        };
        colCombo.SelectionChanged += (_, _) =>
        {
            if (colCombo.SelectedValue is string key)
            {
                condition.ColumnKey = key;
                model?.RefreshPreview();
            }
        };
        row.Children.Add(colCombo);

        var opCombo = new ComboBox
        {
            Width = 120,
            DisplayMemberPath = "Label",
            SelectedValuePath = "Operator",
            ItemsSource = GetOperatorChoices(),
            SelectedValue = condition.Operator,
            Margin = new Thickness(0, 0, 4, 0),
        };

        var val2Box = new TextBox
        {
            Width = 90,
            Text = condition.SecondValue ?? string.Empty,
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = condition.IsBetween ? Visibility.Visible : Visibility.Collapsed,
        };
        val2Box.TextChanged += (_, _) =>
        {
            condition.SecondValue = val2Box.Text;
            model?.RefreshPreview();
        };

        var valBox = new TextBox
        {
            MinWidth = 100,
            Text = condition.Value ?? string.Empty,
            Visibility = condition.NeedsValue ? Visibility.Visible : Visibility.Collapsed,
        };
        valBox.TextChanged += (_, _) =>
        {
            condition.Value = valBox.Text;
            model?.RefreshPreview();
        };

        opCombo.SelectionChanged += (_, _) =>
        {
            if (opCombo.SelectedValue is PetraReportFilterOperator selectedOp)
            {
                condition.Operator = selectedOp;
                valBox.Visibility = condition.NeedsValue ? Visibility.Visible : Visibility.Collapsed;
                val2Box.Visibility = condition.IsBetween ? Visibility.Visible : Visibility.Collapsed;
                model?.RefreshPreview();
            }
        };
        row.Children.Add(opCombo);
        row.Children.Add(valBox);
        row.Children.Add(val2Box);

        return row;
    }

    private Border CreateSubGroupPanel(FilterGroupViewModel parent, FilterGroupViewModel subGroup)
    {
        var border = new Border
        {
            Background = TryFindResource("Surface.Secondary") as Brush ?? Brushes.LightGray,
            BorderBrush = TryFindResource("Border.Normal") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(12, 4, 0, 4),
        };

        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        var removeGroupBtn = new Button
        {
            Content = "حذف المجموعة ×",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 0, 0),
        };
        DockPanel.SetDock(removeGroupBtn, Dock.Right);
        removeGroupBtn.Click += (_, _) =>
        {
            parent.RemoveSubGroup(subGroup);
            model?.RefreshPreview();
            RebuildUI();
        };
        header.Children.Add(removeGroupBtn);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);

        var addCondBtn = new Button
        {
            Content = "+ شرط",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
        };
        addCondBtn.Click += (_, _) =>
        {
            var firstCol = model?.AvailableColumns.FirstOrDefault()?.Key ?? "Date";
            subGroup.AddCondition(firstCol);
            model?.RefreshPreview();
            RebuildUI();
        };
        actions.Children.Add(addCondBtn);

        var addGroupBtn = new Button
        {
            Content = "+ أقواس ()",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 0),
        };
        addGroupBtn.Click += (_, _) =>
        {
            subGroup.AddSubGroup(PetraReportFilterLogic.And);
            model?.RefreshPreview();
            RebuildUI();
        };
        actions.Children.Add(addGroupBtn);
        header.Children.Add(actions);

        var logicBtn = new Button
        {
            MinWidth = 100,
            Padding = new Thickness(6, 2, 6, 2),
            FontWeight = FontWeights.SemiBold,
        };
        UpdateGroupHeader(logicBtn, subGroup);
        logicBtn.Click += (_, _) =>
        {
            subGroup.Logic = subGroup.Logic == PetraReportFilterLogic.And ? PetraReportFilterLogic.Or : PetraReportFilterLogic.And;
            UpdateGroupHeader(logicBtn, subGroup);
            model?.RefreshPreview();
        };
        header.Children.Add(logicBtn);

        stack.Children.Add(header);

        var subItemsHost = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        RenderGroupChildren(subItemsHost, subGroup);
        stack.Children.Add(subItemsHost);

        border.Child = stack;
        return border;
    }

    private static void UpdateGroupHeader(Button button, FilterGroupViewModel group)
    {
        button.Content = group.LogicText;
        var isAnd = group.Logic == PetraReportFilterLogic.And;
        button.SetResourceReference(Control.BackgroundProperty, isAnd ? "Brand.Navy" : "Accent");
        button.SetResourceReference(Control.ForegroundProperty, isAnd ? "Text.OnBrand" : "Text.OnAccent");
    }

    private static IReadOnlyList<FilterOperatorItem> GetOperatorChoices() =>
    [
        new(PetraReportFilterOperator.Equals, "= يساوي"),
        new(PetraReportFilterOperator.NotEquals, "!= لا يساوي"),
        new(PetraReportFilterOperator.GreaterThan, "> أكبر من"),
        new(PetraReportFilterOperator.GreaterThanOrEqual, ">= أكبر أو يساوي"),
        new(PetraReportFilterOperator.LessThan, "< أصغر من"),
        new(PetraReportFilterOperator.LessThanOrEqual, "<= أصغر أو يساوي"),
        new(PetraReportFilterOperator.Between, "بين قيمتين"),
        new(PetraReportFilterOperator.Contains, "يحوي النص"),
        new(PetraReportFilterOperator.DoesNotContain, "لا يحوي النص"),
        new(PetraReportFilterOperator.StartsWith, "يبدأ بـ"),
        new(PetraReportFilterOperator.EndsWith, "ينتهي بـ"),
        new(PetraReportFilterOperator.IsEmpty, "فارغ"),
        new(PetraReportFilterOperator.IsNotEmpty, "غير فارغ"),
        new(PetraReportFilterOperator.In, "ضمن قائمة"),
        new(PetraReportFilterOperator.NotIn, "ليس ضمن قائمة"),
    ];

    private void LogicToggleClicked(object sender, RoutedEventArgs e)
    {
        if (model is null) return;
        model.Root.Logic = model.Root.Logic == PetraReportFilterLogic.And ? PetraReportFilterLogic.Or : PetraReportFilterLogic.And;
        UpdateGroupHeader(LogicToggle, model.Root);
        model.RefreshPreview();
    }

    private void AddConditionClicked(object sender, RoutedEventArgs e)
    {
        if (model is null) return;
        var firstCol = model.AvailableColumns.FirstOrDefault()?.Key ?? "Date";
        model.Root.AddCondition(firstCol);
        model.RefreshPreview();
        RebuildUI();
    }

    private void AddSubGroupClicked(object sender, RoutedEventArgs e)
    {
        if (model is null) return;
        model.Root.AddSubGroup(PetraReportFilterLogic.Or);
        model.RefreshPreview();
        RebuildUI();
    }

    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        model?.Clear();
        RebuildUI();
    }
}
