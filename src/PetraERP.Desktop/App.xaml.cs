using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Desktop.Hosting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop;

public partial class App : System.Windows.Application, IDisposable
{
    private ServiceProvider? services;
    private DispatcherTimer? sessionTimer;
    private DesktopInstanceLease? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        PetraERP.Desktop.Controls.PetraNumberFormat.CaptureOperatingSystemCulture(System.Globalization.CultureInfo.CurrentCulture);
        base.OnStartup(e);
        var splash = new StartupSplashWindow();
        splash.Show();
        await Dispatcher.Yield(DispatcherPriority.Render);
        try
        {
            splash.SetStage("تحميل إعدادات التطبيق...");
            var settings = DesktopSettings.Load(Path.Combine(AppContext.BaseDirectory, "desktopsettings.json"));
            splash.SetStage("التحقق من جلسة Petra ERP...");
            try { instance = new DesktopInstanceLease(settings.ApiBaseUri); }
            catch (IOException) { splash.Close(); MessageBox.Show("تعذّر فتح جلسة محلية حصرية؛ تحقق من النوافذ المفتوحة وأذونات Windows. / Cannot acquire the local session lease; check running instances and Windows access."); Shutdown(); return; }
            splash.SetStage("تجهيز خدمات التطبيق...");
            services = DesktopComposition.Build(this, settings);
            // The last appearance used on this computer (theme, density, number display) applies before sign-in.
            splash.SetStage("تحميل إعدادات المستخدم...");
            services.GetRequiredService<Shell.UserPreferencesService>().Load(null);
            var window = services.GetRequiredService<DesktopWindow>();
            splash.SetStage("الاتصال بالخادم...");
            await window.Model.InitializeAsync().ConfigureAwait(true);
            splash.SetStage("تجهيز مساحة العمل...");
            MainWindow = window;
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Render);
            splash.Close();
            sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            sessionTimer.Tick += RevalidateSession; sessionTimer.Start();
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            splash.Close();
            MessageBox.Show("تعذّر بدء التطبيق. تحقق من إعدادات HTTPS وأذونات Windows. / Unable to start. Check HTTPS configuration and Windows access.", "PetraERP", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private async void RevalidateSession(object? sender, EventArgs args)
    {
        // The timer must not cover the screen: a periodic full reload looks like the application stopped working.
        if (MainWindow is DesktopWindow window) await window.Model.RevalidateSessionAsync().ConfigureAwait(true);
    }
    public void Dispose()
    {
        if (sessionTimer is not null) { sessionTimer.Stop(); sessionTimer.Tick -= RevalidateSession; sessionTimer = null; }
        services?.Dispose(); services = null;
        instance?.Dispose(); instance = null;
        GC.SuppressFinalize(this);
    }
    protected override void OnExit(ExitEventArgs e) { Dispose(); base.OnExit(e); }
}
