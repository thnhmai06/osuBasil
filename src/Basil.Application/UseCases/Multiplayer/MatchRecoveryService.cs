using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Multiplayer;

namespace Basil.Application.UseCases.Multiplayer;

public sealed class MatchRecoveryService(
    IMatchPersistenceRepository persistence,
    IClock clock)
{
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var openMatches = await persistence.FetchUnrecoveredMatchesAsync(cancellationToken);
        foreach (var match in openMatches)
        {
            var openRounds = await persistence.FetchUnrecoveredRoundsAsync(match.Id, cancellationToken);
            foreach (var round in openRounds)
            {
                await persistence.SetRoundEndedAsync(round.Id, clock.UtcNow.UtcDateTime, aborted: true,
                    cancellationToken);
            }

            await persistence.SetMatchEndedAsync(match.Id, clock.UtcNow.UtcDateTime, cancellationToken);

            await persistence.CreateEventAsync(new MatchEventRow(
                match.Id, (int)MatchEventType.Closed,
                null, null, null, null,
                clock.UtcNow.UtcDateTime, "Server shutdown recovery"), cancellationToken);
        }
    }
}
