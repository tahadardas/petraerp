using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Controls;

public enum SkeletonProfile
{
    List,
    Document,
    Tree,
    Report,
}

/// <summary>A neutral placeholder bar shown only during a screen's first load. It carries no data and no progress value.</summary>
public sealed class SkeletonBlock : Control
{
    static SkeletonBlock() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SkeletonBlock), new FrameworkPropertyMetadata(typeof(SkeletonBlock)));
}

/// <summary>
/// Hosts a screen and renders its <see cref="PetraLoadingState"/>:
/// skeleton placeholders on the first load only; a thin indeterminate bar while refreshing (the data stays visible);
/// a blocking "Saving…"/"Posting…" overlay that prevents a second submit while keeping every typed value in place; and
/// an "outcome unknown" banner that tells the user to refresh and check before retrying. There is never a percentage.
/// </summary>
public sealed class LoadingHost : ContentControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(PetraLoadingState), typeof(LoadingHost),
        new PropertyMetadata(null, static (d, e) => ((LoadingHost)d).Observe(e.OldValue as PetraLoadingState, e.NewValue as PetraLoadingState)));
    public static readonly DependencyProperty SavingTextProperty = DependencyProperty.Register(nameof(SavingText), typeof(string), typeof(LoadingHost), new PropertyMetadata(null, Refresh));
    public static readonly DependencyProperty PostingTextProperty = DependencyProperty.Register(nameof(PostingText), typeof(string), typeof(LoadingHost), new PropertyMetadata(null, Refresh));
    public static readonly DependencyProperty LoadingTextProperty = DependencyProperty.Register(nameof(LoadingText), typeof(string), typeof(LoadingHost), new PropertyMetadata(null, Refresh));
    public static readonly DependencyProperty UnknownTextProperty = DependencyProperty.Register(nameof(UnknownText), typeof(string), typeof(LoadingHost));
    public static readonly DependencyProperty UnknownHelpTextProperty = DependencyProperty.Register(nameof(UnknownHelpText), typeof(string), typeof(LoadingHost));
    public static readonly DependencyProperty SkeletonProfileProperty = DependencyProperty.Register(nameof(SkeletonProfile), typeof(SkeletonProfile), typeof(LoadingHost), new PropertyMetadata(SkeletonProfile.List));

    private static readonly DependencyPropertyKey ShowSkeletonKey = DependencyProperty.RegisterReadOnly(nameof(ShowSkeleton), typeof(bool), typeof(LoadingHost), new PropertyMetadata(false));
    public static readonly DependencyProperty ShowSkeletonProperty = ShowSkeletonKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsRefreshingKey = DependencyProperty.RegisterReadOnly(nameof(IsRefreshing), typeof(bool), typeof(LoadingHost), new PropertyMetadata(false));
    public static readonly DependencyProperty IsRefreshingProperty = IsRefreshingKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsSubmittingKey = DependencyProperty.RegisterReadOnly(nameof(IsSubmitting), typeof(bool), typeof(LoadingHost), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSubmittingProperty = IsSubmittingKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsOutcomeUnknownKey = DependencyProperty.RegisterReadOnly(nameof(IsOutcomeUnknown), typeof(bool), typeof(LoadingHost), new PropertyMetadata(false));
    public static readonly DependencyProperty IsOutcomeUnknownProperty = IsOutcomeUnknownKey.DependencyProperty;
    private static readonly DependencyPropertyKey StatusTextKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(LoadingHost), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StatusTextProperty = StatusTextKey.DependencyProperty;

    static LoadingHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LoadingHost), new FrameworkPropertyMetadata(typeof(LoadingHost)));
        FocusableProperty.OverrideMetadata(typeof(LoadingHost), new FrameworkPropertyMetadata(false));
    }

    public PetraLoadingState? State { get => (PetraLoadingState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string? SavingText { get => (string?)GetValue(SavingTextProperty); set => SetValue(SavingTextProperty, value); }
    public string? PostingText { get => (string?)GetValue(PostingTextProperty); set => SetValue(PostingTextProperty, value); }
    public string? LoadingText { get => (string?)GetValue(LoadingTextProperty); set => SetValue(LoadingTextProperty, value); }
    public string? UnknownText { get => (string?)GetValue(UnknownTextProperty); set => SetValue(UnknownTextProperty, value); }
    public string? UnknownHelpText { get => (string?)GetValue(UnknownHelpTextProperty); set => SetValue(UnknownHelpTextProperty, value); }
    public SkeletonProfile SkeletonProfile { get => (SkeletonProfile)GetValue(SkeletonProfileProperty); set => SetValue(SkeletonProfileProperty, value); }
    public bool ShowSkeleton => (bool)GetValue(ShowSkeletonProperty);
    public bool IsRefreshing => (bool)GetValue(IsRefreshingProperty);
    public bool IsSubmitting => (bool)GetValue(IsSubmittingProperty);
    public bool IsOutcomeUnknown => (bool)GetValue(IsOutcomeUnknownProperty);
    public string StatusText => (string)GetValue(StatusTextProperty);

    private static void Refresh(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LoadingHost)d).Sync();

    private void Observe(PetraLoadingState? previous, PetraLoadingState? next)
    {
        if (previous is not null) PropertyChangedEventManager.RemoveHandler(previous, OnStateChanged, string.Empty);
        if (next is not null) PropertyChangedEventManager.AddHandler(next, OnStateChanged, string.Empty);
        Sync();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void Sync()
    {
        var state = State;
        SetValue(ShowSkeletonKey, state?.ShowSkeleton == true);
        SetValue(IsRefreshingKey, state?.IsRefreshing == true);
        SetValue(IsSubmittingKey, state?.IsSubmitting == true);
        SetValue(IsOutcomeUnknownKey, state?.IsOutcomeUnknown == true);
        SetValue(StatusTextKey, state?.State switch
        {
            LoadState.Saving => SavingText ?? string.Empty,
            LoadState.Posting => PostingText ?? string.Empty,
            LoadState.Loading => LoadingText ?? string.Empty,
            _ => string.Empty,
        });
    }
}
