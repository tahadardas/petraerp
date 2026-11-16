using System.Windows;
using System.Windows.Threading;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// WPF allows exactly one <see cref="System.Windows.Application"/> per process, and <c>Shutdown()</c> does not undo
/// it: Application.Current stays non-null in a shutting-down state, so every later InitializeComponent throws
/// "The Application object is being shut down". A second <c>new Application()</c> throws too, and reaching across
/// threads is not an option - Application.Windows, the merged theme brushes and the control templates are all
/// thread-affine (WpfThemeService.Apply fails with "a different thread owns it").
/// Control templates in Themes/Components.xaml also resolve their StaticResource tokens against the ambient
/// Application only: a static reference inside a dictionary does not search the dictionary it was merged into, so
/// a test that renders those controls must host one.
/// Every test that needs an ambient Application therefore runs its UI work on this single dedicated STA host thread,
/// which creates the one Application and never shuts it down. Each run removes the theme resources it merged,
/// because the next test asserts on the merged-dictionary state.
/// </summary>
internal static class WpfApplicationHost
{
    internal static Dispatcher Dispatcher { get; private set; }
    internal static System.Windows.Application Application { get; private set; }

    static WpfApplicationHost()
    {
        var started = new ManualResetEventSlim(false);
        Exception? startupFailure = null;
        Dispatcher? dispatcher = null;
        System.Windows.Application? application = null;
        var thread = new Thread(() =>
        {
            try
            {
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                started.Set();
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception exception) { startupFailure = exception; started.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "PetraERP.WpfTestHost";
        thread.Start();
        if (!started.Wait(TimeSpan.FromSeconds(60))) throw new InvalidOperationException("The WPF test host thread never started.");
        if (startupFailure != null) throw startupFailure;
        Dispatcher = dispatcher!;
        Application = application!;
    }

    /// <summary>
    /// Runs synchronous UI work on the host thread. The dispatcher synchronization context is deliberately left
    /// unset here, matching the threads these tests used to own: bodies that block on GetAwaiter().GetResult()
    /// would otherwise deadlock against a pump they themselves are blocking.
    /// </summary>
    internal static void Run(Action work) => Dispatcher.Invoke(work, DispatcherPriority.Send);

    /// <summary>Runs async UI work on the host thread and pumps until it completes, without blocking that thread.</summary>
    internal static async Task RunAsync(Func<Task> work, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));
            try
            {
                await work().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }));
        await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Merges shipped theme dictionaries onto the ambient Application, exactly like App.xaml does.</summary>
    internal static void MergeThemes(params string[] paths)
    {
        foreach (var path in paths)
            Application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PetraERP.Desktop;component/" + path, UriKind.Absolute)
            });
    }

    internal static void ResetResources()
    {
        Application.Resources.MergedDictionaries.Clear();
        Application.Resources.Clear();
    }
}
