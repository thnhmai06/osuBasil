using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;

/// <summary>
///     Builds the lightweight, in-memory-only payloads pushed over the live WS channels
///     (<see cref="IMatchEventBus" />) — deliberately cheaper than <see cref="MatchReportService" />'s
///     DB-backed <c>MatchReport</c>, since these fire on every state-changing packet (join, ready,
///     slot change, ...), not on demand. Consequently the main channel carries live slot/map/state
///     only, not aggregated team scores or a round winner — those come from polling GET /multi/{id}.
/// </summary>
public static class MatchLiveSnapshotBuilder
{
    public static MatchLiveSnapshot BuildMain(MatchSession match)
    {
        var slots = match.Slots
            .Select((slot, index) => new MatchLiveSlot(index, slot.PlayerId, slot.Status.ToString(),
                slot.Team.ToString(), (int)slot.Mods))
            .ToArray();

        return new MatchLiveSnapshot(
            match.DbId, match.Name, match.MapId, match.MapMd5, match.InProgress,
            match.WinCondition, match.TeamType, match.HostId, slots);
    }

    public static PlayerLiveScore BuildPlayerScore(string playerName, ScoreFrameData frame)
    {
        return new PlayerLiveScore(
            playerName, frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
            frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp);
    }
}

/// <summary>Payload for the WS /multi/{id} main channel — no per-player data, see class doc above.</summary>
public sealed record MatchLiveSnapshot(
    int MatchId,
    string Name,
    int CurrentMapId,
    string CurrentMapMd5,
    bool InProgress,
    MatchWinConditions WinCondition,
    MatchTeamTypes TeamType,
    int HostId,
    MatchLiveSlot[] Slots);

public sealed record MatchLiveSlot(int SlotIndex, int? UserId, string Status, string Team, int Mods);

/// <summary>Payload for the WS /multi/{id}/{playerName} channel — decoded from a MatchScoreUpdate frame.</summary>
public sealed record PlayerLiveScore(
    string PlayerName,
    int Time,
    int Num300,
    int Num100,
    int Num50,
    int NumGeki,
    int NumKatu,
    int NumMiss,
    int TotalScore,
    int MaxCombo,
    int CurrentCombo,
    bool Perfect,
    int CurrentHp);

/// <summary>Payload for the WS /multi/{id}/input channel — raw spectator frame bytes tagged by player.</summary>
public sealed record PlayerInputFrame(string PlayerName, string DataBase64);
