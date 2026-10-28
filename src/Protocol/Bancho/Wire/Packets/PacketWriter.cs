using Basil.Protocol.Bancho.Models.Auth;
using Basil.Protocol.Bancho.Models.Beatmaps;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Binary;

namespace Basil.Protocol.Bancho.Wire.Packets;

/// <summary>Writes the Bancho packets the server sends to the osu! client.</summary>
/// <remarks>
///     <para>
///         The stream must be seekable: every packet header carries the length of the payload that follows it, so the
///         writer returns to the header once the payload is written.
///     </para>
///     <para>The stream is owned by the caller and is never disposed by the writer.</para>
///     <para>The writer is not thread-safe; write one connection's packets from a single flow at a time.</para>
/// </remarks>
public sealed partial class PacketWriter
{
	/// <summary>The number of slots in a bancho match, which always has 16 slots.</summary>
	public const int SlotCount = 16;

	private readonly BinaryWriter _writer;

	/// <summary>Creates a writer that sends packets to <paramref name="stream" />.</summary>
	/// <param name="stream">The seekable stream the packets are written to.</param>
	/// <exception cref="ArgumentException"><paramref name="stream" /> is not seekable.</exception>
	public PacketWriter(Stream stream)
	{
		if (!stream.CanSeek)
			throw new ArgumentException("The stream must be seekable.", nameof(stream));

		_writer = new BinaryWriter(stream);
	}

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
	///     Writes the login reply packet to the stream, carrying the user id, or a negative
	///     <see cref="LoginFailureReason" /> value on failure.
	/// </summary>
	/// <param name="userId">The id of the logged-in player, or the negative login failure reason.</param>
	public void LoginReply(int userId)
	{
		Wrap(ServerPacketType.UserId, () => _writer.Write(userId));
	}

	// packet id: 7
	/// <summary>Writes the send-message packet to the stream for a chat message.</summary>
	/// <param name="sender">The name of the sending player.</param>
	/// <param name="msg">The message body.</param>
	/// <param name="recipient">The name of the receiving player or channel.</param>
	/// <param name="senderId">The id of the sending player.</param>
	public void SendMessage(string sender, string msg, string recipient, int senderId)
	{
		Wrap(ServerPacketType.SendMessage,
			() => WriteMessagePayload(sender, msg, recipient, senderId));
	}

	// packet id: 8
	/// <summary>Writes the empty pong reply packet to the stream.</summary>
	public void Pong()
	{
		Wrap(ServerPacketType.Pong, () => { });
	}

	// packet id: 9 (deprecated)
	/// <summary>
	///     Writes the deprecated change-username packet to the stream, carrying the old and new names joined by
	///     "&gt;&gt;&gt;&gt;".
	/// </summary>
	/// <param name="oldName">The name before the change.</param>
	/// <param name="newName">The name after the change.</param>
	public void ChangeUsername(string oldName, string newName)
	{
		Wrap(ServerPacketType.HandleIrcChangeUsername, () => _writer.WriteOsuString($"{oldName}>>>>{newName}"));
	}

