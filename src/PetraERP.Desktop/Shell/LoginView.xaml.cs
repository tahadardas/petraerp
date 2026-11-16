using System.Windows.Controls;

namespace PetraERP.Desktop.Shell;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (IsVisible) UserNameBox.Focus(); };
    }
}
