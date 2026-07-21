using Basil.Application.Abstractions.Multiplayer;

namespace Basil.Application.Services.Multiplayer;

public sealed class MatchRecoveryService(
    IMatchPersistenceRepository persistence)
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var openMatches = await persistence.FetchUnrecoveredMatchesAsync(cancellationToken);
        foreach (var match in openMatches)
        {
            var openRounds = await persistence.FetchUnrecoveredRoundsAsync(match.Id, cancellationToken);
            foreach (var round in openRounds)
            {
                await persistence.SetRoundEndedAsync(round.Id, DateTimeOffset.UtcNow.UtcDateTime, aborted: true,
                    cancellationToken);
            }

            await persistence.SetMatchEndedAsync(match.Id, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

            await persistence.CreateEventAsync(new MatchEventRow(
                match.Id, (int)MatchEventType.Closed,
                null, null, null, null,
                DateTimeOffset.UtcNow.UtcDateTime, "Server shutdown recovery"), cancellationToken);
        }
    }
}
