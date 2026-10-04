using Basil.Protocol.Bancho.Models.Auth;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

/// <summary>Builds the Bancho packets the server sends to the osu! client.</summary>
public sealed partial class PacketWriter(Stream stream)
{
	private readonly BinaryWriter _writer = new(stream);

	/// <summary>Writes a payload with the 7-byte Bancho packet header (id: u16, padding: u8, length: u32).</summary>
	/// <param name="packetType">The server packet id to place in the header.</param>
	/// <param name="writePayload">Writes the payload that follows the header.</param>
	private void Wrap(ServerPacketType packetType, Action writePayload)
	{
		var stream = _writer.BaseStream;

		_writer.Write((ushort)packetType);
		_writer.Write((byte)0); // padding
		_writer.Write(0); // length placeholder, patched below
		var payloadStart = stream.Position;

		writePayload();

		var payloadLength = checked((int)(stream.Position - payloadStart));
		var endPosition = stream.Position;
		stream.Position = payloadStart - sizeof(int);
		_writer.Write(payloadLength);
		stream.Position = endPosition;
	}

	// packet id: 5
	/// <summary>
	///     Builds the login reply packet carrying the user id, or a negative <see cref="LoginFailureReason" /> value on
	///     failure.
	/// </summary>
	/// <param name="userId">The id of the logged-in player, or the negative login failure reason.</param>
	/// <returns>The complete packet.</returns>
	public void LoginReply(int userId)
	{
		Wrap(ServerPacketType.UserId, () => _writer.Write(userId));
	}

	// packet id: 7
	/// <summary>Builds the send-message packet for a chat message.</summary>
	/// <param name="sender">The name of the sending player.</param>
	/// <param name="msg">The message body.</param>
	/// <param name="recipient">The name of the receiving player or channel.</param>
	/// <param name="senderId">The id of the sending player.</param>
	/// <returns>The complete packet.</returns>
	public void SendMessage(string sender, string msg, string recipient, int senderId)
	{
		Wrap(ServerPacketType.SendMessage,
			() => WriteMessagePayload(sender, msg, recipient, senderId));
	}

	// packet id: 8
	/// <summary>Builds the empty pong reply packet.</summary>
	/// <returns>The complete packet.</returns>
	public void Pong()
	{
		Wrap(ServerPacketType.Pong, () => { });
	}

	// packet id: 9 (deprecated)
	/// <summary>Builds the deprecated change-username packet, carrying the old and new names joined by "&gt;&gt;&gt;&gt;".</summary>
	/// <param name="oldName">The name before the change.</param>
	/// <param name="newName">The name after the change.</param>
	/// <returns>The complete packet.</returns>
	public void ChangeUsername(string oldName, string newName)
	{
		Wrap(ServerPacketType.HandleIrcChangeUsername, () => _writer.WriteOsuString($"{oldName}>>>>{newName}"));
	}

	// packet id: 11
	/// <summary>Builds the user stats packet carrying a player's status and statistics.</summary>
	/// <param name="userId">The id of the player.</param>
	/// <param name="action">The numeric action id of the player's current status.</param>
	/// <param name="infoText">The status text shown in the player's user card.</param>
	/// <param name="mapMd5">The md5 hash of the beatmap the player is playing, empty when idle.</param>
	/// <param name="mods">The bitwise combination of mods the player is using.</param>
	/// <param name="mode">The game mode of the player's current status.</param>
	/// <param name="mapId">The id of the beatmap the player is playing.</param>
	/// <param name="rankedScore">The player's ranked score.</param>
	/// <param name="accuracy">The player's accuracy as a percentage, divided by 100 on the wire.</param>
	/// <param name="plays">The number of plays the player has made.</param>
	/// <param name="totalScore">The player's total score.</param>
	/// <param name="globalRank">The player's global rank.</param>
	/// <param name="pp">
	///     The player's pp; a value above 0xFFFF is substituted into <paramref name="rankedScore" /> and sent as
	///     zero.
	/// </param>
	/// <returns>The complete packet.</returns>
	public void UserStats(
		int userId,
		int action,
		string infoText,
		string mapMd5,
		int mods,
		int mode,
		int mapId,
		long rankedScore,
		double accuracy,
		int plays,
		long totalScore,
		int globalRank,
		int pp)
	{
		if (pp > 0xFFFF)
		{
			// HACK: if pp is over osu!'s ingame cap, display it as ranked score instead.
			rankedScore = pp;
			pp = 0;
		}

		Wrap(ServerPacketType.UserStats, () =>
		{
			_writer.Write(userId);
			_writer.Write((byte)action);
			_writer.WriteOsuString(infoText);
			_writer.WriteOsuString(mapMd5);
			_writer.Write(mods);
			_writer.Write((byte)mode);
			_writer.Write(mapId);
			_writer.Write(rankedScore);
			_writer.Write((float)(accuracy / 100.0));
			_writer.Write(plays);
			_writer.Write(totalScore);
			_writer.Write(globalRank);
			_writer.Write((ushort)pp);
		});
	}

