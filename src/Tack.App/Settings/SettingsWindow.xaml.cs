using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tack.Core.Settings;
using Windows.System;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;

namespace Tack.App.Settings;

/// <summary>
/// The Settings window: one window, two pages switched in place as Windows
/// Settings does, the list of groups and the phone page. Every change applies
/// at once through <see cref="SettingsChanges"/>; there is no Save. While it
/// has the keyboard focus it holds the board down, so a change shows live.
/// Esc closes it, Alt+Left goes back from the phone page. UI thread only.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    const string Activity = "Tack.Settings.Announcement";

    readonly SettingsChanges _settings;
    readonly Reveal _reveal;
    readonly SettingsContext _context;
    readonly ISettingsSection[] _sections;
    bool _loaded;
    bool _active;
    string? _wanted;

    internal SettingsWindow(SettingsChanges settings, Reveal reveal)
    {
        (_settings, _reveal) = (settings, reveal);
        InitializeComponent();
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Tack.ico"));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        _context = new SettingsContext(settings, Root, DispatcherQueue, Go, () => PhoneScroll.Visibility == Visibility.Visible, FocusTop, Announce);
        _sections = [PhoneCardSection, GeneralSection, SpeedSection, AppearanceSection, ShortcutsSection, UpdatesSection, PhonePage];
        foreach (var section in _sections)
        {
            section.Start(_context);
        }
        Render();
        settings.Changed += Render;
        Activated += OnActivated;
        Closed += OnClosed;
        Root.KeyDown += OnKeyDown;
        Root.Loaded += OnLoaded;
        Back.Click += (_, _) => ToList();
        ListScroll.ViewChanged += (_, _) => SyncHead();
        PhoneScroll.ViewChanged += (_, _) => SyncHead();
    }

    /// <summary>The window's handle, for placing it.</summary>
    public nint Handle { get; }

    /// <summary>
    /// Goes where <paramref name="id"/> names (<see cref="SettingsRoute"/>): the
    /// phone page, a group of the list scrolled to the top, or the top of the
    /// list. The keyboard focus follows only while the window has it, so one
    /// that opened by itself never takes the keyboard from another app.
    /// </summary>
    internal void Go(string? id)
    {
        if (!_loaded)
        {
            _wanted = id;
            return;
        }
        Show(SettingsRoute.Of(id));
    }

    /// <summary>Back from the phone page to the list, at the card it was opened from.</summary>
    void ToList() => Show(new SettingsRoute(false, SettingsRoute.PhoneId));

    void Show(SettingsRoute route)
    {
        var phone = route.Phone;
        ListScroll.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        PhoneScroll.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
        Back.Visibility = Crumb.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = phone ? "Use on your phone" : "Settings";
        FrameworkElement? target = phone ? PhonePage : Array.Find(_sections, s => s.Id == route.Section) as FrameworkElement;
        if (phone)
        {
            PhoneScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        else if (target is not null)
        {
            Root.UpdateLayout();
            var top = target.TransformToVisual(List).TransformPoint(default).Y;
            // A little air above the heading, as at the top of the list.
            ListScroll.ChangeView(null, Math.Max(0, top - 4), null, disableAnimation: true);
        }
        SyncHead();
        if (_active && target is not null && FocusManager.FindFirstFocusableElement(target) is Control first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        Go(_wanted);
    }

    void Render()
    {
        var settings = _settings.Current;
        foreach (var section in _sections)
        {
            section.Render(settings);
        }
    }

    /// <summary>The settings hold: the board stays down while this window has the keyboard focus.</summary>
    void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!_active)
        {
            _context.RaiseLeaving();
        }
        _reveal.HoldForSettings(_active);
    }

    void OnClosed(object sender, WindowEventArgs args)
    {
        _active = false;
        _settings.Changed -= Render;
        _context.RaiseLeaving();
        // Closed while a shortcut was listening: the shortcuts come back.
        _settings.PauseShortcuts(false);
        _reveal.HoldForSettings(false);
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Escape && !alt)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == VirtualKey.Left && alt && PhoneScroll.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            ToList();
        }
    }

    /// <summary>A hairline under the title once something is scrolled beneath it.</summary>
    void SyncHead()
    {
        var scroll = PhoneScroll.Visibility == Visibility.Visible ? PhoneScroll : ListScroll;
        HeadLine.Opacity = scroll.VerticalOffset > 0 ? 1 : 0;
    }

    void FocusTop()
    {
        var page = PhoneScroll.Visibility == Visibility.Visible ? (DependencyObject)PhonePage : List;
        if (FocusManager.FindFirstFocusableElement(page) is Control first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    void Announce(string text)
    {
        Live.Text = text;
        var peer = FrameworkElementAutomationPeer.FromElement(Live) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Live);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, Activity);
    }
}
