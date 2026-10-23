namespace Tack.Core.Updates;

/// <summary>
/// Where updates come from and whose signature they need: the same feed and
/// key as Tack 0.2, so every installed Tack, old or new, updates the same way.
/// </summary>
public static class UpdateTrust
{
    /// <summary>
    /// Tack's update-signing public key (minisign; public by design), as the
    /// base64 of its .pub text. The matching private key signs the releases in
    /// CI and nothing else; an update it did not sign is never run.
    /// </summary>
    public const string PublicKey =
        "dW50cnVzdGVkIGNvbW1lbnQ6IG1pbmlzaWduIHB1YmxpYyBrZXk6IDlEMjBGNUEwRjIzMUQ5MkYKUldRdjJUSHlvUFVnblhFbXRrbVRyT1BiQzNabis2Zks0Q0NQN0lrV0ptam9hUnhCUk5LTkNyNEcK";

    /// <summary>The one place Tack looks for a new version: latest.json of the newest published release.</summary>
    public const string Feed = "https://github.com/prajjwalyd/tack/releases/latest/download/latest.json";
}