	// packet id: 12
	/// <summary>Builds the user logout packet for a player.</summary>
	/// <param name="userId">The id of the player who logged out.</param>
	/// <returns>The complete packet.</returns>
	public void Logout(int userId)
	{
		Wrap(ServerPacketType.UserLogout, () =>
		{
			_writer.Write(userId);
			_writer.Write((byte)0);
		});
	}

	// packet id: 13
	/// <summary>Builds the packet notifying a spectated player that a spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	/// <returns>The complete packet.</returns>
	public void SpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.SpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 14
	/// <summary>Builds the packet notifying a spectated player that a spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	/// <returns>The complete packet.</returns>
	public void SpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.SpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 15
	/// <summary>Builds the packet forwarding a raw replay frame bundle to spectators.</summary>
	/// <param name="rawData">The already-serialized frame bundle payload.</param>
	/// <returns>The complete packet.</returns>
	public void SpectateFrames(byte[] rawData)
	{
		Wrap(ServerPacketType.SpectateFrames, () => _writer.Write(rawData));
	}

	// packet id: 19
	/// <summary>Builds the empty version-update notification packet.</summary>
	/// <returns>The complete packet.</returns>
	public void VersionUpdate()
	{
		Wrap(ServerPacketType.VersionUpdate, () => { });
	}

	// packet id: 22
	/// <summary>Builds the packet notifying a spectated player that a spectator cannot spectate.</summary>
	/// <param name="userId">The id of the spectator who cannot spectate.</param>
	/// <returns>The complete packet.</returns>
	public void SpectatorCantSpectate(int userId)
	{
		Wrap(ServerPacketType.SpectatorCantSpectate, () => _writer.Write(userId));
	}

	// packet id: 23
	/// <summary>Builds the empty get-attention packet that prompts the client to focus the game window.</summary>
	/// <returns>The complete packet.</returns>
	public void GetAttention()
	{
		Wrap(ServerPacketType.GetAttention, () => { });
	}

	// packet id: 24
	/// <summary>Builds the notification packet showing a popup message in the client.</summary>
	/// <param name="msg">The message to display.</param>
	/// <returns>The complete packet.</returns>
	public void Notification(string msg)
	{
		Wrap(ServerPacketType.Notification, () => _writer.WriteOsuString(msg));
	}

	// packet id: 26
	/// <summary>Builds the update-room packet broadcasting the current room state to its players.</summary>
	/// <param name="room">The room data to send.</param>
	/// <param name="sendPassword">
	///     <see langword="true" /> to include the real password; otherwise, <see langword="false" /> to
	///     send a blank password.
	/// </param>
	/// <returns>The complete packet.</returns>
	public void UpdateMatch(RoomPacket room, bool sendPassword = true)
	{
		Wrap(ServerPacketType.UpdateMatch, () => WriteMatchPayload(room, sendPassword));
	}

	// packet id: 27
	/// <summary>Builds the new-room packet announcing a room to the lobby.</summary>
	/// <param name="room">The room data to send.</param>
	/// <returns>The complete packet.</returns>
	public void NewMatch(RoomPacket room)
	{
		Wrap(ServerPacketType.NewMatch, () => WriteMatchPayload(room, true));
	}

	// packet id: 28
	/// <summary>Builds the dispose-match packet removing a match from the lobby.</summary>
	/// <param name="matchId">The id of the match to dispose.</param>
	/// <returns>The complete packet.</returns>
	public void DisposeMatch(int matchId)
	{
		Wrap(ServerPacketType.DisposeMatch, () => _writer.Write(matchId));
	}

	// packet id: 34
	/// <summary>Builds the empty packet toggling the block-non-friend-DM preference.</summary>
	/// <returns>The complete packet.</returns>
	public void ToggleBlockNonFriendDm()
	{
		Wrap(ServerPacketType.ToggleBlockNonFriendDms, () => { });
	}

