using System.Windows;

namespace PetraERP.Desktop.Shell;

/// <summary>Short-lived branded startup surface. Every displayed stage is set by the real startup operation.</summary>
public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
        if (!SystemParameters.ClientAreaAnimation)
        {
            Activity.Visibility = Visibility.Collapsed;
            ReducedMotionMarker.Visibility = Visibility.Visible;
        }
    }

    public void SetStage(string stage)
    {
        StageText.Text = stage;
        StageText.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)?.UpdateTarget();
    }
}
