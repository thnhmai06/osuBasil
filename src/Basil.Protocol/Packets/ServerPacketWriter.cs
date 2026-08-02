using System.Buffers.Binary;
using Basil.Protocol.Multiplayer;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Protocol.Packets;

/// <summary>
///     Server-to-client Bancho packet builders. Every payload is assembled from
///     <see cref="PacketWriter" /> primitives, then wrapped with the 7-byte packet header via
///     <see cref="PacketWriter.Wrap" />.
/// </summary>
public static class ServerPacketWriter
{
	private static byte[] Concat(params ReadOnlySpan<byte[]> parts)
	{
		var length = 0;
		foreach (var part in parts) length += part.Length;

		var result = new byte[length];
		var offset = 0;
		foreach (var part in parts)
		{
			part.CopyTo(result.AsSpan(offset));
			offset += part.Length;
		}

		return result;
	}

	private static byte[] WriteMessagePayload(string sender, string text, string recipient, int senderId)
	{
		return Concat(
			BinaryWriter.WriteString(sender),
			BinaryWriter.WriteString(text),
			BinaryWriter.WriteString(recipient),
			BinaryWriter.WriteInt32(senderId));
	}

	private static byte[] WriteChannelPayload(string name, string topic, int playerCount)
	{
		var nameBytes = BinaryWriter.WriteString(name);
		var topicBytes = BinaryWriter.WriteString(topic);
		var result = new byte[nameBytes.Length + topicBytes.Length + 2];
		nameBytes.CopyTo(result, 0);
		topicBytes.CopyTo(result, nameBytes.Length);
		BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(nameBytes.Length + topicBytes.Length),
			(ushort)playerCount);
		return result;
	}

	/// <summary>Builds the payload of a match packet, used by UpdateMatch, NewMatch, MatchJoinSuccess, and MatchStart.</summary>
	/// <param name="match">The match data to serialize.</param>
	/// <param name="sendPassword">
	///     <see langword="true" /> to include the real password; otherwise, <see langword="false" /> to
	///     send a blank password.
	/// </param>
	/// <returns>The match payload bytes, without the packet header.</returns>
	public static byte[] WriteMatch(MatchPacket match, bool sendPassword)
	{
		var parts = new List<byte[]>();

		var header = new byte[8];
		BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)match.Id);
		header[2] = (byte)(match.InProgress ? 1 : 0);
		header[3] = 0; // match type, always 0
		BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)match.Mods);
		parts.Add(header);

		parts.Add(BinaryWriter.WriteString(match.Name));

		if (!string.IsNullOrEmpty(match.Password))
			parts.Add(sendPassword ? BinaryWriter.WriteString(match.Password) : [0x0B, 0x00]);
		else
			parts.Add([0x00]);

		parts.Add(BinaryWriter.WriteString(match.MapName));
		parts.Add(BinaryWriter.WriteInt32(match.MapId));
		parts.Add(BinaryWriter.WriteString(match.MapMd5));

		parts.Add([.. match.Slots.Select(s => (byte)s.Status)]);
		parts.Add([.. match.Slots.Select(s => (byte)s.Team)]);

		foreach (var slot in match.Slots)
			if (slot.HasPlayer)
				parts.Add(BinaryWriter.WriteUInt32((uint)slot.PlayerId!.Value));

		parts.Add(BinaryWriter.WriteUInt32((uint)match.HostId));
		parts.Add([(byte)match.Mode, (byte)match.WinCondition, (byte)match.TeamType, (byte)(match.FreeMods ? 1 : 0)]);

		if (match.FreeMods)
			foreach (var slot in match.Slots)
				parts.Add(BinaryWriter.WriteUInt32((uint)slot.Mods));

		parts.Add(BinaryWriter.WriteUInt32((uint)match.Seed));

		return Concat([.. parts]);
	}

	/// <summary>Builds the fixed 29-byte score frame payload, plus two doubles when score v2 is active.</summary>
	/// <param name="frame">The score frame data to serialize.</param>
	/// <returns>The score frame payload bytes, without the packet header.</returns>
	public static byte[] WriteScoreFrame(ScoreFrame frame)
	{
		var result = new byte[29];
		var span = result.AsSpan();

		BinaryPrimitives.WriteInt32LittleEndian(span, frame.Time);
		span[4] = (byte)frame.Id;
		BinaryPrimitives.WriteUInt16LittleEndian(span[5..], (ushort)frame.Num300);
		BinaryPrimitives.WriteUInt16LittleEndian(span[7..], (ushort)frame.Num100);
		BinaryPrimitives.WriteUInt16LittleEndian(span[9..], (ushort)frame.Num50);
		BinaryPrimitives.WriteUInt16LittleEndian(span[11..], (ushort)frame.NumGeki);
		BinaryPrimitives.WriteUInt16LittleEndian(span[13..], (ushort)frame.NumKatu);
		BinaryPrimitives.WriteUInt16LittleEndian(span[15..], (ushort)frame.NumMiss);
		BinaryPrimitives.WriteInt32LittleEndian(span[17..], frame.TotalScore);
		BinaryPrimitives.WriteUInt16LittleEndian(span[21..], (ushort)frame.MaxCombo);
		BinaryPrimitives.WriteUInt16LittleEndian(span[23..], (ushort)frame.CurrentCombo);
		span[25] = (byte)(frame.Perfect ? 1 : 0);
		span[26] = (byte)frame.CurrentHp;
		span[27] = (byte)frame.TagByte;
		span[28] = (byte)(frame.ScoreV2 ? 1 : 0);

		if (!frame.ScoreV2) return result;

		var comboPortion = new byte[8];
		var bonusPortion = new byte[8];
		BinaryPrimitives.WriteDoubleLittleEndian(comboPortion, frame.ComboPortion ?? 0.0);
		BinaryPrimitives.WriteDoubleLittleEndian(bonusPortion, frame.BonusPortion ?? 0.0);
		return Concat(result, comboPortion, bonusPortion);
	}

	// packet id: 5
	/// <summary>
	///     Builds the login reply packet carrying the user id, or a negative <see cref="LoginFailureReason" /> value on
	///     failure.
	/// </summary>
	/// <param name="userId">The id of the logged-in player, or the negative login failure reason.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] LoginReply(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.UserId, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 7
	/// <summary>Builds the send-message packet for a chat message.</summary>
	/// <param name="sender">The name of the sending player.</param>
	/// <param name="msg">The message body.</param>
	/// <param name="recipient">The name of the receiving player or channel.</param>
	/// <param name="senderId">The id of the sending player.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SendMessage(string sender, string msg, string recipient, int senderId)
	{
		return PacketWriter.Wrap(ServerPackets.SendMessage, WriteMessagePayload(sender, msg, recipient, senderId));
	}

	// packet id: 8
	/// <summary>Builds the empty pong reply packet.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] Pong()
	{
		return PacketWriter.Wrap(ServerPackets.Pong, []);
	}

	// packet id: 9 (deprecated)
	/// <summary>Builds the deprecated change-username packet, carrying the old and new names joined by "&gt;&gt;&gt;&gt;".</summary>
	/// <param name="oldName">The name before the change.</param>
	/// <param name="newName">The name after the change.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ChangeUsername(string oldName, string newName)
	{
		return PacketWriter.Wrap(ServerPackets.HandleIrcChangeUsername,
			BinaryWriter.WriteString($"{oldName}>>>>{newName}"));
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
	public static byte[] UserStats(
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

		var payload = Concat(
			BinaryWriter.WriteInt32(userId),
			[(byte)action],
			BinaryWriter.WriteString(infoText),
			BinaryWriter.WriteString(mapMd5),
			BinaryWriter.WriteInt32(mods),
			[(byte)mode],
			BinaryWriter.WriteInt32(mapId),
			WriteInt64(rankedScore),
			WriteFloat32((float)(accuracy / 100.0)),
			BinaryWriter.WriteInt32(plays),
			WriteInt64(totalScore),
			BinaryWriter.WriteInt32(globalRank),
			WriteUInt16((ushort)pp));

		return PacketWriter.Wrap(ServerPackets.UserStats, payload);
	}

	// packet id: 12
	/// <summary>Builds the user logout packet for a player.</summary>
	/// <param name="userId">The id of the player who logged out.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] Logout(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.UserLogout, Concat(BinaryWriter.WriteInt32(userId), [0]));
	}

	// packet id: 13
	/// <summary>Builds the packet notifying a spectated player that a spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SpectatorJoined(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.SpectatorJoined, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 14
	/// <summary>Builds the packet notifying a spectated player that a spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SpectatorLeft(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.SpectatorLeft, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 15
	/// <summary>Builds the packet forwarding a raw replay frame bundle to spectators.</summary>
	/// <param name="rawData">The already-serialized frame bundle payload.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SpectateFrames(byte[] rawData)
	{
		return PacketWriter.Wrap(ServerPackets.SpectateFrames, rawData);
	}

	// packet id: 19
	/// <summary>Builds the empty version-update notification packet.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] VersionUpdate()
	{
		return PacketWriter.Wrap(ServerPackets.VersionUpdate, []);
	}

	// packet id: 22
	/// <summary>Builds the packet notifying a spectated player that a spectator cannot spectate.</summary>
	/// <param name="userId">The id of the spectator who cannot spectate.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SpectatorCantSpectate(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.SpectatorCantSpectate, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 23
	/// <summary>Builds the empty get-attention packet that prompts the client to focus the game window.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] GetAttention()
	{
		return PacketWriter.Wrap(ServerPackets.GetAttention, []);
	}

	// packet id: 24
	/// <summary>Builds the notification packet showing a popup message in the client.</summary>
	/// <param name="msg">The message to display.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] Notification(string msg)
	{
		return PacketWriter.Wrap(ServerPackets.Notification, BinaryWriter.WriteString(msg));
	}

	// packet id: 26
	/// <summary>Builds the update-match packet broadcasting the current match state to its players.</summary>
	/// <param name="match">The match data to send.</param>
	/// <param name="sendPassword">
	///     <see langword="true" /> to include the real password; otherwise, <see langword="false" /> to
	///     send a blank password.
	/// </param>
	/// <returns>The complete packet.</returns>
	public static byte[] UpdateMatch(MatchPacket match, bool sendPassword = true)
	{
		return PacketWriter.Wrap(ServerPackets.UpdateMatch, WriteMatch(match, sendPassword));
	}

	// packet id: 27
	/// <summary>Builds the new-match packet announcing a match to the lobby.</summary>
	/// <param name="match">The match data to send.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] NewMatch(MatchPacket match)
	{
		return PacketWriter.Wrap(ServerPackets.NewMatch, WriteMatch(match, true));
	}

	// packet id: 28
	/// <summary>Builds the dispose-match packet removing a match from the lobby.</summary>
	/// <param name="matchId">The id of the match to dispose.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] DisposeMatch(int matchId)
	{
		return PacketWriter.Wrap(ServerPackets.DisposeMatch, BinaryWriter.WriteInt32(matchId));
	}

	// packet id: 34
	/// <summary>Builds the empty packet toggling the block-non-friend-DM preference.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] ToggleBlockNonFriendDm()
	{
		return PacketWriter.Wrap(ServerPackets.ToggleBlockNonFriendDms, []);
	}

	// packet id: 36
	/// <summary>Builds the match-join-success packet confirming a join with the current match state.</summary>
	/// <param name="match">The match data to send.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchJoinSuccess(MatchPacket match)
	{
		return PacketWriter.Wrap(ServerPackets.MatchJoinSuccess, WriteMatch(match, true));
	}

	// packet id: 37
	/// <summary>Builds the empty match-join-fail packet.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchJoinFail()
	{
		return PacketWriter.Wrap(ServerPackets.MatchJoinFail, []);
	}

	// packet id: 42
	/// <summary>Builds the packet notifying a spectator that another spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] FellowSpectatorJoined(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.FellowSpectatorJoined, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 43
	/// <summary>Builds the packet notifying a spectator that another spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] FellowSpectatorLeft(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.FellowSpectatorLeft, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 46
	/// <summary>Builds the match-start packet beginning the match for all players.</summary>
	/// <param name="match">The match data to send.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchStart(MatchPacket match)
	{
		return PacketWriter.Wrap(ServerPackets.MatchStart, WriteMatch(match, true));
	}

	// packet id: 48
	/// <summary>Builds the match-score-update packet broadcasting a player's score frame.</summary>
	/// <param name="frame">The score frame data to send.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchScoreUpdate(ScoreFrame frame)
	{
		return PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, WriteScoreFrame(frame));
	}

	// packet id: 50
	/// <summary>Builds the empty match-transfer-host packet notifying the new host of their role.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchTransferHost()
	{
		return PacketWriter.Wrap(ServerPackets.MatchTransferHost, []);
	}

	// packet id: 53
	/// <summary>Builds the empty packet notifying match players that everyone has loaded.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchAllPlayersLoaded()
	{
		return PacketWriter.Wrap(ServerPackets.MatchAllPlayersLoaded, []);
	}

	// packet id: 57
	/// <summary>Builds the match-player-failed packet reporting a player who failed.</summary>
	/// <param name="slotId">The slot id of the failed player.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchPlayerFailed(int slotId)
	{
		return PacketWriter.Wrap(ServerPackets.MatchPlayerFailed, BinaryWriter.WriteInt32(slotId));
	}

	// packet id: 58
	/// <summary>Builds the empty match-complete packet signaling the end of a played map.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchComplete()
	{
		return PacketWriter.Wrap(ServerPackets.MatchComplete, []);
	}

	// packet id: 61
	/// <summary>Builds the empty match-skip packet telling players that the intro skip is complete.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchSkip()
	{
		return PacketWriter.Wrap(ServerPackets.MatchSkip, []);
	}

	// packet id: 64
	/// <summary>Builds the channel-join-success packet confirming that the client joined a channel.</summary>
	/// <param name="name">The name of the channel joined.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ChannelJoin(string name)
	{
		return PacketWriter.Wrap(ServerPackets.ChannelJoinSuccess, BinaryWriter.WriteString(name));
	}

	// packet id: 65
	/// <summary>Builds the channel-info packet describing a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ChannelInfo(string name, string topic, int playerCount)
	{
		return PacketWriter.Wrap(ServerPackets.ChannelInfo, WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 66
	/// <summary>Builds the channel-kick packet removing the client from a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ChannelKick(string name)
	{
		return PacketWriter.Wrap(ServerPackets.ChannelKick, BinaryWriter.WriteString(name));
	}

	// packet id: 67
	/// <summary>Builds the channel-auto-join packet forcing the client into a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ChannelAutoJoin(string name, string topic, int playerCount)
	{
		return PacketWriter.Wrap(ServerPackets.ChannelAutoJoin, WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 71
	/// <summary>Builds the privileges packet sending the client's privilege level.</summary>
	/// <param name="priv">The privilege bitmask of the client.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] BanchoPrivileges(int priv)
	{
		return PacketWriter.Wrap(ServerPackets.Privileges, BinaryWriter.WriteInt32(priv));
	}

	// packet id: 72
	/// <summary>Builds the friends-list packet sending the client's friend ids.</summary>
	/// <param name="friends">The ids of the client's friends.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] FriendsList(IReadOnlyList<int> friends)
	{
		return PacketWriter.Wrap(ServerPackets.FriendsList, BinaryWriter.WriteI32List(friends));
	}

	// packet id: 75
	/// <summary>Builds the protocol-version packet sending the negotiated protocol version.</summary>
	/// <param name="version">The protocol version number.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] ProtocolVersion(int version)
	{
		return PacketWriter.Wrap(ServerPackets.ProtocolVersion, BinaryWriter.WriteInt32(version));
	}

	// packet id: 76
	/// <summary>Builds the main-menu-icon packet setting the menu icon and the url it opens.</summary>
	/// <param name="iconUrl">The url of the icon image.</param>
	/// <param name="onclickUrl">The url opened when the icon is clicked.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MainMenuIcon(string iconUrl, string onclickUrl)
	{
		return PacketWriter.Wrap(ServerPackets.MainMenuIcon, BinaryWriter.WriteString($"{iconUrl}|{onclickUrl}"));
	}

	// packet id: 80 (deprecated)
	/// <summary>Builds the deprecated empty monitor packet.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] Monitor()
	{
		return PacketWriter.Wrap(ServerPackets.Monitor, []);
	}

	// packet id: 81
	/// <summary>Builds the match-player-skipped packet reporting a player who skipped the intro.</summary>
	/// <param name="userId">The id of the player who skipped.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchPlayerSkipped(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.MatchPlayerSkipped, BinaryWriter.WriteInt32(userId));
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
	public static byte[] UserPresence(
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
		var payload = Concat(
			BinaryWriter.WriteInt32(userId),
			BinaryWriter.WriteString(name),
			[(byte)(utcOffset + 24)],
			[(byte)countryCode],
			[(byte)(banchoPrivileges | (mode << 5))],
			WriteFloat32((float)longitude),
			WriteFloat32((float)latitude),
			BinaryWriter.WriteInt32(globalRank));

		return PacketWriter.Wrap(ServerPackets.UserPresence, payload);
	}

	// packet id: 86
	/// <summary>Builds the restart packet telling the client to restart after a delay.</summary>
	/// <param name="ms">The delay in milliseconds before restarting.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] RestartServer(int ms)
	{
		return PacketWriter.Wrap(ServerPackets.Restart, BinaryWriter.WriteInt32(ms));
	}

	// packet id: 88
	/// <summary>Builds the match-invite packet sending an invitation message to a player.</summary>
	/// <param name="playerId">The id of the inviting player, used as the message sender id.</param>
	/// <param name="playerName">The name of the inviting player, used as the message sender.</param>
	/// <param name="matchEmbed">The match url embedded in the invite message.</param>
	/// <param name="targetName">The name of the player invited.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchInvite(int playerId, string playerName, string matchEmbed, string targetName)
	{
		var msg = $"Come join my game: {matchEmbed}.";
		return PacketWriter.Wrap(ServerPackets.MatchInvite, WriteMessagePayload(playerName, msg, targetName, playerId));
	}

	// packet id: 89
	/// <summary>Builds the empty channel-info-end packet marking the end of a channel info batch.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] ChannelInfoEnd()
	{
		return PacketWriter.Wrap(ServerPackets.ChannelInfoEnd, []);
	}

	// packet id: 91
	/// <summary>Builds the match-change-password packet confirming the new match password.</summary>
	/// <param name="newPassword">The new password of the match.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchChangePassword(string newPassword)
	{
		return PacketWriter.Wrap(ServerPackets.MatchChangePassword, BinaryWriter.WriteString(newPassword));
	}

	// packet id: 92
	/// <summary>Builds the silence-end packet reporting the remaining silence time.</summary>
	/// <param name="delta">The remaining silence duration.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SilenceEnd(int delta)
	{
		return PacketWriter.Wrap(ServerPackets.SilenceEnd, BinaryWriter.WriteInt32(delta));
	}

	// packet id: 94
	/// <summary>Builds the user-silenced packet notifying a player that they were silenced.</summary>
	/// <param name="userId">The id of the silenced player.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] UserSilenced(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.UserSilenced, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 95 (unused, kept for parity)
	/// <summary>Builds the user-presence-single packet for one player, unused by this server and kept for parity.</summary>
	/// <param name="userId">The id of the player.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] UserPresenceSingle(int userId)
	{
		return PacketWriter.Wrap(ServerPackets.UserPresenceSingle, BinaryWriter.WriteInt32(userId));
	}

	// packet id: 96 (unused, kept for parity)
	/// <summary>Builds the user-presence-bundle packet for multiple players, unused by this server and kept for parity.</summary>
	/// <param name="userIds">The ids of the players.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] UserPresenceBundle(IReadOnlyList<int> userIds)
	{
		return PacketWriter.Wrap(ServerPackets.UserPresenceBundle, BinaryWriter.WriteI32List(userIds));
	}

	// packet id: 100
	/// <summary>Builds the user-dm-blocked packet notifying the client that a direct message was blocked.</summary>
	/// <param name="target">The name of the player whose message was blocked.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] UserDmBlocked(string target)
	{
		return PacketWriter.Wrap(ServerPackets.UserDmBlocked, WriteMessagePayload("", "", target, 0));
	}

	// packet id: 101
	/// <summary>Builds the target-is-silenced packet notifying the client that the message target is silenced.</summary>
	/// <param name="target">The name of the silenced player.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] TargetSilenced(string target)
	{
		return PacketWriter.Wrap(ServerPackets.TargetIsSilenced, WriteMessagePayload("", "", target, 0));
	}

	// packet id: 102
	/// <summary>Builds the empty forced-version-update packet.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] VersionUpdateForced()
	{
		return PacketWriter.Wrap(ServerPackets.VersionUpdateForced, []);
	}

	// packet id: 103
	/// <summary>Builds the switch-server packet telling the client to switch servers after a delay.</summary>
	/// <param name="t">The delay in milliseconds before switching.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SwitchServer(int t)
	{
		return PacketWriter.Wrap(ServerPackets.SwitchServer, BinaryWriter.WriteInt32(t));
	}

	// packet id: 104
	/// <summary>Builds the empty account-restricted packet notifying the client that the account is restricted.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] AccountRestricted()
	{
		return PacketWriter.Wrap(ServerPackets.AccountRestricted, []);
	}

	// packet id: 105 (deprecated)
	/// <summary>Builds the deprecated Rich Text eXchange packet carrying a message to the client.</summary>
	/// <param name="msg">The message to send.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] Rtx(string msg)
	{
		return PacketWriter.Wrap(ServerPackets.Rtx, BinaryWriter.WriteString(msg));
	}

	// packet id: 106
	/// <summary>Builds the empty match-abort packet notifying match players that the match was aborted.</summary>
	/// <returns>The complete packet.</returns>
	public static byte[] MatchAbort()
	{
		return PacketWriter.Wrap(ServerPackets.MatchAbort, []);
	}

	// packet id: 107
	/// <summary>Builds the switch-tournament-server packet telling the client to connect to a tournament server.</summary>
	/// <param name="ip">The ip or hostname of the tournament server.</param>
	/// <returns>The complete packet.</returns>
	public static byte[] SwitchTournamentServer(string ip)
	{
		return PacketWriter.Wrap(ServerPackets.SwitchTournamentServer, BinaryWriter.WriteString(ip));
	}

	private static byte[] WriteInt64(long value)
	{
		var result = new byte[8];
		BinaryPrimitives.WriteInt64LittleEndian(result, value);
		return result;
	}

	private static byte[] WriteUInt16(ushort value)
	{
		var result = new byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian(result, value);
		return result;
	}

	private static byte[] WriteFloat32(float value)
	{
		var result = new byte[4];
		BinaryPrimitives.WriteSingleLittleEndian(result, value);
		return result;
	}
}