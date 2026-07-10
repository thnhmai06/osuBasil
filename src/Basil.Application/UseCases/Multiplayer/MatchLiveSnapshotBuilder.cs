using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Multiplayer;

namespace Basil.Application.UseCases.Multiplayer;

/// <summary>
///     Builds the lightweight, in-memory-only payloads pushed over the live WS channels
///     (<see cref="IMatchEventBus" />) — deliberately cheaper than <see cref="MatchReportService" />'s
///     DB-backed <c>MatchReport</c>, since these fire on every state-changing packet (join, ready,
///     slot change, ...), not on demand. Consequently, the main channel carries live slot/map/state
///     only, not aggregated team scores or a round winner — those come from polling GET /multi/{id}.
/// </summary>
public static class MatchLiveSnapshotBuilder
{
    public static MatchLiveSnapshot BuildMain(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        var host = sessionRegistry.GetById(match.HostId);

        var referees = match.Referees
            .Select(id =>
            {
                var s = sessionRegistry.GetById(id);
                return new UserBrief(id, s?.Name);
            })
            .ToArray();

        var slots = match.Slots
            .Select((slot, index) =>
            {
                var s = slot.PlayerId is { } pid ? sessionRegistry.GetById(pid) : null;
                return new MatchLiveSlot(
                    index, slot.PlayerId, s?.Name, s?.Geoloc.CountryAcronym,
                    slot.Status.ToString(), slot.Team.ToString(), (int)slot.Mods);
            })
            .ToArray();

        return new MatchLiveSnapshot(
            match.DbId, match.Name, match.MapId, match.MapMd5, match.InProgress,
            match.WinCondition, match.TeamType, match.Mode, (int)match.Mods, match.Freemods,
            host is not null ? new UserBrief(host.Id, host.Name, host.Geoloc.CountryAcronym) : null,
            referees, slots);
    }

    public static PlayerLiveScore BuildPlayerScore(string playerName, ScoreFrameData frame)
    {
        return new PlayerLiveScore(
            playerName, frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
            frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp);
    }
}

/// <summary>Brief user info for embedded references (host, referees, slots).</summary>
public sealed record UserBrief(int? UserId, string? UserName, string? Country = null);

/// <summary>Payload for the WS /multi/{id} main channel.</summary>
public sealed record MatchLiveSnapshot(
    int MatchId,
    string Name,
    int CurrentMapId,
    string CurrentMapMd5,
    bool InProgress,
    MatchWinConditions WinCondition,
    MatchTeamTypes TeamType,
    GameMode Mode,
    int Mods,
    bool Freemods,
    UserBrief? Host,
    UserBrief[] Referees,
    MatchLiveSlot[] Slots);

public sealed record MatchLiveSlot(
    int SlotIndex,
    int? UserId,
    string? UserName,
    string? Country,
    string Status,
    string Team,
    int Mods);

/// <summary>Payload for the WS /multi/{id}/{playerName} channel.</summary>
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

/// <summary>Payload for the WS /multi/{id}/input channel.</summary>
public sealed record PlayerInputFrame(string PlayerName, string DataBase64);
