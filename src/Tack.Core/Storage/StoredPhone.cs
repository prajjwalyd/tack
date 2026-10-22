using Tack.Core.Phone;

namespace Tack.Core.Storage;

/// <summary>board.json's "phone".</summary>
internal sealed class StoredPhone
{
    public bool On { get; set; }

    public List<StoredDevice> Devices { get; set; } = [];

    public PhoneSettings ToSettings() => new() { On = On, Devices = [.. Devices.Select(d => d.ToDevice())] };

    public static StoredPhone Of(PhoneSettings settings) =>
        new() { On = settings.On, Devices = [.. settings.Devices.Select(StoredDevice.Of)] };
}
