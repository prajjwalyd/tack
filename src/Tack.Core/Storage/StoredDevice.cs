using Tack.Core.Phone;

namespace Tack.Core.Storage;

/// <summary>One allowed device in board.json. Every field is required: a device missing one fails the file, as it did.</summary>
internal sealed class StoredDevice
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public required ulong ApprovedAt { get; set; }

    public Device ToDevice() => new() { Key = Key, Name = Name, ApprovedAt = ApprovedAt };

    public static StoredDevice Of(Device device) =>
        new() { Key = device.Key, Name = device.Name, ApprovedAt = device.ApprovedAt };
}
