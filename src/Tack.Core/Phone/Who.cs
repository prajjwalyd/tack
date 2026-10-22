namespace Tack.Core.Phone;

/// <summary>A device asking to use the board, as NetBird knows it.</summary>
/// <param name="Key">Its WireGuard public key: who it is.</param>
/// <param name="Name">Its short NetBird name ("pixel").</param>
/// <param name="Fqdn">Its full NetBird name ("pixel.netbird.cloud").</param>
/// <param name="Ip">Its NetBird address.</param>
public sealed record Who(string Key, string Name, string Fqdn, string Ip);
