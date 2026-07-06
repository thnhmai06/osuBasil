using System.Net;
using Bancho.Domain;

namespace Bancho.Application.Abstractions;

/// <summary>
/// Fetches geolocation by IP via ip-api.com. Ported from app/state/services.py's
/// _fetch_geoloc_from_ip — the header-based path (Cloudflare/nginx) is pure logic and lives in
/// Bancho.Domain's GeolocationHeaderParser instead, since it needs no network I/O.
/// </summary>
public interface IGeolocationProvider
{
    Task<Geolocation?> FetchByIpAsync(IPAddress ip, CancellationToken cancellationToken = default);
}
