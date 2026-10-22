using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tack.Core.Diagnostics;
using Tack.Core.Phone;
using Tack.Windows.Clipboard;
using Tack.Windows.Shell;

namespace Tack.App.Settings.Pages.Phone;

/// <summary>
/// The body of the phone page under its switch (<see cref="PhoneWords.Body"/>):
/// what NetBird needs, a quiet line, or the QR code and the address with Copy.
/// Each is built once and shown or hidden, never rebuilt, so the focus stays.
/// </summary>
public sealed partial class PhoneBodyView : UserControl
{
    /// <summary>How long Copy says "Copied".</summary>
    static readonly TimeSpan s_copiedFor = TimeSpan.FromMilliseconds(1800);

    SettingsContext? _context;
    DispatcherQueueTimer? _copied;
    QrMatrix? _qr;

    public PhoneBodyView()
    {
        InitializeComponent();
        GetNetBird.Click += (_, _) => ShellLauncher.OpenUrl(PhoneWords.NetBirdInstall);
        Copy.Click += OnCopy;
    }

    /// <summary>Once, with the window's context.</summary>
    internal void Start(SettingsContext context)
    {
        _context = context;
        _copied = context.Ui.CreateTimer();
        _copied.Interval = s_copiedFor;
        _copied.IsRepeating = false;
        _copied.Tick += (_, _) => Copy.Content = "Copy";
    }

    /// <summary>Shows <paramref name="body"/>, hiding the others.</summary>
    public void Render(PhoneBody body)
    {
        var kind = body.Kind;
        GetNetBirdCard.Visibility = Seen(kind == PhoneBodyKind.GetNetBird);
        Quiet.Visibility = Seen(kind is PhoneBodyKind.Off or PhoneBodyKind.GettingReady);
        NoteCard.Visibility = Seen(kind == PhoneBodyKind.Disconnected);
        ShareCard.Visibility = Seen(kind == PhoneBodyKind.Share);
        ProblemCard.Visibility = Seen(kind == PhoneBodyKind.Problem);
        switch (kind)
        {
            case PhoneBodyKind.GetNetBird:
                GetNetBirdText.Text = body.Text;
                break;
            case PhoneBodyKind.Off or PhoneBodyKind.GettingReady:
                Quiet.Text = body.Text;
                break;
            case PhoneBodyKind.Disconnected:
                NoteText.Text = body.Text;
                break;
            case PhoneBodyKind.Share:
                ShowShare(body);
                break;
            case PhoneBodyKind.Problem:
                ProblemText.Text = body.Text;
                break;
        }
    }

    void ShowShare(PhoneBody body)
    {
        var address = body.Address ?? string.Empty;
        ScanText.Text = body.Text;
        if (Address.Text != address)
        {
            Address.Text = address;
            AutomationProperties.SetHelpText(Copy, address);
            AutomationProperties.SetName(QrTile, $"QR code for {address}");
        }
        QrTile.Visibility = Seen(body.Qr is not null);
        if (!ReferenceEquals(_qr, body.Qr))
        {
            _qr = body.Qr;
            QrPath.Data = body.Qr is null ? null : QrPicture.Of(body.Qr);
        }
    }

    async void OnCopy(object sender, RoutedEventArgs e)
    {
        var address = Address.Text;
        if (address.Length == 0)
        {
            return;
        }
        try
        {
            await ClipboardWriter.CopyTextAsync(address);
        }
        catch (IOException error)
        {
            DevTrace.Write($"settings: copying the address failed: {error.Message}");
            // Selected, so Ctrl+C is one key away.
            Address.SelectAll();
            _context?.Announce(PhoneWords.CopyFailed);
            return;
        }
        Copy.Content = "Copied";
        _context?.Announce(PhoneWords.AddressCopied);
        _copied?.Stop();
        _copied?.Start();
    }

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