	// packet id: 36
	/// <summary>Builds the room-join-success packet confirming a join with the current room state.</summary>
	/// <param name="room">The room data to send.</param>
	/// <returns>The complete packet.</returns>
	public void MatchJoinSuccess(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchJoinSuccess, () => WriteMatchPayload(room, true));
	}

	// packet id: 37
	/// <summary>Builds the empty match-join-fail packet.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchJoinFail()
	{
		Wrap(ServerPacketType.MatchJoinFail, () => { });
	}

	// packet id: 42
	/// <summary>Builds the packet notifying a spectator that another spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	/// <returns>The complete packet.</returns>
	public void FellowSpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 43
	/// <summary>Builds the packet notifying a spectator that another spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	/// <returns>The complete packet.</returns>
	public void FellowSpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 46
	/// <summary>Builds the room-start packet beginning the room for all players.</summary>
	/// <param name="room">The room data to send.</param>
	/// <returns>The complete packet.</returns>
	public void MatchStart(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchStart, () => WriteMatchPayload(room, true));
	}

	// packet id: 48
	/// <summary>Builds the match-score-update packet broadcasting a player's score frame.</summary>
	/// <param name="frame">The score frame data to send.</param>
	/// <returns>The complete packet.</returns>
	public void MatchScoreUpdate(ScoreFrame frame)
	{
		Wrap(ServerPacketType.MatchScoreUpdate, () => WriteScoreFramePayload(frame));
	}

	// packet id: 50
	/// <summary>Builds the empty match-transfer-host packet notifying the new host of their role.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchTransferHost()
	{
		Wrap(ServerPacketType.MatchTransferHost, () => { });
	}

	// packet id: 53
	/// <summary>Builds the empty packet notifying match players that everyone has loaded.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchAllPlayersLoaded()
	{
		Wrap(ServerPacketType.MatchAllPlayersLoaded, () => { });
	}

	// packet id: 57
	/// <summary>Builds the match-player-failed packet reporting a player who failed.</summary>
	/// <param name="slotId">The slot id of the failed player.</param>
	/// <returns>The complete packet.</returns>
	public void MatchPlayerFailed(int slotId)
	{
		Wrap(ServerPacketType.MatchPlayerFailed, () => _writer.Write(slotId));
	}

	// packet id: 58
	/// <summary>Builds the empty match-complete packet signaling the end of a played map.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchComplete()
	{
		Wrap(ServerPacketType.MatchComplete, () => { });
	}

	// packet id: 61
	/// <summary>Builds the empty match-skip packet telling players that the intro skip is complete.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchSkip()
	{
		Wrap(ServerPacketType.MatchSkip, () => { });
	}

	// packet id: 64
	/// <summary>Builds the channel-join-success packet confirming that the client joined a channel.</summary>
	/// <param name="name">The name of the channel joined.</param>
	/// <returns>The complete packet.</returns>
	public void ChannelJoin(string name)
	{
		Wrap(ServerPacketType.ChannelJoinSuccess, () => _writer.WriteOsuString(name));
	}

	// packet id: 65
	/// <summary>Builds the channel-info packet describing a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <returns>The complete packet.</returns>
	public void ChannelInfo(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelInfo, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 66
	/// <summary>Builds the channel-kick packet removing the client from a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <returns>The complete packet.</returns>
	public void ChannelKick(string name)
	{
		Wrap(ServerPacketType.ChannelKick, () => _writer.WriteOsuString(name));
	}

	// packet id: 67
	/// <summary>Builds the channel-auto-join packet forcing the client into a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <returns>The complete packet.</returns>
	public void ChannelAutoJoin(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelAutoJoin, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 71
	/// <summary>Builds the privileges packet sending the client's privilege level.</summary>
	/// <param name="priv">The privilege bitmask of the client.</param>
	/// <returns>The complete packet.</returns>
	public void BanchoPrivileges(int priv)
	{
		Wrap(ServerPacketType.Privileges, () => _writer.Write(priv));
	}

	// packet id: 72
	/// <summary>Builds the friends-list packet sending the client's friend ids.</summary>
	/// <param name="friends">The ids of the client's friends.</param>
	/// <returns>The complete packet.</returns>
	public void FriendsList(IReadOnlyList<int> friends)
	{
		Wrap(ServerPacketType.FriendsList, () => _writer.WriteI32ListI16L(friends));
	}

	// packet id: 75
	/// <summary>Builds the protocol-version packet sending the negotiated protocol version.</summary>
	/// <param name="version">The protocol version number.</param>
	/// <returns>The complete packet.</returns>
	public void ProtocolVersion(int version)
	{
		Wrap(ServerPacketType.ProtocolVersion, () => _writer.Write(version));
	}

	// packet id: 76
	/// <summary>Builds the main-menu-icon packet setting the menu icon and the url it opens.</summary>
	/// <param name="iconUrl">The url of the icon image.</param>
	/// <param name="onclickUrl">The url opened when the icon is clicked.</param>
	/// <returns>The complete packet.</returns>
	public void MainMenuIcon(string iconUrl, string onclickUrl)
	{
		Wrap(ServerPacketType.MainMenuIcon, () => _writer.WriteOsuString($"{iconUrl}|{onclickUrl}"));
	}

	// packet id: 80 (deprecated)
	/// <summary>Builds the deprecated empty monitor packet.</summary>
	/// <returns>The complete packet.</returns>
	public void Monitor()
	{
		Wrap(ServerPacketType.Monitor, () => { });
	}

	// packet id: 81
	/// <summary>Builds the match-player-skipped packet reporting a player who skipped the intro.</summary>
	/// <param name="userId">The id of the player who skipped.</param>
	/// <returns>The complete packet.</returns>
	public void MatchPlayerSkipped(int userId)
	{
		Wrap(ServerPacketType.MatchPlayerSkipped, () => _writer.Write(userId));
	}

	// packet id: 83
	/// <summary>Builds the user-presence packet describing a player's online presence.</summary>
	/// <param name="userId">The id of the player.</param>
	/// <param name="name">The name of the player.</param>
	/// <param name="utcOffset">The player's UTC offset in hours, stored on the wire as the offset plus 24.</param>
	/// <param name="countryCode">The player's numeric country code.</param>
	/// <param name="banchoPrivileges">The player's privilege bitmask, combined with the game mode in the high bits.</param>
	/// <param name="mode">The game mode of the player's current status.</param>
	/// <param name="longitude">The player's longitude.</param>
	/// <param name="latitude">The player's latitude.</param>
	/// <param name="globalRank">The player's global rank.</param>
	/// <returns>The complete packet.</returns>
	public void UserPresence(
		int userId,
		string name,
		int utcOffset,
		int countryCode,
		int banchoPrivileges,
		int mode,
		double longitude,
		double latitude,
		int globalRank)
	{
		Wrap(ServerPacketType.UserPresence, () =>
		{
			_writer.Write(userId);
			_writer.WriteOsuString(name);
			_writer.Write((byte)(utcOffset + 24));
			_writer.Write((byte)countryCode);
			_writer.Write((byte)(banchoPrivileges | (mode << 5)));
			_writer.Write((float)longitude);
			_writer.Write((float)latitude);
			_writer.Write(globalRank);
		});
	}

	// packet id: 86
	/// <summary>Builds the restart packet telling the client to restart after a delay.</summary>
	/// <param name="ms">The delay in milliseconds before restarting.</param>
	/// <returns>The complete packet.</returns>
	public void RestartServer(int ms)
	{
		Wrap(ServerPacketType.Restart, () => _writer.Write(ms));
	}

	// packet id: 88
	/// <summary>Builds the match-invite packet sending an invitation message to a player.</summary>
	/// <param name="playerId">The id of the inviting player, used as the message sender id.</param>
	/// <param name="playerName">The name of the inviting player, used as the message sender.</param>
	/// <param name="matchEmbed">The match url embedded in the invite message.</param>
	/// <param name="targetName">The name of the player invited.</param>
	/// <returns>The complete packet.</returns>
	public void MatchInvite(int playerId, string playerName, string matchEmbed, string targetName)
	{
		var msg = $"Come join my game: {matchEmbed}.";
		Wrap(ServerPacketType.MatchInvite,
			() => WriteMessagePayload(playerName, msg, targetName, playerId));
	}

	// packet id: 89
	/// <summary>Builds the empty channel-info-end packet marking the end of a channel info batch.</summary>
	/// <returns>The complete packet.</returns>
	public void ChannelInfoEnd()
	{
		Wrap(ServerPacketType.ChannelInfoEnd, () => { });
	}

	// packet id: 91
	/// <summary>Builds the match-change-password packet confirming the new match password.</summary>
	/// <param name="newPassword">The new password of the match.</param>
	/// <returns>The complete packet.</returns>
	public void MatchChangePassword(string newPassword)
	{
		Wrap(ServerPacketType.MatchChangePassword, () => _writer.WriteOsuString(newPassword));
	}

	// packet id: 92
	/// <summary>Builds the silence-end packet reporting the remaining silence time.</summary>
	/// <param name="delta">The remaining silence duration.</param>
	/// <returns>The complete packet.</returns>
	public void SilenceEnd(int delta)
	{
		Wrap(ServerPacketType.SilenceEnd, () => _writer.Write(delta));
	}

	// packet id: 94
	/// <summary>Builds the user-silenced packet notifying a player that they were silenced.</summary>
	/// <param name="userId">The id of the silenced player.</param>
	/// <returns>The complete packet.</returns>
	public void UserSilenced(int userId)
	{
		Wrap(ServerPacketType.UserSilenced, () => _writer.Write(userId));
	}

	// packet id: 95 (unused, kept for parity)
	/// <summary>Builds the user-presence-single packet for one player, unused by this server and kept for parity.</summary>
	/// <param name="userId">The id of the player.</param>
	/// <returns>The complete packet.</returns>
	public void UserPresenceSingle(int userId)
	{
		Wrap(ServerPacketType.UserPresenceSingle, () => _writer.Write(userId));
	}

	// packet id: 96 (unused, kept for parity)
	/// <summary>Builds the user-presence-bundle packet for multiple players, unused by this server and kept for parity.</summary>
	/// <param name="userIds">The ids of the players.</param>
	/// <returns>The complete packet.</returns>
	public void UserPresenceBundle(IReadOnlyList<int> userIds)
	{
		Wrap(ServerPacketType.UserPresenceBundle, () => _writer.WriteI32ListI16L(userIds));
	}

	// packet id: 100
	/// <summary>Builds the user-dm-blocked packet notifying the client that a direct message was blocked.</summary>
	/// <param name="target">The name of the player whose message was blocked.</param>
	/// <returns>The complete packet.</returns>
	public void UserDmBlocked(string target)
	{
		Wrap(ServerPacketType.UserDmBlocked, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 101
	/// <summary>Builds the target-is-silenced packet notifying the client that the message target is silenced.</summary>
	/// <param name="target">The name of the silenced player.</param>
	/// <returns>The complete packet.</returns>
	public void TargetSilenced(string target)
	{
		Wrap(ServerPacketType.TargetIsSilenced, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 102
	/// <summary>Builds the empty forced-version-update packet.</summary>
	/// <returns>The complete packet.</returns>
	public void VersionUpdateForced()
	{
		Wrap(ServerPacketType.VersionUpdateForced, () => { });
	}

	// packet id: 103
	/// <summary>Builds the switch-server packet telling the client to switch servers after a delay.</summary>
	/// <param name="t">The delay in milliseconds before switching.</param>
	/// <returns>The complete packet.</returns>
	public void SwitchServer(int t)
	{
		Wrap(ServerPacketType.SwitchServer, () => _writer.Write(t));
	}

	// packet id: 104
	/// <summary>Builds the empty account-restricted packet notifying the client that the account is restricted.</summary>
	/// <returns>The complete packet.</returns>
	public void AccountRestricted()
	{
		Wrap(ServerPacketType.AccountRestricted, () => { });
	}

	// packet id: 105 (deprecated)
	/// <summary>Builds the deprecated Rich Text eXchange packet carrying a message to the client.</summary>
	/// <param name="msg">The message to send.</param>
	/// <returns>The complete packet.</returns>
	public void Rtx(string msg)
	{
		Wrap(ServerPacketType.Rtx, () => _writer.WriteOsuString(msg));
	}

	// packet id: 106
	/// <summary>Builds the empty match-abort packet notifying match players that the match was aborted.</summary>
	/// <returns>The complete packet.</returns>
	public void MatchAbort()
	{
		Wrap(ServerPacketType.MatchAbort, () => { });
	}

	// packet id: 107
	/// <summary>Builds the switch-tournament-server packet telling the client to connect to a tournament server.</summary>
	/// <param name="ip">The ip or hostname of the tournament server.</param>
	/// <returns>The complete packet.</returns>
	public void SwitchTournamentServer(string ip)
	{
		Wrap(ServerPacketType.SwitchTournamentServer, () => _writer.WriteOsuString(ip));
	}
}