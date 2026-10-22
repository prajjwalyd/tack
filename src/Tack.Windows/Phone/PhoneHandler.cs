using Tack.Core.Phone.Http;
using Tack.Core.Phone.Serving;

namespace Tack.Windows.Phone;

/// <summary>Answers one request from a NetBird peer (<see cref="PhoneRoutes.HandleAsync"/>).</summary>
public delegate Task<PhoneResponse> PhoneHandler(RequestHead head, IRequestBody body, PeerMatch from);
