namespace Basil.Web.Middleware;

/// <summary>
///     Endpoint metadata tagging every SSE-capable route on the `api.` host (whether always-SSE like
///     `GET /matches/{matchId}/live` or content-negotiated like `GET /matches/{matchId}/hosts`) so
///     <see cref="EnvelopeMiddleware" /> can skip buffering it entirely — critical, not cosmetic:
///     buffering the whole response until the handler completes would silently turn a live push stream
///     into one that never delivers a single event until the connection eventually closes. Applied via
///     <c>.WithMetadata(SseEndpointMarker.Instance)</c> on each such route registration.
/// </summary>
public sealed class SseEndpointMarker
{
    public static readonly SseEndpointMarker Instance = new();

    private SseEndpointMarker()
    {
    }
}
