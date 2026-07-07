using Bancho.Domain;

namespace Bancho.Application.Sessions;

/// <summary>
/// Ported from the `MatchTeams | Player` union Python uses as a dict/list key in
/// Match.match_points/winners — a scrim point's winner is either a team (team-vs matches) or an
/// individual player (FFA/co-op matches), never both. Exactly one of <see cref="Team"/>/
/// <see cref="PlayerId"/> is set.
/// </summary>
public readonly record struct ScrimParticipant
{
    public MatchTeams? Team { get; }
    public int? PlayerId { get; }

    private ScrimParticipant(MatchTeams? team, int? playerId)
    {
        Team = team;
        PlayerId = playerId;
    }

    public static ScrimParticipant OfTeam(MatchTeams team) => new(team, null);

    public static ScrimParticipant OfPlayer(int playerId) => new(null, playerId);
}
