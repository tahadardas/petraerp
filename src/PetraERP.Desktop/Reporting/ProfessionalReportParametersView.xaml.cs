using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// Al-Ameen 10 native report parameters dialog matching reference screenshots 100%.
/// Displays the 4-action top-right stack, 3-column parameters, fields with up/down arrows,
/// and exact reporting options and sources.
/// </summary>
public partial class ProfessionalReportParametersView : UserControl
{
    private ProfessionalReportPageViewModel? model;
    private FrameworkElement? sources, options, parameters;
    private bool? compact;
    internal event Action<PetraReportViewerWindow>? PreviewOpened;

    public ProfessionalReportParametersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => ArrangePanels();
        Loaded += (_, _) =>
        {
            Detach(); Attach(); Build();
            ReportDisplayOptionsHub.Changed -= OnDisplayOptionsChanged;
            ReportDisplayOptionsHub.Changed += OnDisplayOptionsChanged;
            ArrangePanels();
        };
        Unloaded += (_, _) =>
        {
            Detach();
            ReportDisplayOptionsHub.Changed -= OnDisplayOptionsChanged;
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        Detach();
        model = args.NewValue as ProfessionalReportPageViewModel;
        Attach();
        Build();
    }

    private void Attach()
    {
        if (model is null) return;
        model.DefinitionChanged += ModelDefinitionChanged;
        model.PreviewRequested += Preview;
        model.PrintRequested += OnPrintRequested;
        model.CloseRequested += OnCloseRequested;
        model.PropertyChanged += ModelPropertyChanged;
        model.Localization.LanguageChanged += ModelDefinitionChanged;
    }

    private void Detach()
    {
        if (model is null) return;
        model.DefinitionChanged -= ModelDefinitionChanged;
        model.PreviewRequested -= Preview;
        model.PrintRequested -= OnPrintRequested;
        model.CloseRequested -= OnCloseRequested;
        model.PropertyChanged -= ModelPropertyChanged;
        model.Localization.LanguageChanged -= ModelDefinitionChanged;
    }

    private void ModelDefinitionChanged(object? sender, EventArgs args) => Build();

    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (model is null) return;
        if (args.PropertyName == nameof(ProfessionalReportPageViewModel.IsExecuting))
        {
            BusyIndicator.Visibility = model.IsExecuting ? Visibility.Visible : Visibility.Collapsed;
            StatusLabel.Text = model.IsExecuting ? T("جارٍ الحساب في الخادم…", "Calculating on server…") : string.Empty;
        }