	// packet id: 11
	/// <summary>Writes the user stats packet to the stream, carrying a player's status and statistics.</summary>
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
	/// <param name="pp">The player's pp. <paramref name="pp" /> must fit in 16 bits.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="action" />, <paramref name="mode" />, or <paramref name="pp" /> is outside the range of the
	///     field that carries it.
	/// </exception>
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
		Wrap(ServerPacketType.UserStats, () =>
		{
			_writer.Write(userId);
			_writer.Write(checked((byte)action));
			_writer.WriteOsuString(infoText);
			_writer.WriteOsuString(mapMd5);
			_writer.Write(mods);
			_writer.Write(checked((byte)mode));
			_writer.Write(mapId);
			_writer.Write(rankedScore);
			_writer.Write((float)(accuracy / 100.0));
			_writer.Write(plays);
			_writer.Write(totalScore);
			_writer.Write(globalRank);
			_writer.Write(checked((ushort)pp));
		});
	}

	// packet id: 12
	/// <summary>Writes the user logout packet to the stream for a player.</summary>
	/// <param name="userId">The id of the player who logged out.</param>
	public void Logout(int userId)
	{
		Wrap(ServerPacketType.UserLogout, () =>
		{
			_writer.Write(userId);
			_writer.Write((byte)0);
		});
	}

	// packet id: 13
	/// <summary>Writes the packet to the stream that notifies a spectated player that a spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	public void SpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.SpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 14
	/// <summary>Writes the packet to the stream that notifies a spectated player that a spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	public void SpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.SpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 15
	/// <summary>Writes the packet to the stream that forwards a raw replay frame bundle to spectators.</summary>
	/// <param name="rawData">The already-serialized frame bundle payload.</param>
	public void SpectateFrames(byte[] rawData)
	{
		Wrap(ServerPacketType.SpectateFrames, () => _writer.Write(rawData));
	}

	// packet id: 19
	/// <summary>Writes the empty version-update notification packet to the stream.</summary>
	public void VersionUpdate()
	{
		Wrap(ServerPacketType.VersionUpdate, () => { });
	}

	// packet id: 22
	/// <summary>
	///     Writes the packet to the stream that notifies a spectated player that a spectator cannot spectate.
	/// </summary>
	/// <param name="userId">The id of the spectator who cannot spectate.</param>
	public void SpectatorCantSpectate(int userId)
	{
		Wrap(ServerPacketType.SpectatorCantSpectate, () => _writer.Write(userId));
	}

	// packet id: 23
	/// <summary>
	///     Writes the empty get-attention packet to the stream, which prompts the client to focus the game window.
	/// </summary>
	public void GetAttention()
	{
		Wrap(ServerPacketType.GetAttention, () => { });
	}

	// packet id: 24
	/// <summary>Writes the notification packet to the stream, showing a popup message in the client.</summary>
	/// <param name="msg">The message to display.</param>
	public void Notification(string msg)
	{
		Wrap(ServerPacketType.Notification, () => _writer.WriteOsuString(msg));
	}

	// packet id: 26
	/// <summary>
	///     Writes the update-room packet to the stream, broadcasting the current room state to its players.
	/// </summary>
	/// <param name="room">The room data to send.</param>
	/// <param name="sendPassword">
	///     <see langword="true" /> to include the real password; otherwise, <see langword="false" /> to
	///     send a blank password.
	/// </param>
	/// <exception cref="ArgumentException">
	///     <paramref name="room" /> does not have exactly <see cref="SlotCount" /> slots.
	/// </exception>
	/// <exception cref="OverflowException">
	///     A value of <paramref name="room" /> is outside the range of the field that carries it.
	/// </exception>
	public void UpdateMatch(RoomPacket room, bool sendPassword = true)
	{
		Wrap(ServerPacketType.UpdateMatch, () => WriteMatchPayload(room, sendPassword));
	}

	// packet id: 27
	/// <summary>Writes the new-room packet to the stream, announcing a room to the lobby.</summary>
	/// <param name="room">The room data to send.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="room" /> does not have exactly <see cref="SlotCount" /> slots.
	/// </exception>
	/// <exception cref="OverflowException">
	///     A value of <paramref name="room" /> is outside the range of the field that carries it.
	/// </exception>
	public void NewMatch(RoomPacket room)
	{
		Wrap(ServerPacketType.NewMatch, () => WriteMatchPayload(room, true));
	}

	// packet id: 28
	/// <summary>Writes the dispose-match packet to the stream, removing a match from the lobby.</summary>
	/// <param name="matchId">The id of the match to dispose.</param>
	public void DisposeMatch(int matchId)
	{
		Wrap(ServerPacketType.DisposeMatch, () => _writer.Write(matchId));
	}

	// packet id: 34
	/// <summary>Writes the empty block-non-friend-DM toggle packet to the stream.</summary>
	public void ToggleBlockNonFriendDm()
	{
		Wrap(ServerPacketType.ToggleBlockNonFriendDms, () => { });
	}

	// packet id: 36
	/// <summary>
	///     Writes the room-join-success packet to the stream, confirming a join with the current room state.
	/// </summary>
	/// <param name="room">The room data to send.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="room" /> does not have exactly <see cref="SlotCount" /> slots.
	/// </exception>
	/// <exception cref="OverflowException">
	///     A value of <paramref name="room" /> is outside the range of the field that carries it.
	/// </exception>
	public void MatchJoinSuccess(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchJoinSuccess, () => WriteMatchPayload(room, true));
	}

	// packet id: 37
	/// <summary>Writes the empty match-join-fail packet to the stream.</summary>
	public void MatchJoinFail()
	{
		Wrap(ServerPacketType.MatchJoinFail, () => { });
	}

	// packet id: 42
	/// <summary>Writes the packet to the stream that notifies a spectator that another spectator joined.</summary>
	/// <param name="userId">The id of the joining spectator.</param>
	public void FellowSpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 43
	/// <summary>Writes the packet to the stream that notifies a spectator that another spectator left.</summary>
	/// <param name="userId">The id of the leaving spectator.</param>
	public void FellowSpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 46
	/// <summary>Writes the room-start packet to the stream, beginning the room for all players.</summary>
	/// <param name="room">The room data to send.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="room" /> does not have exactly <see cref="SlotCount" /> slots.
	/// </exception>
	/// <exception cref="OverflowException">
	///     A value of <paramref name="room" /> is outside the range of the field that carries it.
	/// </exception>
	public void MatchStart(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchStart, () => WriteMatchPayload(room, true));
	}

	// packet id: 48
	/// <summary>
	///     Writes the match-score-update packet to the stream, broadcasting a player's score frame.
	/// </summary>
	/// <param name="frame">The score frame data to send.</param>
	/// <exception cref="OverflowException">
	///     A value of <paramref name="frame" /> is outside the range of the field that carries it.
	/// </exception>
	public void MatchScoreUpdate(ScoreFrame frame)
	{
		Wrap(ServerPacketType.MatchScoreUpdate, () => WriteScoreFramePayload(frame));
	}

	// packet id: 50
	/// <summary>
	///     Writes the empty match-transfer-host packet to the stream, notifying the new host of their role.
	/// </summary>
	public void MatchTransferHost()
	{
		Wrap(ServerPacketType.MatchTransferHost, () => { });
	}

	// packet id: 53
	/// <summary>Writes the empty packet to the stream that notifies match players that everyone has loaded.</summary>
	public void MatchAllPlayersLoaded()
	{
		Wrap(ServerPacketType.MatchAllPlayersLoaded, () => { });
	}

	// packet id: 57
	/// <summary>Writes the match-player-failed packet to the stream, reporting a player who failed.</summary>
	/// <param name="slotId">The slot id of the failed player.</param>
	public void MatchPlayerFailed(int slotId)
	{
		Wrap(ServerPacketType.MatchPlayerFailed, () => _writer.Write(slotId));
	}

	// packet id: 58
	/// <summary>Writes the empty match-complete packet to the stream, signaling the end of a played map.</summary>
	public void MatchComplete()
	{
		Wrap(ServerPacketType.MatchComplete, () => { });
	}

	// packet id: 61
	/// <summary>
	///     Writes the empty match-skip packet to the stream, telling players that the intro skip is complete.
	/// </summary>
	public void MatchSkip()
	{
		Wrap(ServerPacketType.MatchSkip, () => { });
	}

	// packet id: 64
	/// <summary>
	///     Writes the channel-join-success packet to the stream, confirming that the client joined a channel.
	/// </summary>
	/// <param name="name">The name of the channel joined.</param>
	public void ChannelJoin(string name)
	{
		Wrap(ServerPacketType.ChannelJoinSuccess, () => _writer.WriteOsuString(name));
	}

	// packet id: 65
	/// <summary>Writes the channel-info packet to the stream, describing a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="playerCount" /> is negative or above <c>65535</c>.
	/// </exception>
	public void ChannelInfo(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelInfo, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 66
	/// <summary>Writes the channel-kick packet to the stream, removing the client from a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	public void ChannelKick(string name)
	{
		Wrap(ServerPacketType.ChannelKick, () => _writer.WriteOsuString(name));
	}

	// packet id: 67
	/// <summary>Writes the channel-auto-join packet to the stream, forcing the client into a channel.</summary>
	/// <param name="name">The name of the channel.</param>
	/// <param name="topic">The topic of the channel.</param>
	/// <param name="playerCount">The number of players in the channel.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="playerCount" /> is negative or above <c>65535</c>.
	/// </exception>
	public void ChannelAutoJoin(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelAutoJoin, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 71
	/// <summary>Writes the privileges packet to the stream, sending the client's privilege level.</summary>
	/// <param name="privileges">The privilege bitmask of the client.</param>
	public void Privileges(int privileges)
	{
		Wrap(ServerPacketType.Privileges, () => _writer.Write(privileges));
	}

	// packet id: 72
	/// <summary>Writes the friends-list packet to the stream, sending the client's friend ids.</summary>
	/// <param name="friends">The ids of the client's friends.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="friends" /> contains more than <c>65535</c> entries.
	/// </exception>
	public void FriendsList(IReadOnlyList<int> friends)
	{
		Wrap(ServerPacketType.FriendsList, () => _writer.WriteInt32List(friends));
	}

	// packet id: 75
	/// <summary>
	///     Writes the protocol-version packet to the stream, sending the negotiated protocol version.
	/// </summary>
	/// <param name="version">The protocol version number.</param>
	public void ProtocolVersion(int version)
	{
		Wrap(ServerPacketType.ProtocolVersion, () => _writer.Write(version));
	}

	// packet id: 76
	/// <summary>Writes the main-menu-icon packet to the stream, setting the menu icon and the url it opens.</summary>
	/// <param name="iconUrl">The url of the icon image.</param>
	/// <param name="onclickUrl">The url opened when the icon is clicked.</param>
	public void MainMenuIcon(string iconUrl, string onclickUrl)
	{
		Wrap(ServerPacketType.MainMenuIcon, () => _writer.WriteOsuString($"{iconUrl}|{onclickUrl}"));
	}

	// packet id: 80 (deprecated)
	/// <summary>Writes the deprecated empty monitor packet to the stream.</summary>
	public void Monitor()
	{
		Wrap(ServerPacketType.Monitor, () => { });
	}

	// packet id: 81
	/// <summary>
	///     Writes the match-player-skipped packet to the stream, reporting a player who skipped the intro.
	/// </summary>
	/// <param name="userId">The id of the player who skipped.</param>
	public void MatchPlayerSkipped(int userId)
	{
		Wrap(ServerPacketType.MatchPlayerSkipped, () => _writer.Write(userId));
	}

	// packet id: 83
	/// <summary>Writes the user-presence packet to the stream, describing a player's online presence.</summary>
	/// <param name="userId">The id of the player.</param>
	/// <param name="name">The name of the player.</param>
	/// <param name="utcOffset">The player's UTC offset in hours, stored on the wire as the offset plus 24.</param>
	/// <param name="countryCode">The player's numeric country code.</param>
	/// <param name="banchoPrivileges">The player's privilege bitmask, combined with the game mode in the high bits.</param>
	/// <param name="mode">The game mode of the player's current status.</param>
	/// <param name="longitude">The player's longitude.</param>
	/// <param name="latitude">The player's latitude.</param>
	/// <param name="globalRank">The player's global rank.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="utcOffset" />, <paramref name="countryCode" />, or the privileges and mode combined are
	///     outside the range of the field that carries them.
	/// </exception>
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
			_writer.Write(checked((byte)(utcOffset + 24)));
			_writer.Write(checked((byte)countryCode));
			_writer.Write(checked((byte)(banchoPrivileges | (mode << 5))));
			_writer.Write((float)longitude);
			_writer.Write((float)latitude);
			_writer.Write(globalRank);
		});
	}

	// packet id: 86
	/// <summary>
	///     Writes the restart packet to the stream, telling the client to restart after a delay.
	/// </summary>
	/// <param name="ms">The delay in milliseconds before restarting.</param>
	public void RestartServer(int ms)
	{
		Wrap(ServerPacketType.Restart, () => _writer.Write(ms));
	}

	// packet id: 88
	/// <summary>
	///     Writes the match-invite packet to the stream, carrying an invitation message for a player.
	/// </summary>
	/// <param name="playerId">The id of the inviting player, used as the message sender id.</param>
	/// <param name="playerName">The name of the inviting player, used as the message sender.</param>
	/// <param name="message">The text of the invitation to send.</param>
	/// <param name="targetName">The name of the player invited.</param>
	public void MatchInvite(int playerId, string playerName, string message, string targetName)
	{
		Wrap(ServerPacketType.MatchInvite,
			() => WriteMessagePayload(playerName, message, targetName, playerId));
	}

	// packet id: 89
	/// <summary>
	///     Writes the empty channel-info-end packet to the stream, marking the end of a channel info batch.
	/// </summary>
	public void ChannelInfoEnd()
	{
		Wrap(ServerPacketType.ChannelInfoEnd, () => { });
	}

	// packet id: 91
	/// <summary>
	///     Writes the match-change-password packet to the stream, confirming the new match password.
	/// </summary>
	/// <param name="newPassword">The new password of the match.</param>
	public void MatchChangePassword(string newPassword)
	{
		Wrap(ServerPacketType.MatchChangePassword, () => _writer.WriteOsuString(newPassword));
	}

	// packet id: 92
	/// <summary>Writes the silence-end packet to the stream, reporting the remaining silence time.</summary>
	/// <param name="delta">The remaining silence duration.</param>
	public void SilenceEnd(int delta)
	{
		Wrap(ServerPacketType.SilenceEnd, () => _writer.Write(delta));
	}

	// packet id: 94
	/// <summary>Writes the user-silenced packet to the stream, notifying a player that they were silenced.</summary>
	/// <param name="userId">The id of the silenced player.</param>
	public void UserSilenced(int userId)
	{
		Wrap(ServerPacketType.UserSilenced, () => _writer.Write(userId));
	}

	// packet id: 95 (unused, kept for parity)
	/// <summary>
	///     Writes the user-presence-single packet to the stream for one player; unused by this server and kept for
	///     parity.
	/// </summary>
	/// <param name="userId">The id of the player.</param>
	public void UserPresenceSingle(int userId)
	{
		Wrap(ServerPacketType.UserPresenceSingle, () => _writer.Write(userId));
	}

	// packet id: 96 (unused, kept for parity)
	/// <summary>
	///     Writes the user-presence-bundle packet to the stream for multiple players; unused by this server and kept
	///     for parity.
	/// </summary>
	/// <param name="userIds">The ids of the players.</param>
	/// <exception cref="OverflowException">
	///     <paramref name="userIds" /> contains more than <c>65535</c> entries.
	/// </exception>
	public void UserPresenceBundle(IReadOnlyList<int> userIds)
	{
		Wrap(ServerPacketType.UserPresenceBundle, () => _writer.WriteInt32List(userIds));
	}

	// packet id: 100
	/// <summary>
	///     Writes the user-dm-blocked packet to the stream, notifying the client that a direct message was blocked.
	/// </summary>
	/// <param name="target">The name of the player whose message was blocked.</param>
	public void UserDmBlocked(string target)
	{
		Wrap(ServerPacketType.UserDmBlocked, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 101
	/// <summary>
	///     Writes the target-is-silenced packet to the stream, notifying the client that the message target is
	///     silenced.
	/// </summary>
	/// <param name="target">The name of the silenced player.</param>
	public void TargetSilenced(string target)
	{
		Wrap(ServerPacketType.TargetIsSilenced, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 102
	/// <summary>Writes the empty forced-version-update packet to the stream.</summary>
	public void VersionUpdateForced()
	{
		Wrap(ServerPacketType.VersionUpdateForced, () => { });
	}

	// packet id: 103
	/// <summary>
	///     Writes the switch-server packet to the stream, telling the client to switch servers after a delay.
	/// </summary>
	/// <param name="t">The delay in milliseconds before switching.</param>
	public void SwitchServer(int t)
	{
		Wrap(ServerPacketType.SwitchServer, () => _writer.Write(t));
	}

	// packet id: 104
	/// <summary>
	///     Writes the empty account-restricted packet to the stream, notifying the client that the account is
	///     restricted.
	/// </summary>
	public void AccountRestricted()
	{
		Wrap(ServerPacketType.AccountRestricted, () => { });
	}

	// packet id: 105 (deprecated)
	/// <summary>
	///     Writes the deprecated Rich Text eXchange packet to the stream, carrying a message to the client.
	/// </summary>
	/// <param name="msg">The message to send.</param>
	public void Rtx(string msg)
	{
		Wrap(ServerPacketType.Rtx, () => _writer.WriteOsuString(msg));
	}

	// packet id: 106
	/// <summary>
	///     Writes the empty match-abort packet to the stream, notifying match players that the match was aborted.
	/// </summary>
	public void MatchAbort()
	{
		Wrap(ServerPacketType.MatchAbort, () => { });
	}

	// packet id: 107
	/// <summary>
	///     Writes the switch-tournament-server packet to the stream, telling the client to connect to a tournament
	///     server.
	/// </summary>
	/// <param name="ip">The ip or hostname of the tournament server.</param>
	public void SwitchTournamentServer(string ip)
	{
		Wrap(ServerPacketType.SwitchTournamentServer, () => _writer.WriteOsuString(ip));
	}

	// packet id: 10
	/// <summary>Writes the IRC-quit packet to the stream, carrying the name of the player who quit.</summary>
	/// <param name="name">The name of the player who quit.</param>
	public void HandleIrcQuit(string name)
	{
		Wrap(ServerPacketType.HandleIrcQuit, () => _writer.WriteOsuString(name));
	}

	// packet id: 45
	/// <summary>Writes the empty all-players-loaded packet to the stream.</summary>
	public void AllPlayersLoaded()
	{
		Wrap(ServerPacketType.AllPlayersLoaded, () => { });
	}

	// packet id: 62 (unused, kept for parity)
	/// <summary>Writes the empty unauthorized packet to the stream; unused by this server and kept for parity.</summary>
	public void Unauthorized()
	{
		Wrap(ServerPacketType.Unauthorized, () => { });
	}

	// packet id: 69
	/// <summary>Writes the beatmap-info-reply packet to the stream, describing the beatmaps a client asked about.</summary>
	/// <param name="maps">The beatmaps to describe; the count is written as a 32-bit integer.</param>
	/// <exception cref="OverflowException">A value of <paramref name="maps" /> is outside the range of its field.</exception>
	public void BeatmapInfoReply(IReadOnlyList<BeatmapInfoReplyEntry> maps)
	{
		Wrap(ServerPacketType.BeatmapInfoReply, () =>
		{
			_writer.Write(maps.Count);
			foreach (var map in maps)
			{
				_writer.Write(map.Index);
				_writer.Write(map.MapId);
				_writer.Write(map.MapSetId);
				_writer.Write(map.ThreadId);
				_writer.Write(map.RankedStatus);
				_writer.Write(map.GradeOsu);
				_writer.Write(map.GradeCatch);
				_writer.Write(map.GradeTaiko);
				_writer.Write(map.GradeMania);
				_writer.WriteOsuString(map.Checksum);
			}
		});
	}

	/// <summary>Builds the bytes of the packets written by <paramref name="write" />.</summary>
	/// <param name="write">Writes one or more packets to the writer it is given.</param>
	/// <returns>The bytes of every packet written.</returns>
	public static byte[] Build(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}
}