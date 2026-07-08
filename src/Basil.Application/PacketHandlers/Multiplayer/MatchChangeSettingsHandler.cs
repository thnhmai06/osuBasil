using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
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
                    if (host is not null) match.Mode = (GameMode)host.Status.Mode.AsVanilla();
                }
                else
                {
                    match.MapId = matchData.MapId;
                    match.MapMd5 = matchData.MapMd5;
                    match.MapName = matchData.MapName;
                    match.Mode = (GameMode)matchData.Mode;
                }
            }

            var newTeamType = (MatchTeamTypes)matchData.TeamType;
            if (match.TeamType != newTeamType)
            {
                var newTeam = newTeamType is MatchTeamTypes.HeadToHead or MatchTeamTypes.TagCoop
                    ? MatchTeams.Neutral
                    : MatchTeams.Red;

                foreach (var slot in match.Slots)
                    if (slot.PlayerId is not null)
                        slot.Team = newTeam;

                match.TeamType = newTeamType;
            }

            var newWinCondition = (MatchWinConditions)matchData.WinCondition;
            if (match.WinCondition != newWinCondition) match.WinCondition = newWinCondition;

            match.Name = matchData.Name;
            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }
    }
}