        if (args.PropertyName == nameof(ProfessionalReportPageViewModel.Message))
        {
            MessageText.Text = model.Message;
            MessageBar.Visibility = string.IsNullOrWhiteSpace(model.Message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Preview(ProfessionalPreviewRequest request)
    {
        if (model is null) return;
        var host = Window.GetWindow(this);
        var window = new PetraReportViewerWindow(request, model.Localization, model.OpenSource, host, () => PreviewButton.Focus());
        if (host is not null) window.Resources.MergedDictionaries.Insert(0, host.Resources);
        PreviewOpened?.Invoke(window);
        window.Show();
        window.Activate();
    }

    private void OnPrintRequested()
    {
        // PrintCommand executes the snapshot and triggers PreviewRequested.
    }

    private void OnCloseRequested()
    {
        var window = Window.GetWindow(this);
        if (window is DocumentWindow docWin)
        {
            docWin.Close();
            return;
        }

        var current = Parent as DependencyObject;
        while (current is not null)
        {
            if (current is MdiChildWindow mdiChild)
            {
                if (window is DesktopWindow desktop)
                {
                    _ = desktop.Model.CloseDocumentCommand.ExecuteAsync(mdiChild.Document);
                }
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        if (window is not null && window != Application.Current?.MainWindow && window is not PetraReportViewerWindow)
        {
            window.Close();
        }
    }

    private string T(string ar, string en) => model?.Text(ar, en) ?? en;

    private void Build()
    {
        if (model is null) return;
        model.RefreshLabels();
        FlowDirection = model.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        SummaryButton.Visibility = model.IsTrialBalance ? Visibility.Collapsed : Visibility.Visible;
        AuthorityLabel.Text = T("المجاميع يُحسبها الخادم ولا تغيّرها مرشحات العرض.", "Totals come from the server; display filters never change them.");
        ParametersTab.Header = T("المعايير", "Criteria");
        OptionsTab.Header = T("الخيارات", "Options");
        SourcesTab.Header = T("المصادر", "Sources");

        sources = BuildSources(model);
        options = BuildOptions(model);
        parameters = BuildParameters(model);
        compact = null;
        ArrangePanels();
        ModelPropertyChanged(this, new PropertyChangedEventArgs(nameof(ProfessionalReportPageViewModel.IsExecuting)));
        ModelPropertyChanged(this, new PropertyChangedEventArgs(nameof(ProfessionalReportPageViewModel.Message)));
    }

    // ── 1. Criteria Pane ──────────────────────────────────────────────────────────────────────────
    private ScrollViewer BuildParameters(ProfessionalReportPageViewModel model)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };

        // Account / Party lookup
        if (model.IsPartyStatement)
        {
            panel.Children.Add(AmeenLookupRow(T(model.Kind == ProfessionalReportKind.SupplierStatement ? "المورد" : model.IsCombinedStatement ? "الطرف (زبون/مورد)" : "الزبون",
                model.Kind == ProfessionalReportKind.SupplierStatement ? "Supplier" : model.IsCombinedStatement ? "Party (customer/supplier)" : "Customer"), nameof(model.Party)));
            var accountContext = Note(string.Empty);
            accountContext.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.PartyAccountContext)));
            panel.Children.Add(accountContext);
        }
        else
        {
            panel.Children.Add(AmeenLookupRow(T("الحساب", "Account"), nameof(model.Account)));
        }

        // Level (in trial balance)
        if (model.IsAccountTrial)
        {
            panel.Children.Add(AmeenTextRow(T("المستوى", "Level"), nameof(model.AccountLevel)));
        }

        // Cost Center
        panel.Children.Add(AmeenLookupRow(T("مركز الكلفه", "Cost Center"), nameof(model.CostCenter), model.Supports(ProfessionalReportOption.CostCenter)));

