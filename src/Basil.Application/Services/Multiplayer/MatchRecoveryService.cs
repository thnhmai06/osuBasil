using Basil.Application.Abstractions.Multiplayer;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Multiplayer;

public sealed class MatchRecoveryService(
	IMatchPersistenceRepository persistence,
	ILogger<MatchRecoveryService> logger)
{
	public async Task RecoverAsync(CancellationToken cancellationToken = default)
	{
		var openMatches = await persistence.FetchUnrecoveredMatchesAsync(cancellationToken);
		if (openMatches.Count > 0)
			logger.LogInformation("Recovering {Count} orphaned match(es) from abnormal shutdown", openMatches.Count);

		foreach (var match in openMatches)
		{
			var openRounds = await persistence.FetchUnrecoveredRoundsAsync(match.Id, cancellationToken);
			foreach (var round in openRounds)
			{
				await persistence.SetRoundEndedAsync(round.Id, DateTimeOffset.UtcNow.UtcDateTime, true,
					cancellationToken);
				logger.LogInformation("Recovered orphaned round: MatchId={MatchId} RoundId={RoundId}",
					match.Id, round.Id);
			}

			await persistence.SetMatchEndedAsync(match.Id, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);
			logger.LogInformation("Recovered orphaned match: MatchId={MatchId}", match.Id);

			await persistence.CreateEventAsync(new MatchEventRow(
				match.Id, (int)MatchEventType.Closed,
				null, null, null, null,
				DateTimeOffset.UtcNow.UtcDateTime, "Server shutdown recovery"), cancellationToken);
		}
	}
}