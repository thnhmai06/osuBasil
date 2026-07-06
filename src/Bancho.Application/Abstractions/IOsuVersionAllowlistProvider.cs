using Bancho.Domain;

namespace Bancho.Application.Abstractions;

/// <summary>
/// Ported from app/api/domains/cho.py's get_allowed_client_versions — queries the osu!api v2
/// changelog to determine which client build dates are still acceptable for a stream. Returns
/// null if the osu!api request fails (bancho.py then allows the connection through rather than
/// blocking legitimate players due to an outage).
/// </summary>
public interface IOsuVersionAllowlistProvider
{
    Task<IReadOnlySet<DateOnly>?> GetAllowedVersionsAsync(OsuStream stream, CancellationToken cancellationToken = default);
}