        // Ledger-specific criteria
        if (!model.IsTrialBalance)
        {
            panel.Children.Add(AmeenInputWithSearchRow(T("المادة", "Material"), nameof(model.MaterialQuery)));
            panel.Children.Add(AmeenInputWithSearchRow(T("المجموعة", "Group"), nameof(model.GroupQuery)));
            panel.Children.Add(AmeenInputWithSearchRow(T("المستخدم", "User"), nameof(model.UserQuery)));
            panel.Children.Add(AmeenTextRow(T("الفئة", "Category"), nameof(model.AccountCategory)));
            panel.Children.Add(AmeenTextRow(T("المستوى", "Level"), nameof(model.AccountLevel)));

            var reconcileCheck = new CheckBox
            {
                Content = T("من آخر مطابقة", "From last reconciliation"),
                FontSize = 11,
                Margin = new Thickness(70, 2, 0, 4),
            };
            reconcileCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(nameof(model.IsFromLastReconciliation)));
            panel.Children.Add(reconcileCheck);
        }

        // GroupBox تاريخ الإضافة
        panel.Children.Add(AmeenDateGroup(T("تاريخ الإضافة", "Creation Date"), nameof(model.FilterByCreatedDate), nameof(model.CreatedFrom), nameof(model.CreatedTo)));

        // GroupBox تاريخ الترحيل
        panel.Children.Add(AmeenDateGroup(T("تاريخ الترحيل", "Posting Date"), nameof(model.FilterByPostingDate), nameof(model.FromDate), nameof(model.ToDate)));

        // Currency & Exchange Rate Row
        panel.Children.Add(AmeenCurrencyRateRow(model));

        // Ledger specific (Opposite Account & Narration)
        if (!model.IsTrialBalance)
        {
            if (model.Supports(ProfessionalReportOption.OppositeAccount))
            {
                panel.Children.Add(AmeenLookupRow(T("الحساب المقابل", "Opposite Account"), nameof(model.OppositeAccount)));
                var subNote = new TextBlock
                {
                    Text = T("تفصيلية + رئيسية + تجميعية", "Detail + Parent + Aggregate"),
                    FontSize = 9.5,
                    Foreground = (Brush)FindResource("Text.Muted"),
                    Margin = new Thickness(70, -2, 0, 4),
                };
                panel.Children.Add(subNote);
            }

            // GroupBox البيان
            var descGroup = new GroupBox { Header = T("البيان", "Description"), Style = (Style)Resources["AmeenGroupBox"] };
            var descStack = new StackPanel { Margin = new Thickness(4, 2, 4, 4) };
            descStack.Children.Add(AmeenTextRow(T("يحوي:", "Contains:"), nameof(model.DescriptionContains)));
            descStack.Children.Add(AmeenTextRow(T("لا يحوي:", "Excludes:"), nameof(model.DescriptionNotContains)));
            descGroup.Content = descStack;
            panel.Children.Add(descGroup);

            // شروط أنماط السند
            panel.Children.Add(AmeenTextRow(T("شروط أنماط السند", "Voucher Pattern"), nameof(model.VoucherPatternCondition)));
        }
        else
        {
            // Trial balance criteria
            panel.Children.Add(AmeenTextRow(T("نوع التعادل", "Rate Kind"), nameof(model.ExchangeRateType)));
            panel.Children.Add(AmeenTextRow(T("ترتيب الحسابات", "Account Order"), nameof(model.AccountOrderText)));
            panel.Children.Add(AmeenTextRow(T("ميزان شهري", "Monthly Trial"), nameof(model.MonthlyTrialText)));

            var accCurrCheck = new CheckBox
            {
                Content = T("التصفية على عملة بطاقة الحساب", "Filter by account card currency"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4),
            };
            accCurrCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(nameof(model.FilterByAccountCurrency)));
            panel.Children.Add(accCurrCheck);

            // Sequence filter
            var seqPanel = new Grid { Margin = new Thickness(0, 1, 0, 4) };
            seqPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            seqPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var fromSeq = new StackPanel { Orientation = Orientation.Horizontal };
            fromSeq.Children.Add(new TextBlock { Text = T("من قيد", "From seq"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
            var fromSeqTxt = new TextBox { Width = 60, Height = 22, FontSize = 10 };
            fromSeqTxt.SetBinding(TextBox.TextProperty, Bind(nameof(model.FromSequence)));
            fromSeq.Children.Add(fromSeqTxt);
            Grid.SetColumn(fromSeq, 0);
            seqPanel.Children.Add(fromSeq);

            var toSeq = new StackPanel { Orientation = Orientation.Horizontal };
            toSeq.Children.Add(new TextBlock { Text = T("إلى قيد", "To seq"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
            var toSeqTxt = new TextBox { Width = 60, Height = 22, FontSize = 10 };
            toSeqTxt.SetBinding(TextBox.TextProperty, Bind(nameof(model.ToSequence)));
            toSeq.Children.Add(toSeqTxt);
            Grid.SetColumn(toSeq, 1);
            seqPanel.Children.Add(toSeq);
            panel.Children.Add(seqPanel);

            // GroupBox نوع الميزان
            var trialTypeGroup = new GroupBox { Header = T("نوع الميزان", "Trial Balance Type"), Style = (Style)Resources["AmeenGroupBox"] };
            var trialTypeStack = new StackPanel { Margin = new Thickness(6, 3, 6, 3) };
            trialTypeStack.Children.Add(new RadioButton { Content = T("أرصدة الأرصدة", "Balances"), IsChecked = true, GroupName = "TrialType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            trialTypeStack.Children.Add(new RadioButton { Content = T("المجاميع", "Totals"), GroupName = "TrialType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            trialTypeStack.Children.Add(new RadioButton { Content = T("الأرصدة", "Net Balances"), GroupName = "TrialType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            trialTypeGroup.Content = trialTypeStack;
            panel.Children.Add(trialTypeGroup);
        }

        return Pane(panel);
    }

    // ── 2. Options Pane ───────────────────────────────────────────────────────────────────────────
    private ScrollViewer BuildOptions(ProfessionalReportPageViewModel model)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };

        if (!model.IsTrialBalance)
        {
            // GroupBox إظهار الحقول with blue up/down reorder arrows
            var fieldsGroup = new GroupBox { Header = T("إظهار الحقول", "Show Fields"), Style = (Style)Resources["AmeenGroupBox"], Margin = new Thickness(0, 0, 0, 4) };
            var fieldsGrid = new Grid { Margin = new Thickness(2) };
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Pixel) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var arrowsStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
            var upBtn = new Button { Width = 22, Height = 24, Margin = new Thickness(0, 0, 0, 3), ToolTip = T("تحريك للأعلى", "Move Up"), Style = (Style)Resources["AmeenArrowButton"] };
            upBtn.SetBinding(Button.CommandProperty, new Binding(nameof(model.MoveFieldUpCommand)));
            var upBtnArrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,6 L 5,0 L 10,6 Z"), Width = 10, Height = 6, Stretch = Stretch.Fill };
            upBtnArrow.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
            upBtn.Content = upBtnArrow;
            arrowsStack.Children.Add(upBtn);

            var downBtn = new Button { Width = 22, Height = 24, ToolTip = T("تحريك للأسفل", "Move Down"), Style = (Style)Resources["AmeenArrowButton"] };
            downBtn.SetBinding(Button.CommandProperty, new Binding(nameof(model.MoveFieldDownCommand)));
            var downBtnArrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,0 L 5,6 L 10,0 Z"), Width = 10, Height = 6, Stretch = Stretch.Fill };
            downBtnArrow.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
            downBtn.Content = downBtnArrow;
            arrowsStack.Children.Add(downBtn);
            Grid.SetColumn(arrowsStack, 0);
            fieldsGrid.Children.Add(arrowsStack);

            var fieldsList = new ListBox
            {
                Height = 190,
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("Border"),
                ItemTemplate = (DataTemplate)Resources["ReportChoice"],
            };
            ScrollViewer.SetVerticalScrollBarVisibility(fieldsList, ScrollBarVisibility.Auto);
            fieldsList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.Fields)));
            fieldsList.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Bind(nameof(model.SelectedField)));
            Grid.SetColumn(fieldsList, 1);
            fieldsGrid.Children.Add(fieldsList);
            fieldsGroup.Content = fieldsGrid;
            panel.Children.Add(fieldsGroup);

            // GroupBox خيارات التقرير
            var optionsGroup = new GroupBox { Header = T("خيارات التقرير", "Report Options"), Style = (Style)Resources["AmeenGroupBox"] };
            var optionsList = new StackPanel { Margin = new Thickness(4) };
            optionsList.Children.Add(CreateCheck(T("تصفير الرصيد في بداية المدة", "Zero balance at start"), nameof(model.ZeroBalanceAtStart)));
            optionsList.Children.Add(CreateCheck(T("تجميع الحسابات المكررة في نفس القيد", "Group duplicate accounts in journal"), nameof(model.GroupDuplicateAccountsInJournal)));
            optionsList.Children.Add(CreateCheck(T("دمج سندات الأوراق المالية ذات نفس الرقم", "Merge commercial papers"), nameof(model.MergeCommercialPapers)));
            optionsList.Children.Add(CreateCheck(T("معالجة البيان في حساب الرصيد السابق", "Handle narration in opening balance"), nameof(model.HandleNarrationInOpeningBalance)));
            optionsList.Children.Add(CreateCheck(T("قراءة القيود غير المرحلة", "Read unposted entries"), nameof(model.ReadUnpostedEntries)));
            optionsList.Children.Add(CreateCheck(T("إظهار الحسابات الفارغة", "Include empty accounts"), nameof(model.IncludeEmptyAccounts)));
            optionsList.Children.Add(CreateCheck(T("كل حساب على ورقة جديدة", "Each account on new page"), nameof(model.EachAccountOnNewPage)));
            optionsList.Children.Add(CreateCheck(T("قراءة الأقلام المدققة", "Read audited lines"), nameof(model.ReadAuditedLines)));
            optionsList.Children.Add(CreateCheck(T("قراءة الأقلام غير المدققة", "Read unaudited lines"), nameof(model.ReadUnauditedLines)));
            optionsList.Children.Add(CreateCheck(T("إظهار تفقيط الرصيد", "Show tafqeet"), nameof(model.ShowTafqeet)));
            optionsList.Children.Add(CreateCheck(T("إظهار فروقات أسعار الصرف بالكلفة التاريخية", "Show FX differences historical"), nameof(model.ShowExchangeDiffHistorical)));
            optionsList.Children.Add(CreateCheck(T("إظهار بيان القلم و السند", "Show line and voucher narration"), nameof(model.ShowLineAndVoucherNarration)));
            optionsList.Children.Add(CreateCheck(T("إظهار تاريخ الترحيل", "Show posting date"), nameof(model.ShowPostingDate)));
            optionsGroup.Content = new ScrollViewer { Content = optionsList, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(optionsGroup);
        }
        else
        {
            // Trial balance: GroupBox خيارات الإظهار
            var showGroup = new GroupBox { Header = T("خيارات الإظهار", "Display Options"), Style = (Style)Resources["AmeenGroupBox"] };
            var showList = new StackPanel { Margin = new Thickness(4) };
            showList.Children.Add(CreateCheck(T("إظهار اسم الحساب", "Show account name"), nameof(model.ShowAccountName)));
            showList.Children.Add(CreateCheck(T("إظهار رمز الحساب", "Show account code"), nameof(model.ShowAccountCode)));
            if (model.IsAccountTrial) showList.Children.Add(CreateCheck(T("إظهار الحسابات الفرعية", "Include posting accounts"), nameof(model.IncludePostingAccounts)));
            if (model.IsAccountTrial) showList.Children.Add(CreateCheck(T("إظهار الحسابات الرئيسية", "Include parent accounts"), nameof(model.IncludeParents)));
            if (model.IsAccountTrial) showList.Children.Add(CreateCheck(T("إظهار الحسابات التجميعية", "Include aggregate accounts"), nameof(model.IncludeAggregateAccounts)));
            showList.Children.Add(CreateCheck(T("إظهار الحسابات المرصدة", "Nonzero balances only"), nameof(model.OnlyNonZero)));
            if (model.IsAccountTrial) showList.Children.Add(CreateCheck(T("إظهار الحسابات الفارغة", "Include empty accounts"), nameof(model.IncludeEmptyAccounts)));
            showList.Children.Add(CreateCheck(T("إظهار آخر دفعة دفعها الزبون", "Show last customer payment"), nameof(model.ShowLastCustomerPayment)));
            showList.Children.Add(CreateCheck(T("إظهار آخر حركة مدينة", "Show last debit activity"), nameof(model.ShowLastDebitActivity)));
            showList.Children.Add(CreateCheck(T("إظهار آخر حركة دائنة", "Show last credit activity"), nameof(model.ShowLastCreditActivity)));
            showList.Children.Add(CreateCheck(T("إظهار القيود المرحلة", "Show posted entries"), nameof(model.ShowPostedEntries)));
            showList.Children.Add(CreateCheck(T("قراءة القيود غير المرحلة", "Read unposted entries"), nameof(model.ReadUnpostedEntries)));
            showGroup.Content = new ScrollViewer { Content = showList, Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(showGroup);

            // GroupBox خيارات الرصيد
            var balanceGroup = new GroupBox { Header = T("خيارات الرصيد", "Balance Options"), Style = (Style)Resources["AmeenGroupBox"] };
            var balStack = new StackPanel { Margin = new Thickness(6, 3, 6, 3) };
            balStack.Children.Add(new RadioButton { Content = T("أرصدة الأرصدة", "Balances"), IsChecked = true, GroupName = "BalType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            balStack.Children.Add(new RadioButton { Content = T("المجاميع", "Totals"), GroupName = "BalType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            balStack.Children.Add(new RadioButton { Content = T("الأرصدة", "Net Balances"), GroupName = "BalType", FontSize = 11, Margin = new Thickness(0, 1, 0, 2) });
            balanceGroup.Content = balStack;
            panel.Children.Add(balanceGroup);
        }

        return Pane(panel);
    }

    // ── 3. Sources Pane ───────────────────────────────────────────────────────────────────────────
    private ScrollViewer BuildSources(ProfessionalReportPageViewModel model)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };

        var sourcesGroup = new GroupBox { Header = T("مصادر التقرير", "Report Sources"), Style = (Style)Resources["AmeenGroupBox"] };
        var dock = new DockPanel { Margin = new Thickness(2) };
        var toolbar = SelectionToolbar(nameof(model.SelectAllSourcesCommand), nameof(model.ClearSourcesCommand), nameof(model.SourceCount));
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);

        var sourceList = new ItemsControl { ItemTemplate = (DataTemplate)Resources["ReportChoice"] };
        sourceList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.VisibleSources)));
        var scroller = new ScrollViewer { Content = sourceList, Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(scroller);
        sourcesGroup.Content = dock;
        panel.Children.Add(sourcesGroup);

        var tabsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var patternsBtn = new Button { Content = T("مجموعات الأنماط", "Pattern Groups"), Style = (Style)Resources["AmeenTabButton"], Margin = new Thickness(0, 0, 0, 2), Height = 24 };
        patternsBtn.Click += PatternGroupsClicked;
        tabsPanel.Children.Add(patternsBtn);

        if (!model.IsTrialBalance)
        {
            tabsPanel.Children.Add(new Button { Content = T("حقول مخصصة فواتير", "Custom Invoice Fields"), Style = (Style)Resources["AmeenTabButton"], Margin = new Thickness(0, 0, 0, 2), Height = 24 });
            tabsPanel.Children.Add(new Button { Content = T("حقول مخصصة سندات", "Custom Voucher Fields"), Style = (Style)Resources["AmeenTabButton"], Height = 24 });
        }
        panel.Children.Add(tabsPanel);

        return Pane(panel);
    }

    // ── Shared UI Row Builders ────────────────────────────────────────────────────────────────────
    private Grid AmeenLookupRow(string label, string path, bool enabled = true)
    {
        var row = new Grid { IsEnabled = enabled, Margin = new Thickness(0, 1, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24, GridUnitType.Pixel) });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(0, 0, 4, 0),
        };
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            Height = 24,
            FontSize = 11,
            DisplayMemberPath = "Label",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        combo.SetBinding(TextBox.TextProperty, Bind(path + ".Query"));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(path + ".Items"));
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Bind(path + ".Selected"));
        combo.SetBinding(ComboBox.IsDropDownOpenProperty, Bind(path + ".IsResultsOpen"));

        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);

        var btn = new Button
        {
            Content = "🔍",
            Width = 24,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = T("بحث", "Search"),
            FontSize = 10,
        };
        btn.SetBinding(Button.CommandProperty, new Binding(path + ".SearchCommand"));
        Grid.SetColumn(btn, 2);
        row.Children.Add(btn);

        return row;
    }

    private static Grid AmeenTextRow(string label, string property, bool enabled = true)
    {
        var row = new Grid { IsEnabled = enabled, Margin = new Thickness(0, 1, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        var txt = new TextBox { Height = 24, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
        txt.SetBinding(TextBox.TextProperty, Bind(property));
        Grid.SetColumn(txt, 1);
        row.Children.Add(txt);

        return row;
    }

    private Grid AmeenInputWithSearchRow(string label, string property, bool enabled = true)
    {
        var row = new Grid { IsEnabled = enabled, Margin = new Thickness(0, 1, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24, GridUnitType.Pixel) });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(lbl, 0);
        row.Children.Add(lbl);

        var txt = new TextBox { Height = 24, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
        txt.SetBinding(TextBox.TextProperty, Bind(property));
        Grid.SetColumn(txt, 1);
        row.Children.Add(txt);

        var btn = new Button
        {
            Content = "🔍",
            Width = 24,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = T("بحث", "Search"),
            FontSize = 10,
        };
        Grid.SetColumn(btn, 2);
        row.Children.Add(btn);

        return row;
    }

    private GroupBox AmeenDateGroup(string headerText, string checkProperty, string fromProperty, string toProperty)
    {
        var group = new GroupBox { Style = (Style)Resources["AmeenGroupBox"], Margin = new Thickness(0, 2, 0, 4) };
        var chk = new CheckBox { Content = headerText, FontSize = 11, FontWeight = FontWeights.SemiBold };
        chk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(checkProperty));
        group.Header = chk;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fromPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
        fromPanel.Children.Add(new TextBlock { Text = T("من تاريخ", "From"), VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Margin = new Thickness(0, 0, 3, 0) });
        var fromPicker = new PetraDatePicker { Height = 24, FontSize = 10, FlowDirection = FlowDirection.LeftToRight };
        fromPicker.SetBinding(DatePicker.SelectedDateProperty, Bind(fromProperty));
        fromPanel.Children.Add(fromPicker);
        Grid.SetColumn(fromPanel, 0);
        grid.Children.Add(fromPanel);

        var toPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        toPanel.Children.Add(new TextBlock { Text = T("إلى تاريخ", "To"), VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Margin = new Thickness(0, 0, 3, 0) });
        var toPicker = new PetraDatePicker { Height = 24, FontSize = 10, FlowDirection = FlowDirection.LeftToRight };
        toPicker.SetBinding(DatePicker.SelectedDateProperty, Bind(toProperty));
        toPanel.Children.Add(toPicker);
        Grid.SetColumn(toPanel, 1);
        grid.Children.Add(toPanel);

        group.Content = grid;
        return group;
    }

    private Grid AmeenCurrencyRateRow(ProfessionalReportPageViewModel model)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var currLbl = new TextBlock { Text = T("العملة", "Currency"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(currLbl, 0);
        row.Children.Add(currLbl);

        var currInput = new ComboBox { Height = 24, FontSize = 11, IsEditable = false, VerticalContentAlignment = VerticalAlignment.Center };
        currInput.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Bind(nameof(model.CurrencyCode)));
        currInput.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.CurrencyOptions)) { Mode = BindingMode.OneWay });
        Grid.SetColumn(currInput, 1);
        row.Children.Add(currInput);

        var rateLbl = new TextBlock { Text = T("عرض بـ", "View in"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(rateLbl, 2);
        if (!model.IsCurrencyBalance) row.Children.Add(rateLbl);

        var rateInput = new ComboBox { Height = 24, FontSize = 11, IsEditable = false, VerticalContentAlignment = VerticalAlignment.Center };
        rateInput.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Bind(nameof(model.DisplayCurrencyCode)));
        rateInput.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.CurrencyOptions)) { Mode = BindingMode.OneWay });
        Grid.SetColumn(rateInput, 3);
        if (!model.IsCurrencyBalance) row.Children.Add(rateInput);

        return row;
    }

    private static CheckBox CreateCheck(string label, string property)
    {
        var chk = new CheckBox { Content = label, FontSize = 11, Margin = new Thickness(0, 1, 0, 2) };
        chk.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(property));
        return chk;
    }

    private StackPanel SelectionToolbar(string selectCommand, string clearCommand, string countProperty)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        var allBtn = new Button { Content = T("الكل", "All"), Style = (Style)Resources["AmeenTabButton"], Width = 50, Height = 22, Margin = new Thickness(0, 0, 3, 0), FontSize = 10 };
        allBtn.SetBinding(Button.CommandProperty, new Binding(selectCommand));
        panel.Children.Add(allBtn);

        var noneBtn = new Button { Content = T("لا شيء", "None"), Style = (Style)Resources["AmeenTabButton"], Width = 50, Height = 22, Margin = new Thickness(0, 0, 3, 0), FontSize = 10 };
        noneBtn.SetBinding(Button.CommandProperty, new Binding(clearCommand));
        panel.Children.Add(noneBtn);

        var count = new TextBlock { FontSize = 10, Foreground = (Brush)FindResource("Text.Muted"), Margin = new Thickness(4, 2, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        count.SetBinding(TextBlock.TextProperty, new Binding(countProperty));
        panel.Children.Add(count);
        return panel;
    }

    private void PatternGroupsClicked(object sender, RoutedEventArgs e)
    {
        if (model is null || model.Patterns.Count == 0) return;
        var popup = new Window
        {
            Title = T("مجموعات الأنماط", "Pattern Groups"),
            Height = 380,
            FlowDirection = FlowDirection,
        };
        var dock = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var selectAll = new Button { Content = T("الكل", "All"), Width = 60, Margin = new Thickness(0, 0, 4, 0) };
        selectAll.Click += (_, _) => model.SelectAllPatternsCommand.Execute(null);
        var clear = new Button { Content = T("لا شيء", "None"), Width = 60 };
        selectAll.SetResourceReference(StyleProperty, "Button.Command");
        clear.SetResourceReference(StyleProperty, "Button.Command");
        clear.Click += (_, _) => model.ClearPatternsCommand.Execute(null);
        toolbar.Children.Add(selectAll);
        toolbar.Children.Add(clear);
        dock.Children.Add(toolbar);

        var list = new ItemsControl { ItemTemplate = PatternTemplate() };
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.VisiblePatterns)) { Source = model });
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(scroll);
        popup.Content = dock;
        _ = PetraWindows.ShowDialog(popup, Window.GetWindow(this), DialogSize.Small);
    }

    private static DataTemplate PatternTemplate()
    {
        var checkbox = new FrameworkElementFactory(typeof(CheckBox));
        checkbox.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ProfessionalPatternChoice.Label)));
        checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(ProfessionalPatternChoice.IsSelected)) { Mode = BindingMode.TwoWay });
        checkbox.SetValue(MarginProperty, new Thickness(0, 2, 0, 2));
        checkbox.SetValue(FontSizeProperty, 11d);
        return new DataTemplate { VisualTree = checkbox };
    }

    private static Binding Bind(string path) => new(path)
    {
        Mode = BindingMode.TwoWay,
        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        ValidatesOnExceptions = true,
        NotifyOnValidationError = true,
    };

    private TextBlock Note(string text) => new() { Text = text, FontSize = 10, Foreground = (Brush)FindResource("Text.Muted"), Margin = new Thickness(70, 0, 0, 2) };

    private ScrollViewer Pane(UIElement content) => new()
    {
        Content = content,
        Style = (Style)Resources["ReportPane"],
        FlowDirection = FlowDirection,
    };

    private void OnDisplayOptionsChanged(object? sender, EventArgs args) => ArrangePanels();

    private void ArrangePanels()
    {
        if (parameters is null || options is null || sources is null) return;
        var useTabs = ReportDisplayOptionsHub.Current.ParametersLayout switch
        {
            ReportParametersLayout.Tabs => true,
            ReportParametersLayout.Columns => false,
            _ => ActualWidth > 0 && ActualWidth < 1000,
        };

        if (compact == useTabs) return;
        SourcesHost.Content = null; OptionsHost.Content = null; ParametersHost.Content = null;
        CompactSourcesHost.Content = null; CompactOptionsHost.Content = null; CompactParametersHost.Content = null;
        if (useTabs)
        {
            CompactParametersHost.Content = parameters; CompactOptionsHost.Content = options; CompactSourcesHost.Content = sources;
        }
        else
        {
            SourcesHost.Content = sources; OptionsHost.Content = options; ParametersHost.Content = parameters;
        }

        CompactLayout.Visibility = useTabs ? Visibility.Visible : Visibility.Collapsed;
        WideLayout.Visibility = useTabs ? Visibility.Collapsed : Visibility.Visible;
        compact = useTabs;
    }
}
