using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Sticky bottom totals summary bar displaying high-contrast grand total, subtotal, discounts,
/// taxes, additions, quantities, and settlement balances in the Al-Ameen accounting layout,
/// accompanied by live Arabic Tafqeet text.
/// </summary>
public sealed class DocumentTotalsBar : Control
{
    public static readonly DependencyProperty GrandTotalProperty = DependencyProperty.Register(
        nameof(GrandTotal), typeof(string), typeof(DocumentTotalsBar),
        new PropertyMetadata("0.0000", OnTotalsChanged));

    public static readonly DependencyProperty SubTotalProperty = DependencyProperty.Register(
        nameof(SubTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata("0.0000"));

    public static readonly DependencyProperty DiscountTotalProperty = DependencyProperty.Register(
        nameof(DiscountTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata("0.0000"));

    public static readonly DependencyProperty TaxTotalProperty = DependencyProperty.Register(
        nameof(TaxTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata("0.0000"));

    public static readonly DependencyProperty TotalQuantityProperty = DependencyProperty.Register(
        nameof(TotalQuantity), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata("0.0000"));

    public static readonly DependencyProperty AdditionsTotalProperty = DependencyProperty.Register(
        nameof(AdditionsTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata("0.0000"));

    public static readonly DependencyProperty CurrencyCodeProperty = DependencyProperty.Register(
        nameof(CurrencyCode), typeof(string), typeof(DocumentTotalsBar),
        new PropertyMetadata(string.Empty, OnTotalsChanged));

    public static readonly DependencyProperty SettledTotalProperty = DependencyProperty.Register(
        nameof(SettledTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ReturnedTotalProperty = DependencyProperty.Register(
        nameof(ReturnedTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty OutstandingTotalProperty = DependencyProperty.Register(
        nameof(OutstandingTotal), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasSettlementProperty = DependencyProperty.Register(
        nameof(HasSettlement), typeof(bool), typeof(DocumentTotalsBar), new PropertyMetadata(false));

    public static readonly DependencyProperty ExtraContentProperty = DependencyProperty.Register(
        nameof(ExtraContent), typeof(object), typeof(DocumentTotalsBar));

    public static readonly DependencyProperty TafqeetTextProperty = DependencyProperty.Register(
        nameof(TafqeetText), typeof(string), typeof(DocumentTotalsBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowTafqeetProperty = DependencyProperty.Register(
        nameof(ShowTafqeet), typeof(bool), typeof(DocumentTotalsBar), new PropertyMetadata(true));

    static DocumentTotalsBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DocumentTotalsBar), new FrameworkPropertyMetadata(typeof(DocumentTotalsBar)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (ShowTafqeet) UpdateTafqeet();
    }

    private static void OnTotalsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentTotalsBar bar && bar.ShowTafqeet)
        {
            bar.UpdateTafqeet();
        }
    }

    private void UpdateTafqeet()
    {
        if (decimal.TryParse(GrandTotal, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
        {
            SetCurrentValue(TafqeetTextProperty, ArabicTafqeet.ToWords(amt, CurrencyCode));
        }
        else
        {
            SetCurrentValue(TafqeetTextProperty, string.Empty);
        }
    }

    public string GrandTotal
    {
        get => (string)GetValue(GrandTotalProperty);
        set => SetValue(GrandTotalProperty, value);
    }

    public string SubTotal
    {
        get => (string)GetValue(SubTotalProperty);
        set => SetValue(SubTotalProperty, value);
    }

    public string DiscountTotal
    {
        get => (string)GetValue(DiscountTotalProperty);
        set => SetValue(DiscountTotalProperty, value);
    }

    public string TaxTotal
    {
        get => (string)GetValue(TaxTotalProperty);
        set => SetValue(TaxTotalProperty, value);
    }

    public string TotalQuantity
    {
        get => (string)GetValue(TotalQuantityProperty);
        set => SetValue(TotalQuantityProperty, value);
    }

    public string AdditionsTotal
    {
        get => (string)GetValue(AdditionsTotalProperty);
        set => SetValue(AdditionsTotalProperty, value);
    }

    public string CurrencyCode
    {
        get => (string)GetValue(CurrencyCodeProperty);
        set => SetValue(CurrencyCodeProperty, value);
    }

    public string SettledTotal
    {
        get => (string)GetValue(SettledTotalProperty);
        set => SetValue(SettledTotalProperty, value);
    }

    public string ReturnedTotal
    {
        get => (string)GetValue(ReturnedTotalProperty);
        set => SetValue(ReturnedTotalProperty, value);
    }

    public string OutstandingTotal
    {
        get => (string)GetValue(OutstandingTotalProperty);
        set => SetValue(OutstandingTotalProperty, value);
    }

    public bool HasSettlement
    {
        get => (bool)GetValue(HasSettlementProperty);
        set => SetValue(HasSettlementProperty, value);
    }

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }

    public string TafqeetText
    {
        get => (string)GetValue(TafqeetTextProperty);
        set => SetValue(TafqeetTextProperty, value);
    }

    public bool ShowTafqeet
    {
        get => (bool)GetValue(ShowTafqeetProperty);
        set => SetValue(ShowTafqeetProperty, value);
    }
}
