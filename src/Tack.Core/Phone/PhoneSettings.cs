namespace Tack.Core.Phone;

/// <summary>The phone board's saved settings: the user's switch and the devices they allowed.</summary>
public sealed record PhoneSettings
{
    /// <summary>Serve the board to the user's devices.</summary>
    public bool On { get; init; }

    public IReadOnlyList<Device> Devices { get; init; } = [];

    /// <summary>Whether <paramref name="key"/> may use the board, without asking anybody.</summary>
    public bool Allows(string key) => Devices.Any(d => d.Key == key);

    /// <summary>Allows a device, again if need be: its name and date are updated, and it moves to the end.</summary>
    public PhoneSettings Allow(string key, string name, ulong nowMs) =>
        this with { Devices = [.. Devices.Where(d => d.Key != key), new Device { Key = key, Name = name, ApprovedAt = nowMs }] };

    /// <summary>Removes a device; it has to ask again. The same settings if it was not there.</summary>
    public PhoneSettings Forget(string key) =>
        Allows(key) ? this with { Devices = [.. Devices.Where(d => d.Key != key)] } : this;

    public bool Equals(PhoneSettings? other) =>
        other is not null && On == other.On && Devices.SequenceEqual(other.Devices);

    public override int GetHashCode() => HashCode.Combine(On, Devices.Count);
}
