using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.UseCases.Anticheat;

/// <summary>Ported from app/services/client_integrity.py's ClientIntegrityResult.</summary>
public enum ClientIntegrityResult
{
    Empty,
    StopSending,
}

/// <summary>
/// Ported from app/services/client_integrity.py's ClientIntegrityService.handle_lastfm_flags.
/// Diverges from Python by explicit user decision: detected cheat-tool flags are only logged via
/// ILogRepository for later manual review — the restrict/force-logout/random-ban-roll/Discord-
/// webhook side effects are all dropped, since bancho-net has no restrict machinery yet (Phase 10
/// is deferred along with the rest of the chat command system it would otherwise ride on).
/// </summary>
public sealed class ClientIntegrityService(ILogRepository logs)
{
    public async Task<ClientIntegrityResult> HandleLastFmFlagsAsync(
        PlayerSession player, string beatmapIdOrHiddenFlag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(beatmapIdOrHiddenFlag) || beatmapIdOrHiddenFlag[0] != 'a')
        {
            return ClientIntegrityResult.StopSending;
        }

        var flags = (LastFMFlags)int.Parse(beatmapIdOrHiddenFlag.AsSpan(1));

        if ((flags & (LastFMFlags.HqAssembly | LastFMFlags.HqFile)) != 0)
        {
            await logs.CreateAsync(0, player.Id, "lastfm_flag", $"hq!osu running ({flags})", cancellationToken);
            return ClientIntegrityResult.StopSending;
        }

        if ((flags & LastFMFlags.RegistryEdits) != 0)
        {
            await logs.CreateAsync(0, player.Id, "lastfm_flag", "hq!osu relife multiaccounting tool registry edits detected", cancellationToken);
            return ClientIntegrityResult.StopSending;
        }

        return ClientIntegrityResult.Empty;
    }
}
