using Basil.Application.Beatmaps;
using Basil.Application.Bot;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Scores;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the host's request to apply a new set of room settings.</summary>
/// <remarks>
///     Applies a full settings snapshot sent by the host client. Turning freemods on keeps only the
///     speed-changing mods on the match and strips them from every occupied slot; turning it off
///     restores the host slot's mods to the match and clears the mods of every other occupied slot. The
///     beatmap selection is handled through a clear-and-resolve handshake: a snapshot with Beatmap -1
///     clears the current selection (unready all players and cancel a queued auto-start), while a
///     match that currently has no map re-attempts resolution of the snapshot's md5 against the local
///     repository on every settings packet, applying the resolved beatmap and updating the game mode
///     from the host's current status, or warning through a bot chat message each time the beatmap is
///     not found locally. Changing the team type normalizes every occupied slot to
///     Neutral (for HeadToHead and TagCoop) or Red (for all other types), and any team-type or
///     win-condition change cancels a queued auto-start. The room name is adopted from the snapshot,
///     the chat channel's topic is synced to it, and the final state is broadcast. All mutations run
///     under the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeSettingsHandler(
	IBeatmapRepository beatmapRepository,
	ISessionRegistry<GameSession> sessionRegistry,
	MatchMembership matchMembership,
	MatchLifecycle matchLifecycle,
	MatchBroadcast matchBroadcast) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeSettings;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();

		var match = gameSession.Match;
		if (!MatchCreationDataMapper.IsValid(matchData, gameSession.Id) || match is null ||
		    gameSession.Id != match.Host?.Id) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		// Re-checked under the lock: host status can only change under this same lock, so a
		// sender who lost host while waiting for it must not still act with host authority.
		if (gameSession.Id != match.Host?.Id) return;

		var freemods = matchData.FreeMods;
		if (freemods != match.Freemods)
		{
			match.Freemods = freemods;
			if (freemods)
			{
				foreach (var slot in match.Slots)
					if (slot.User is not null)
						slot.Mods = match.Mods & ~Mods.SpeedChangingMods;

				match.Mods &= Mods.SpeedChangingMods;
			}
			else
			{
				var hostSlot = match.GetHostSlot();
				match.Mods &= Mods.SpeedChangingMods;
				if (hostSlot is not null) match.Mods |= hostSlot.Mods;

				foreach (var slot in match.Slots)
					if (slot.User is not null)
						slot.Mods = Mods.NoMod;
			}
		}

		if (matchData.MapId == -1)
		{
			match.UnreadyPlayers();
			match.Beatmap = null;
			matchLifecycle.CancelQueuedAutoStart(match);
		}
		else if (match.MapId is null)
		{
			// Always re-attempted on every settings packet, not just the first, so a beatmap
			// ingested later while the room sits idle resolves silently on the next one.
			var beatmap = await beatmapRepository.FetchOneAsync(
				md5: matchData.MapMd5, cancellationToken: cancellationToken);
			if (beatmap is not null)
			{
				match.Beatmap = beatmap;

				// gameSession is the host, verified by the guard above.
				match.Mode = gameSession.Status.Mode;
				matchLifecycle.CancelQueuedAutoStart(match);
			}
			else
			{
				// The client-supplied id/md5/name is never written into authoritative match state
				// here: a beatmap absent from this server's local DB would otherwise corrupt round
				// and match-report data. Osu! clients resend their full settings snapshot on any
				// room-setting change (freemod, team type, ...), not just a new map pick, so this
				// warning re-fires on every one of those, not just the first.
				var bot = sessionRegistry.GetByUserId(BotBootstrapService.BotId);
				if (bot is not null)
					matchBroadcast.EnqueueChat(match, bot.Name, bot.Id,
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
				if (slot.User is not null)
					slot.Team = newTeam;

			match.TeamType = newTeamType;
			matchLifecycle.CancelQueuedAutoStart(match);
		}

		var newWinCondition = (MatchWinCondition)matchData.WinCondition;
		if (match.WinCondition != newWinCondition)
		{
			match.WinCondition = newWinCondition;
			matchLifecycle.CancelQueuedAutoStart(match);
		}

		match.Name = matchData.Name;
		matchMembership.SyncChannelTopic(match);
		mutation.PublishState();
	}
}