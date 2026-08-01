using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Scores;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Anticheat;

/// <summary>
///     Indicates whether a LastFM telemetry report was fully handled.
/// </summary>
public enum ClientIntegrityResult : byte
{
	/// <summary>No violation was detected.</summary>
	Empty,

	/// <summary>The report needs no further processing.</summary>
	StopSending
}

/// <summary>
///     Evaluates anticheat flags reported by osu! clients through the LastFM telemetry endpoint.
/// </summary>
/// <remarks>
///     A flagged player who is currently in a match gets BasilBot posting a warning to that match's
///     own chat channel and a direct message to every referee of the match, naming the player,
///     room, and reason. A flagged player who is not in a match produces no side effect at all.
///     This deliberately drops the restrict, force-logout, and ban-roll machinery that a ranked
///     server would apply, because Basil has no restrict system.
/// </remarks>
public sealed class ClientIntegrityService(
	IPlayerSessionRegistry sessionRegistry,
	MatchMembershipService matchMembership,
	ILogger<ClientIntegrityService> logger)
{
	/// <summary>
	///     Evaluates a single LastFM telemetry report for client integrity violations.
	/// </summary>
	/// <param name="player">The player session the report belongs to.</param>
	/// <param name="beatmapIdOrHiddenFlag">The telemetry value: either a beatmap id or the encoded anticheat flag payload.</param>
	/// <param name="cancellationToken">A token that cancels the report.</param>
	/// <returns>A <see cref="ClientIntegrityResult" /> describing the outcome.</returns>
	/// <remarks>
	///     Only values beginning with the <c>a</c> marker are treated as flag payloads; the flag
	///     bits after the marker are parsed defensively, so malformed input is ignored rather than
	///     thrown. HQ-cheat assembly, HQ-cheat file, and registry-edit flags trigger a report; all
	///     other flags are ignored.
	/// </remarks>
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
			logger.LogInformation("Anticheat flag: UserId={UserId} Username={Username} Flags={Flags} MatchId={MatchId}",
				player.Id, player.Name, flags, player.Match?.DbId);
			ReportFlag(player, $"hq!osu running ({flags})");
			return Task.FromResult(ClientIntegrityResult.StopSending);
		}

		if ((flags & LastFmFlags.RegistryEdits) != 0)
		{
			logger.LogInformation("Anticheat flag: UserId={UserId} Username={Username} Flags={Flags} MatchId={MatchId}",
				player.Id, player.Name, flags, player.Match?.DbId);
			ReportFlag(player, "hq!osu tool registry edits detected");
			return Task.FromResult(ClientIntegrityResult.StopSending);
		}

		return Task.FromResult(ClientIntegrityResult.Empty);
	}

	private void ReportFlag(PlayerSession player, string reason)
	{
		var match = player.Match;
		if (match is null)
		{
			logger.LogInformation("Anticheat flag had no effect: UserId={UserId} Reason={Reason} (not in a match)",
				player.Id, reason);
			return;
		}

		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
		if (bot is null) return;

		matchMembership.EnqueueChat(match, bot.Name, bot.Id, $"Anti-cheat flag for {player.Name}: {reason}");

		var dm = $"Anti-cheat flag in match #{match.DbId} {match.Name}: {player.Name} — {reason}";
		logger.LogDebug("Anticheat flag reported: MatchId={MatchId} RefereeIds={RefereeIds}",
			match.DbId, match.Referees);
		foreach (var refereeId in match.Referees)
		{
			var referee = sessionRegistry.GetById(refereeId);
			referee?.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, referee.Name, dm));
		}
	}
}