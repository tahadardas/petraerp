using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Reporting;

/// <summary>WPF text layout supplies Arabic shaping. Paper is intentionally white in both application themes.</summary>
public static class ProfessionalReportPageBuilder
{
    private const double Margin = 32;
    private const double HeaderHeight = 136;
    private const double FooterHeight = 30;
    private const double ColumnHeaderHeight = 44;

    public static async Task<FixedDocument> BuildAsync(ReportDocument report, bool rightToLeft, CancellationToken cancellationToken, PetraReportBranding? branding = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var landscape = report.Columns.Count > 7;
        var pageWidth = landscape ? 1122.52 : 793.70;
        var pageHeight = landscape ? 793.70 : 1122.52;
        var marginL = branding?.MarginLeft > 0 ? branding.MarginLeft : Margin;
        var marginR = branding?.MarginRight > 0 ? branding.MarginRight : Margin;
        var marginT = branding?.MarginTop > 0 ? branding.MarginTop : Margin;
        var marginB = branding?.MarginBottom > 0 ? branding.MarginBottom : Margin;
        var width = pageWidth - marginL - marginR;
        var bottom = pageHeight - marginB - FooterHeight;
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);
        FixedPage? page = null;
        var y = 0d;
        var pageNumber = 0;
        var direction = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        void NewPage(bool includeTableHeader)
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White, FlowDirection = FlowDirection.LeftToRight };
            var header = new StackPanel { Width = width, FlowDirection = direction };

            var companyTitle = !string.IsNullOrWhiteSpace(branding?.CompanyNameArabic) && rightToLeft
                ? branding.CompanyNameArabic
                : (!string.IsNullOrWhiteSpace(branding?.CompanyNameEnglish) && !rightToLeft ? branding.CompanyNameEnglish : report.CompanyLabel);

            var topRow = new DockPanel { Width = width, FlowDirection = direction };
            if (branding?.ShowLogo == true && !string.IsNullOrEmpty(branding.LogoBase64))
            {
                try
                {
                    var logoBytes = Convert.FromBase64String(branding.LogoBase64);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(logoBytes);
                    bmp.EndInit();
                    var img = new Image { Source = bmp, MaxHeight = 48, MaxWidth = 120, Margin = new Thickness(0, 0, 10, 0) };
                    DockPanel.SetDock(img, rightToLeft ? Dock.Right : Dock.Left);
                    topRow.Children.Add(img);
                }
                catch (Exception) { }
            }
            var titleStack = new StackPanel();
            titleStack.Children.Add(Text(report.Title, 18, FontWeights.Bold));
            titleStack.Children.Add(Text(companyTitle + " · " + report.PeriodLabel, 10, FontWeights.Normal));
            topRow.Children.Add(titleStack);
            header.Children.Add(topRow);

            if (branding?.ShowHeaderSubtitle == true && !string.IsNullOrWhiteSpace(branding.HeaderSubtitle))
            {
                header.Children.Add(Text(branding.HeaderSubtitle, 9, FontWeights.Normal));
            }

            if (branding?.ShowTaxNumber == true && (!string.IsNullOrWhiteSpace(branding.TaxNumber) || !string.IsNullOrWhiteSpace(branding.CommercialRegistration)))
            {
                var taxInfo = $"الرقم الضريبي: {branding.TaxNumber} · السجل التجاري: {branding.CommercialRegistration}";
                header.Children.Add(Text(taxInfo, 8, FontWeights.Normal));
            }

            header.Children.Add(Text(report.Filters.GetValueOrDefault("Subject / الحساب", string.Empty) + " · " + report.CurrencyCode, 10, FontWeights.Normal));
            header.Children.Add(Text("Snapshot / نسخة: " + report.ExecutionId + " · " + report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture), 8, FontWeights.Normal));
            Place(header, marginL, marginT);
            y = marginT + HeaderHeight;
            if (includeTableHeader)
            {
                var columns = Row(report.Columns.Select(column => (object?)column.Heading).ToArray(), true);
                columns.Height = ColumnHeaderHeight; Place(columns, marginL, y); y += ColumnHeaderHeight;
            }
            var footerText = !string.IsNullOrWhiteSpace(branding?.FooterText)
                ? branding.FooterText + " · "
                : string.Empty;
            var footer = Text(footerText + "PetraERP · " + report.ReportCode + " · " + (++pageNumber).ToString(CultureInfo.InvariantCulture), 9, FontWeights.Normal);
            footer.Width = width; footer.TextAlignment = TextAlignment.Center; Place(footer, marginL, pageHeight - marginB - 18);
            var content = new PageContent(); ((System.Windows.Markup.IAddChild)content).AddChild(page); document.Pages.Add(content);
        }

        void Place(UIElement element, double left, double top)
        {
            FixedPage.SetLeft(element, left); FixedPage.SetTop(element, top); page!.Children.Add(element);
        }

        Grid Row(IReadOnlyList<object?> values, bool heading)
        {
            var grid = new Grid { Width = width, FlowDirection = direction, Background = heading ? Brushes.Gainsboro : Brushes.White };
            foreach (var _ in report.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var columnIndex = 0; columnIndex < report.Columns.Count; columnIndex++)
            {
                var definition = report.Columns[columnIndex];
                var value = values[columnIndex];
                var cell = Text(heading ? value?.ToString() ?? string.Empty : Format(value, definition), report.Columns.Count > 12 ? 8 : 10, heading ? FontWeights.SemiBold : FontWeights.Normal);
                cell.Margin = new Thickness(4, 5, 4, 5);
                if (!heading && definition.Kind is ReportValueKind.Amount or ReportValueKind.Quantity or ReportValueKind.Integer or ReportValueKind.Date)
                { cell.FlowDirection = FlowDirection.LeftToRight; cell.TextAlignment = TextAlignment.Right; }
                var border = new Border { Child = cell, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0.5, 0.5) };
                Grid.SetColumn(border, columnIndex); grid.Children.Add(border);
            }
            return grid;
        }

        NewPage(true);
        string? currentGroup = null;
        for (var rowIndex = 0; rowIndex < report.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Rows[rowIndex].GetValueOrDefault("ReportGroup") is string group && !string.Equals(currentGroup, group, StringComparison.Ordinal))
            {
                if (y + 60 > bottom) NewPage(true);
                var groupLabel = Text(group, 11, FontWeights.SemiBold); groupLabel.Width = width; groupLabel.FlowDirection = direction;
                groupLabel.Padding = new Thickness(4); groupLabel.Background = Brushes.WhiteSmoke; Place(groupLabel, marginL, y); y += 28;
                currentGroup = group;
            }
            var row = Row(report.Columns.Select(column => report.Rows[rowIndex].GetValueOrDefault(column.Key)).ToArray(), false);
            row.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Max(26, row.DesiredSize.Height);
            if (height <= bottom - (marginT + HeaderHeight + ColumnHeaderHeight))
            {
                if (y + height > bottom) NewPage(true);
                Place(row, marginL, y); y += height;
            }
            else
            {
                // Long descriptions are carried across pages without shortening or removing their content.
                row.Arrange(new Rect(0, 0, width, height)); row.UpdateLayout();
                for (var offset = 0d; offset < height;)
                {
                    if (bottom - y < 40) NewPage(true);
                    var sliceHeight = Math.Min(height - offset, bottom - y);
                    var brush = new VisualBrush(row)
                    {
                        ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, offset, width, sliceHeight), Stretch = Stretch.Fill,
                    };
                    Place(new System.Windows.Shapes.Rectangle { Width = width, Height = sliceHeight, Fill = brush }, marginL, y);
                    y += sliceHeight; offset += sliceHeight;
                    if (offset < height) NewPage(true);
                }
            }
            if (rowIndex % 50 == 0) await Dispatcher.Yield(DispatcherPriority.Background);
        }

        foreach (var summary in report.Summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (y + 35 > bottom) NewPage(false);
            var text = Text(summary.Label + " : " + summary.Value.ToString("N4", CultureInfo.InvariantCulture) + " " + summary.CurrencyCode, 10, FontWeights.SemiBold);
            text.Width = width; text.FlowDirection = direction; Place(text, marginL, y + 7); y += 30;
        }
        foreach (var filter in report.Filters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = Text(filter.Key + ": " + filter.Value, 9, FontWeights.Normal); text.Width = width; text.FlowDirection = direction;
            text.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Max(20, text.DesiredSize.Height + 6);
            if (y + height > bottom) NewPage(false);
            Place(text, marginL, y + 3); y += height;
        }

        if (branding?.ShowSignatures == true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (y + 65 > bottom) NewPage(false);
            var signaturesGrid = new Grid { Width = width, FlowDirection = direction, Margin = new Thickness(0, 10, 0, 0) };
            signaturesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            signaturesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            signaturesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sig1 = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sig1.Children.Add(Text(branding.PreparedByTitle, 9, FontWeights.SemiBold));
            sig1.Children.Add(Text("______________________", 9, FontWeights.Normal));
            Grid.SetColumn(sig1, 0);
            signaturesGrid.Children.Add(sig1);

            var sig2 = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sig2.Children.Add(Text(branding.ReviewedByTitle, 9, FontWeights.SemiBold));
            sig2.Children.Add(Text("______________________", 9, FontWeights.Normal));
            Grid.SetColumn(sig2, 1);
            signaturesGrid.Children.Add(sig2);

            var sig3 = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sig3.Children.Add(Text(branding.ApprovedByTitle, 9, FontWeights.SemiBold));
            sig3.Children.Add(Text("______________________", 9, FontWeights.Normal));
            Grid.SetColumn(sig3, 2);
            signaturesGrid.Children.Add(sig3);

            Place(signaturesGrid, marginL, y + 5);
        }

        return document;
    }

    private static TextBlock Text(string value, double size, FontWeight weight) => new()
    {
        Text = value, FontSize = size, FontWeight = weight, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black,
        TextWrapping = TextWrapping.Wrap,
    };

    private static string Format(object? value, ReportColumn column)
    {
        if (value is null) return string.Empty;
        return column.Kind switch
        {
            ReportValueKind.Amount or ReportValueKind.Quantity => ReportDocument.DecimalValue(value).ToString("N" + column.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            ReportValueKind.Integer => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            ReportValueKind.Date => ReportDocument.DateValue(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }
}
