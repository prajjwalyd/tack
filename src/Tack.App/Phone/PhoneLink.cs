using Microsoft.UI.Dispatching;
using Tack.Core.Diagnostics;
using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using Tack.Core.Prints;

namespace Tack.App.Phone;

/// <summary>
/// The board on the person's phone, over NetBird, for the app's whole life:
/// what Settings shows of it and the answers it gives, and who may get in.
/// Serving follows the switch and NetBird (<see cref="PhoneServing"/>);
/// the gate, which is not thread-safe, is used under this link's own lock.
/// </summary>
/// <remarks>
/// NetBird is looked at when Tack starts with the switch on, when the switch
/// flips, and, while it is on, when the network changes; nothing polls. The
/// routes ask the gate from the server's connections, on the thread pool.
/// <see cref="Changed"/> and <see cref="DeviceAsks"/> are raised on the UI thread.
/// </remarks>
sealed class PhoneLink : IPhoneLink, IDisposable
{
    readonly SettingsChanges _settings;
    readonly DispatcherQueue _ui;
    readonly PhoneServing _serving;
    readonly Lock _lock = new();
    readonly PhoneGate _gate = new();

    /// <summary>The switch as last heard from the settings, to tell when it flips. UI thread only.</summary>
    bool _on;

    /// <summary>The QR code for an address, made once per address. UI thread only.</summary>
    (string Address, QrMatrix? Qr)? _qr;

    public PhoneLink(SettingsChanges settings, DispatcherQueue ui)
    {
        (_settings, _ui) = (settings, ui);
        _serving = new PhoneServing(settings, RaiseChanged, ClearGate);
    }

    public event Action? Changed;

    /// <summary>A device asks to use the board, for the first time this request: the person should see it. Raised on the UI thread.</summary>
    public event Action? DeviceAsks;

    /// <summary>
    /// Starts following the switch, with <paramref name="host"/> for the routes,
    /// and serves at once if the switch was left on. UI thread, once.
    /// </summary>
    public void Start(IPhoneHost host)
    {
        _serving.Start(host);
        _on = _settings.Current.Phone.On;
        _settings.Changed += OnSettingsChanged;
        if (_on)
        {
            _serving.Nudge();
        }
    }

    public PhoneState State(PhoneSettings saved)
    {
        if (!saved.On)
        {
            _serving.Look();
        }
        var (netBird, address, error) = _serving.Now;
        IReadOnlyList<Asking> pending;
        lock (_lock)
        {
            _gate.Lapse(History.NowMs());
            pending = [.. _gate.Pending];
        }
        return new PhoneState
        {
            On = saved.On,
            NetBird = netBird,
            Serving = address is not null,
            Address = address,
            Qr = address is null ? null : QrFor(address),
            Error = error,
            Devices = saved.Devices,
            Pending = pending,
        };
    }

    public bool Answer(string key, bool allow)
    {
        var now = History.NowMs();
        string? name;
        lock (_lock)
        {
            name = _gate.Answer(key, allow, now);
        }
        if (name is not null && allow)
        {
            _settings.Change(s => s with { Phone = s.Phone.Allow(key, name, (ulong)now) });
        }
        RaiseChanged();
        return name is not null;
    }

    /// <summary>A request from the page on <paramref name="peer"/> (<see cref="PhoneGate.Check"/>). Any thread.</summary>
    public Verdict Admit(Peer peer) => Decide(peer, static (gate, saved, who, code, now) => gate.Check(saved, who, code, now));

    /// <summary>A turned-down device asks again from its page (<see cref="PhoneGate.AskAgain"/>). Any thread.</summary>
    public Verdict AskAgain(Peer peer) => Decide(peer, static (gate, saved, who, code, now) => gate.AskAgain(saved, who, code, now));

    /// <summary>Stops serving and watching the network, for good: Tack is quitting. UI thread.</summary>
    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _serving.Dispose();
    }

    /// <summary>Puts <paramref name="peer"/>'s request to the gate with a fresh code; a device asking for the first time is shown to the person.</summary>
    Verdict Decide(Peer peer, Func<PhoneGate, PhoneSettings, Who, string, long, Verdict> rule)
    {
        var saved = _settings.Current.Phone;
        var who = new Who(peer.Key, peer.ShortName, peer.Fqdn, peer.Ip.ToString());
        Verdict verdict;
        lock (_lock)
        {
            verdict = rule(_gate, saved, who, AskCode.Fresh(), History.NowMs());
        }
        if (verdict is Verdict.Waiting { Asked: true })
        {
            DevTrace.Write("phone: a device asks to use the board");
            OnUi(() =>
            {
                Changed?.Invoke();
                DeviceAsks?.Invoke();
            });
        }
        return verdict;
    }

    /// <summary>The board stopped being served: the requests waiting on it go.</summary>
    void ClearGate()
    {
        lock (_lock)
        {
            _gate.Clear();
        }
    }

    /// <summary>The switch flipped, from Settings or the tray: serve or stop to match.</summary>
    void OnSettingsChanged()
    {
        var on = _settings.Current.Phone.On;
        if (on != _on)
        {
            _on = on;
            _serving.Nudge();
        }
    }

    QrMatrix? QrFor(string address)
    {
        if (_qr is not { } known || known.Address != address)
        {
            known = (address, QrEncoder.Encode(address));
            _qr = known;
        }
        return known.Qr;
    }

    void RaiseChanged() => OnUi(() => Changed?.Invoke());

    void OnUi(Action raise)
    {
        if (_ui.HasThreadAccess)
        {
            raise();
        }
        else
        {
            _ui.TryEnqueue(() => raise());
        }
    }
}
