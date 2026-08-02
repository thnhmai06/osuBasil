using Basil.Application.Abstractions.Multiplayer;
using Basil.Domain.Multiplayer;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Recovers matches and rounds left open by an abnormal shutdown.
/// </summary>
/// <remarks>
///     Runs once at startup. Every match whose row never recorded an end is treated as orphaned:
///     its open rounds are marked ended (aborted) and the match itself is marked ended, each
///     accompanied by a <c>Closed</c> event noting server shutdown recovery.
/// </remarks>
public sealed class MatchRecoveryService(
	IMatchRepository persistence,
	ILogger<MatchRecoveryService> logger)
{
	/// <summary>Marks every unrecovered match and round from a previous shutdown as ended.</summary>
	/// <param name="cancellationToken">A token that cancels the recovery writes.</param>
	public async Task RecoverAsync(CancellationToken cancellationToken = default)
	{
		var openMatches = await persistence.FetchUnrecoveredMatchesAsync(cancellationToken);
		if (openMatches.Count > 0)
			logger.LogInformation("Recovering and closing {Count} orphaned match(es) from abnormal shutdown",
				openMatches.Count);

		foreach (var match in openMatches)
		{
			var openRounds = await persistence.FetchUnrecoveredRoundsAsync(match.Id, cancellationToken);
			foreach (var round in openRounds)
			{
				await persistence.SetRoundEndedAsync(round.Id, DateTimeOffset.UtcNow.UtcDateTime, true,
					cancellationToken);
				logger.LogInformation("Recovered and aborted orphaned round: MatchId={MatchId} RoundId={RoundId}",
					match.Id, round.Id);
			}

			await persistence.SetMatchEndedAsync(match.Id, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);
			logger.LogInformation("Recovered and closed orphaned match: MatchId={MatchId}", match.Id);

			await persistence.CreateEventAsync(new MatchEvent(
				match.Id, (int)MatchEventType.Closed,
				null, null, null, null,
				DateTimeOffset.UtcNow.UtcDateTime, "Server shutdown recovery"), cancellationToken);
		}
	}
}