namespace Tack.Core.Phone.Http;

/// <summary>The status each <see cref="HeadError"/> is answered with.</summary>
public static class HeadErrors
{
    /// <summary>The status to answer with, or null for <see cref="HeadError.Gone"/>, which gets no answer.</summary>
    public static int? Status(this HeadError error) => error switch
    {
        HeadError.HeadTooLarge => 431,
        HeadError.Malformed => 400,
        HeadError.LengthRequired => 411,
        _ => null,
    };
}
