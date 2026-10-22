namespace Tack.Windows.Phone;

/// <summary>The board could not be served; the message says why in a few words, as "port 7717 is in use".</summary>
public sealed class PhoneServeException(string message, Exception? inner = null) : Exception(message, inner);
