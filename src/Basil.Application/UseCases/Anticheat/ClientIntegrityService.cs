using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Domain;

namespace Basil.Application.UseCases.Anticheat;

/// <summary>Ported from app/services/client_integrity.py's ClientIntegrityResult.</summary>
public enum ClientIntegrityResult
{
    Empty,
    StopSending
}

/// <summary>
///     Ported from app/services/client_integrity.py's ClientIntegrityService.handle_lastfm_flags.
///     Diverges from Python by explicit user decision: detected cheat-tool flags are only logged via
///     ILogRepository for later manual review — the restrict/force-logout/random-ban-roll/Discord-
///     webhook side effects are all dropped, since Basil has no restrict machinery.
/// </summary>
public sealed class ClientIntegrityService(ILogRepository logs)
{
    public async Task<ClientIntegrityResult> HandleLastFmFlagsAsync(
        PlayerSession player, string beatmapIdOrHiddenFlag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(beatmapIdOrHiddenFlag) || beatmapIdOrHiddenFlag[0] != 'a')
            return ClientIntegrityResult.StopSending;

        var flags = (LastFmFlags)int.Parse(beatmapIdOrHiddenFlag.AsSpan(1));

        if ((flags & (LastFmFlags.HqAssembly | LastFmFlags.HqFile)) != 0)
        {
            await logs.CreateAsync(0, player.Id, "lastfm_flag", $"hq!osu running ({flags})", cancellationToken);
            return ClientIntegrityResult.StopSending;
        }

        if ((flags & LastFmFlags.RegistryEdits) != 0)
        {
            await logs.CreateAsync(0, player.Id, "lastfm_flag",
                "hq!osu relife multiaccounting tool registry edits detected", cancellationToken);
            return ClientIntegrityResult.StopSending;
        }

        return ClientIntegrityResult.Empty;
    }
}