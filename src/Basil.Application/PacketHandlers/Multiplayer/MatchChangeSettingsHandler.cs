using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>
///     Ported from app/api/domains/cho.py's MatchChangeSettings. The `is_scrimming` branch (send a
///     bot message instead of changing team type) is skipped — scrim state isn't ported to
///     MatchSession in this slice, so `IsScrimming` is always false and the else-branch always runs,
///     exactly matching the Python source's behavior for every match today.
/// </summary>
public sealed class MatchChangeSettingsHandler(
    IMapRepository mapRepository,
    IPlayerSessionRegistry sessionRegistry,
    MatchMembershipService matchMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.MatchChangeSettings;

    public bool AllowedWhenRestricted => false;

    public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchData = reader.ReadMatch();

        var match = player.Match;
        if (!MatchMembershipService.ValidateMatchData(matchData, player.Id) || match is null ||
            player.Id != match.HostId) return;

        await match.Lock.WaitAsync();
        try
        {
            var freemods = matchData.FreeMods;
            if (freemods != match.Freemods)
            {
                match.Freemods = freemods;
                if (freemods)
                {
                    foreach (var slot in match.Slots)
                        if (slot.PlayerId is not null)
                            slot.Mods = match.Mods & ~ModsExtensions.SpeedChangingMods;

                    match.Mods &= ModsExtensions.SpeedChangingMods;
                }
                else
                {
                    var hostSlot = match.GetHostSlot();
                    match.Mods &= ModsExtensions.SpeedChangingMods;
                    if (hostSlot is not null) match.Mods |= hostSlot.Mods;

                    foreach (var slot in match.Slots)
                        if (slot.PlayerId is not null)
                            slot.Mods = Mods.NoMod;
                }
            }

            if (matchData.MapId == -1)
            {
                match.UnreadyPlayers();
                match.PrevMapId = match.MapId;
                match.MapId = -1;
                match.MapMd5 = "";
                match.MapName = "";
                matchMembership.CancelQueuedAutoStart(match);
            }
            else if (match.MapId == -1)
            {
                var bmap = await mapRepository.FetchOneAsync(md5: matchData.MapMd5);
                if (bmap is not null)
                {
                    match.MapId = bmap.Id;
                    match.MapMd5 = bmap.Md5;
                    match.MapName = bmap.FullName;

                    var host = sessionRegistry.GetById(match.HostId);
                    if (host is not null) match.Mode = host.Status.Mode;
                    matchMembership.CancelQueuedAutoStart(match);
                }
                else
                {
                    // Diverges from bancho.py's cho.py, which blindly accepts the client-supplied
                    // id/md5/name here — that let a beatmap absent from this server's local DB get
                    // written into authoritative match state, corrupting round/match-report data.
                    var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
                    if (bot is not null)
                        matchMembership.EnqueueChat(match, bot.Name, bot.Id,
                            "Beatmap not found locally — map selection ignored.");
                }
            }

            var newTeamType = (MatchTeamType)matchData.TeamType;
            if (match.TeamType != newTeamType)
            {
                var newTeam = newTeamType is MatchTeamType.HeadToHead or MatchTeamType.TagCoop
                    ? MatchTeam.Neutral
                    : MatchTeam.Red;

                foreach (var slot in match.Slots)
                    if (slot.PlayerId is not null)
                        slot.Team = newTeam;

                match.TeamType = newTeamType;
                matchMembership.CancelQueuedAutoStart(match);
            }

            var newWinCondition = (MatchWinCondition)matchData.WinCondition;
            if (match.WinCondition != newWinCondition)
            {
                match.WinCondition = newWinCondition;
                matchMembership.CancelQueuedAutoStart(match);
            }

            match.Name = matchData.Name;
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}