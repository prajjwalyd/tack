using Microsoft.UI.Composition;

namespace Tack.App.Pinboard.Prints;

/// <summary>The pieces of a print its state looks move besides its named layers.</summary>
/// <param name="Peel">Everything on the card, turned about its top edge as a drag peels it up.</param>
/// <param name="Curl">The peeled paper's shading.</param>
/// <param name="Sheen">The photo's band of light.</param>
/// <param name="PinBody">Everything in the pin, larger while kept.</param>
/// <param name="Photo">The photo, which places the sheen.</param>
/// <param name="Keep">The Keep button's look.</param>
/// <param name="Unpin">The unpin button's look.</param>
sealed record PrintParts(
    Visual Peel, Visual Curl, Visual Sheen, Visual PinBody, PhotoFace Photo, PrintButton Keep, PrintButton Unpin);
