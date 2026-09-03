using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the host's request to apply a new set of room settings.</summary>
/// <remarks>
///     Applies a full settings snapshot sent by the host client. Turning freemods on keeps only the
///     speed-changing mods on the match and strips them from every occupied slot; turning it off
///     restores the host slot's mods to the match and clears the mods of every other occupied slot. The
///     beatmap selection is handled through a clear-and-resolve handshake: a snapshot with MapId -1
///     clears the current selection (unready all players, remembering the previous map, and
///     cancelling a queued auto-start), while a match that currently has no map re-attempts resolution
///     of the snapshot's md5 against the local repository, applying the resolved beatmap and updating
///     the game mode from the host's current status, or warning once through a bot chat message when
///     the beatmap is not found locally. Changing the team type normalizes every occupied slot to
///     Neutral (for HeadToHead and TagCoop) or Red (for all other types), and any team-type or
///     win-condition change cancels a queued auto-start. The room name is adopted from the snapshot,
///     the chat channel's topic is synced to it, and the final state is broadcast. All mutations run
///     under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeSettingsHandler(
	IBeatmapRepository beatmapRepository,
	ISessionRegistry<GameSession> sessionRegistry,
	MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeSettings;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();

		var match = gameSession.Match;
		if (!MatchMembershipService.ValidateMatchData(matchData, gameSession.Id) || match is null ||
		    gameSession.Id != match.HostId) return;

		await match.Lock.WaitAsync(cancellationToken);
		long version;
		try
		{
			// Re-checked under the lock: host status can only change under this same lock, so a
			// sender who lost host while waiting for it must not still act with host authority.
			if (gameSession.Id != match.HostId) return;

			var freemods = matchData.FreeMods;
			if (freemods != match.Freemods)
			{
				match.Freemods = freemods;
				if (freemods)
				{
					foreach (var slot in match.Slots)
						if (slot.PlayerId is not null)
							slot.Mods = match.Mods & ~Mods.SpeedChangingMods;

					match.Mods &= Mods.SpeedChangingMods;
				}
				else
				{
					var hostSlot = match.GetHostSlot();
					match.Mods &= Mods.SpeedChangingMods;
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
				match.MapId = null;
				match.MapMd5 = "";
				match.MapName = MatchControlService.NoBeatmapSelectedName;
				match.UnresolvedMapMd5 = null;
				matchMembership.CancelQueuedAutoStart(match);
			}
			else if (match.MapId is null)
			{
				// Always re-attempt the lookup (not gated on UnresolvedMapMd5) so a beatmap ingested
				// later while the room sits idle resolves silently on the next settings packet. Only
				// the warning below is deduped, not the lookup itself.
				var beatmap = await beatmapRepository.FetchOneAsync(
					md5: matchData.MapMd5, cancellationToken: cancellationToken);
				if (beatmap is not null)
				{
					match.MapId = beatmap.Id;
					match.MapMd5 = beatmap.Md5;
					match.MapName = beatmap.FullName;
					match.UnresolvedMapMd5 = null;

					var host = sessionRegistry.GetByUserId(match.HostId);
					if (host is not null) match.Mode = host.Status.Mode;
					matchMembership.CancelQueuedAutoStart(match);
				}
				else if (matchData.MapMd5 != match.UnresolvedMapMd5)
				{
					// The client-supplied id/md5/name is never written into authoritative match state
					// here: a beatmap absent from this server's local DB would otherwise corrupt round
					// and match-report data. Osu! clients resend their full settings snapshot on any
					// room-setting change (freemod, team type, ...), not just a new map pick, so without
					// UnresolvedMapMd5 this warning would re-fire on every one of those instead of just
					// the first.
					match.UnresolvedMapMd5 = matchData.MapMd5;
					var bot = sessionRegistry.GetByUserId(BotBootstrapService.BotId);
					if (bot is not null)
						matchMembership.EnqueueChat(match, bot.Name, bot.Id,
							"Beatmap not found on the server — map selection ignored.");
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
			matchMembership.SyncChannelTopic(match);
			version = match.NextStateVersion();
		}
		finally
		{
			match.Lock.Release();
		}

		await matchMembership.EnqueueStateAsync(match, version, cancellationToken: cancellationToken);
	}
}