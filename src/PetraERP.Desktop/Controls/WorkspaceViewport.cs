using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Controls;

/// <summary>Short work areas scroll a bounded page; grids still receive finite height and retain virtualization.</summary>
public sealed class WorkspaceViewport : ScrollViewer
{
    public static readonly DependencyProperty MinimumContentHeightProperty = DependencyProperty.Register(nameof(MinimumContentHeight), typeof(double), typeof(WorkspaceViewport), new FrameworkPropertyMetadata(520d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double MinimumContentHeight { get => (double)GetValue(MinimumContentHeightProperty); set => SetValue(MinimumContentHeightProperty, value); }

    public WorkspaceViewport()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        CanContentScroll = false;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Content is FrameworkElement content && double.IsFinite(constraint.Height))
            content.SetCurrentValue(HeightProperty, Math.Max(MinimumContentHeight, constraint.Height));
        return base.MeasureOverride(constraint);
    }
}
