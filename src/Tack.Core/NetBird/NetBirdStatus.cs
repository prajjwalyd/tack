using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Tack.Core.NetBird;

/// <summary>
/// What Tack needs from <c>netbird status --json</c>: this device's address and
/// name on the NetBird network, and the peers it can see. Only the fields Tack
/// uses are read; NetBird adds fields over time.
/// </summary>
/// <param name="Ip">This device's NetBird address, e.g. 100.90.53.74.</param>
/// <param name="Fqdn">Its NetBird name. Never assumed to end in netbird.cloud: self-hosted networks have their own domain.</param>
public sealed record NetBirdStatus(IPAddress Ip, string Fqdn, IReadOnlyList<Peer> Peers)
{
    /// <summary>The peer at <paramref name="ip"/>, if NetBird lists it.</summary>
    public Peer? PeerAt(IPAddress ip) => Peers.FirstOrDefault(p => p.Ip.Equals(ip));

    /// <summary>Reads the status. Null when NetBird is not connected (no address yet) or the output is not what Tack expects.</summary>
    public static NetBirdStatus? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (ParseIpv4(Text(root, "netbirdIp")) is not { } ip)
            {
                return null;
            }
            var fqdn = Text(root, "fqdn").TrimEnd('.');
            if (fqdn.Length == 0)
            {
                return null;
            }
            var peers = new List<Peer>();
            if (root.TryGetProperty("peers", out var list) && list.TryGetProperty("details", out var details))
            {
                foreach (var peer in details.EnumerateArray())
                {
                    var key = Text(peer, "publicKey");
                    if (key.Length > 0 && ParseIpv4(Text(peer, "netbirdIp")) is { } address)
                    {
                        peers.Add(new Peer(address, Text(peer, "fqdn").TrimEnd('.'), key));
                    }
                }
            }
            return new NetBirdStatus(ip, fqdn, peers);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// "100.90.53.74/16" or "100.90.53.74" to the address: four plain decimal
    /// numbers only, as Tack 0.2 read it. Anything else is null.
    /// </summary>
    public static IPAddress? ParseIpv4(string text)
    {
        var parts = text.Split('/')[0].Trim().Split('.');
        if (parts.Length != 4)
        {
            return null;
        }
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var part = parts[i];
            var plain = part.Length is >= 1 and <= 3 && part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0');
            if (!plain || !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }
        return new IPAddress(bytes);
    }

    /// <summary>A string field, or empty when it is missing; a field of another type does not parse.</summary>
    static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
