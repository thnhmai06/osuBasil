using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Scores;
using Basil.Protocol.Irc;

namespace Basil.Application.Services.Anticheat;

/// <summary>Ported from app/services/client_integrity.py's ClientIntegrityResult.</summary>
public enum ClientIntegrityResult : byte
{
    Empty,
    StopSending
}

/// <summary>
///     Ported from app/services/client_integrity.py's ClientIntegrityService.handle_lastfm_flags.
///     Diverges from Python by explicit user decision: the restrict/force-logout/random-ban-roll/
///     Discord-webhook side effects are all dropped, since Basil has no restrict machinery. Instead
///     of a log entry, a flagged player currently in a match gets BasilBot posting a warning to that
///     match's own chat channel, plus a DM to every referee of that match naming the player/room/
///     reason. A flagged player not in any match produces no side effect at all.
/// </summary>
public sealed class ClientIntegrityService(
    IPlayerSessionRegistry sessionRegistry,
    MatchMembershipService matchMembership)
{
    public Task<ClientIntegrityResult> HandleLastFmFlagsAsync(
        PlayerSession player, string beatmapIdOrHiddenFlag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(beatmapIdOrHiddenFlag) || beatmapIdOrHiddenFlag[0] != 'a')
            return Task.FromResult(ClientIntegrityResult.StopSending);

        // A malformed suffix (not a valid int) is treated the same as "not a lastfm flag" instead of
        // throwing FormatException/OverflowException out of an unvalidated client-supplied string.
        if (!int.TryParse(beatmapIdOrHiddenFlag.AsSpan(1), out var rawFlags))
            return Task.FromResult(ClientIntegrityResult.StopSending);

        var flags = (LastFmFlags)rawFlags;

        if ((flags & (LastFmFlags.HqAssembly | LastFmFlags.HqFile)) != 0)
        {
            ReportFlag(player, $"hq!osu running ({flags})");
            return Task.FromResult(ClientIntegrityResult.StopSending);
        }

        if ((flags & LastFmFlags.RegistryEdits) != 0)
        {
            ReportFlag(player, "hq!osu tool registry edits detected");
            return Task.FromResult(ClientIntegrityResult.StopSending);
        }

        return Task.FromResult(ClientIntegrityResult.Empty);
    }

    private void ReportFlag(PlayerSession player, string reason)
    {
        var match = player.Match;
        if (match is null) return;

        var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
        if (bot is null) return;

        matchMembership.EnqueueChat(match, bot.Name, bot.Id, $"Anti-cheat flag for {player.Name}: {reason}");

        var dm = $"Anti-cheat flag in match #{match.DbId} {match.Name}: {player.Name} — {reason}";
        foreach (var refereeId in match.Referees)
        {
            var referee = sessionRegistry.GetById(refereeId);
            referee?.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, referee.Name, dm));
        }
    }
}
