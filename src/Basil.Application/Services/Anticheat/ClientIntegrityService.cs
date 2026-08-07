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
///     A flagged userSession who is currently in a match gets BasilBot posting a warning to that match's
///     own chat channel and a direct message to every referee of the match, naming the userSession,
///     room, and reason. A flagged userSession who is not in a match produces no side effect at all.
///     There is no restrict, force-logout, or ban machinery: this service only reports.
/// </remarks>
public sealed class ClientIntegrityService(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	MatchMembershipService matchMembership,
	ILogger<ClientIntegrityService> logger)
{
	/// <summary>
	///     Evaluates a single LastFM telemetry report for client integrity violations.
	/// </summary>
	/// <param name="userSession">The userSession session the report belongs to.</param>
	/// <param name="beatmapIdOrHiddenFlag">The telemetry value: either a beatmap id or the encoded anticheat flag payload.</param>
	/// <returns>A <see cref="ClientIntegrityResult" /> describing the outcome.</returns>
	/// <remarks>
	///     Only values beginning with the <c>a</c> marker are treated as flag payloads; the flag
	///     bits after the marker are parsed defensively, so malformed input is ignored rather than
	///     thrown. HQ-cheat assembly, HQ-cheat file, and registry-edit flags trigger a report; all
	///     other flags are ignored.
	/// </remarks>
	public Task<ClientIntegrityResult> HandleLastFmFlagsAsync(
		GameSession userSession, string beatmapIdOrHiddenFlag)
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
				userSession.Id, userSession.Name, flags, userSession.Match?.DbId);
			ReportFlag(userSession, $"hq!osu running ({flags})");
			return Task.FromResult(ClientIntegrityResult.StopSending);
		}

		if ((flags & LastFmFlags.RegistryEdits) != 0)
		{
			logger.LogInformation("Anticheat flag: UserId={UserId} Username={Username} Flags={Flags} MatchId={MatchId}",
				userSession.Id, userSession.Name, flags, userSession.Match?.DbId);
			ReportFlag(userSession, "hq!osu tool registry edits detected");
			return Task.FromResult(ClientIntegrityResult.StopSending);
		}

		return Task.FromResult(ClientIntegrityResult.Empty);
	}

	private void ReportFlag(GameSession userSession, string reason)
	{
		var match = userSession.Match;
		if (match is null)
		{
			logger.LogInformation("Anticheat flag had no effect: UserId={UserId} Reason={Reason} (not in a match)",
				userSession.Id, reason);
			return;
		}

		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is null) return;

		matchMembership.EnqueueChat(match, bot.Name, bot.Id, $"Anti-cheat flag for {userSession.Name}: {reason}");

		var dm = $"Anti-cheat flag in match #{match.DbId} {match.Name}: {userSession.Name} — {reason}";
		logger.LogDebug("Anticheat flag reported: MatchId={MatchId} RefereeIds={RefereeIds}",
			match.DbId, match.Referees);
		foreach (var refereeId in match.Referees)
		{
			if (gameRegistry.GetByUserId(refereeId) is { } referee)
				referee.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, referee.Name, dm));
			if (ircRegistry.GetByUserId(refereeId) is { } irc)
				irc.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, irc.Name, dm));
		}
	}
}