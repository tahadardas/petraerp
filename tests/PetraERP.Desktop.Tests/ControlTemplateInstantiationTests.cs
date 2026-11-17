using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Controls;
using Xunit;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Every custom control styled in Components.xaml must instantiate its template: a markup cycle or a missing resource inside a
/// template only throws when the template is applied, i.e. when the first screen that uses the control is shown.
/// </summary>
[Collection(WpfApplicationGroup.Name)]
public sealed class ControlTemplateInstantiationTests
{
    [Fact]
    public async Task EveryStyledCustomControlAppliesItsTemplateWithoutMarkupErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.Run(() => WpfApplicationHost.Run(Verify), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(300), cancellationToken).ConfigureAwait(true);
    }

    private static void Verify()
    {
        WpfApplicationHost.ResetResources();
        try
        {
            WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            var failures = new List<string>();
            var tested = 0;
            var candidates = typeof(RecordNavigator).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null
                    && (typeof(UserControl).IsAssignableFrom(t)
                        || typeof(Control).IsAssignableFrom(t) && !typeof(Window).IsAssignableFrom(t)
                           && t.Namespace == "PetraERP.Desktop.Controls" && System.Windows.Application.Current.TryFindResource(t) is Style));
            foreach (var type in candidates)
            {
                try
                {
                    var control = (FrameworkElement)Activator.CreateInstance(type)!;
                    var host = new Grid { Width = 800, Height = 600 };
                    host.Children.Add(control);
                    host.Measure(new Size(800, 600));
                    host.Arrange(new Rect(0, 0, 800, 600));
                    host.UpdateLayout();
                    tested++;
                }
                catch (Exception exception)
                {
                    var root = exception;
                    while (root.InnerException is not null) root = root.InnerException;
                    failures.Add($"{type.Name}: {root.Message}");
                }
            }

            Assert.True(tested > 10, $"Only {tested} controls were instantiated; the enumeration is broken.");
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            WpfApplicationHost.ResetResources();
        }
    }
}
