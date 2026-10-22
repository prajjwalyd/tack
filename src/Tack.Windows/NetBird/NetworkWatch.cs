using System.Net.NetworkInformation;

namespace Tack.Windows.NetBird;

/// <summary>
/// Tells when this PC's network addresses change, until disposed. NetBird
/// connecting, disconnecting or getting another address shows up as one.
/// Windows reports it; nothing polls.
/// </summary>
public sealed class NetworkWatch : IDisposable
{
    public NetworkWatch() => NetworkChange.NetworkAddressChanged += OnAddressChanged;

    /// <summary>
    /// An address appeared, went or changed. Raised on a thread-pool thread, so a
    /// handler should do no more than wake someone, and must not throw.
    /// </summary>
    public event Action? Changed;

    void OnAddressChanged(object? sender, EventArgs e) => Changed?.Invoke();

    public void Dispose() => NetworkChange.NetworkAddressChanged -= OnAddressChanged;
}
