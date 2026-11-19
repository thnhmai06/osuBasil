using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Models.Beatmaps;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Wire.Binary;

namespace Basil.Protocol.Game.Stable.Wire.Packets;

public sealed partial class PacketWriter
{
	public const int SlotCount = 16;

	private readonly BinaryWriter _writer;

	public PacketWriter(Stream stream)
	{
		if (!stream.CanSeek)
			throw new ArgumentException("The stream must be seekable.", nameof(stream));

		_writer = new BinaryWriter(stream);
	}

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
	public void LoginReply(int userId)
	{
		Wrap(ServerPacketType.UserId, () => _writer.Write(userId));
	}

	// packet id: 7
	public void SendMessage(string sender, string msg, string recipient, int senderId)
	{
		Wrap(ServerPacketType.SendMessage,
			() => WriteMessagePayload(sender, msg, recipient, senderId));
	}

	// packet id: 8
	public void Pong()
	{
		Wrap(ServerPacketType.Pong, () => { });
	}

	// packet id: 9 (deprecated)
	public void ChangeUsername(string oldName, string newName)
	{
		Wrap(ServerPacketType.HandleIrcChangeUsername, () => _writer.WriteOsuString($"{oldName}>>>>{newName}"));
	}

	// packet id: 11
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
	public void Logout(int userId)
	{
		Wrap(ServerPacketType.UserLogout, () =>
		{
			_writer.Write(userId);
			_writer.Write((byte)0);
		});
	}

	// packet id: 13
	public void SpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.SpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 14
	public void SpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.SpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 15
	public void SpectateFrames(byte[] rawData)
	{
		Wrap(ServerPacketType.SpectateFrames, () => _writer.Write(rawData));
	}

	// packet id: 19
	public void VersionUpdate()
	{
		Wrap(ServerPacketType.VersionUpdate, () => { });
	}

	// packet id: 22
	public void SpectatorCantSpectate(int userId)
	{
		Wrap(ServerPacketType.SpectatorCantSpectate, () => _writer.Write(userId));
	}

	// packet id: 23
	public void GetAttention()
	{
		Wrap(ServerPacketType.GetAttention, () => { });
	}

	// packet id: 24
	public void Notification(string msg)
	{
		Wrap(ServerPacketType.Notification, () => _writer.WriteOsuString(msg));
	}

	// packet id: 26
	public void UpdateMatch(RoomPacket room, bool sendPassword = true)
	{
		Wrap(ServerPacketType.UpdateMatch, () => WriteMatchPayload(room, sendPassword));
	}

	// packet id: 27
	public void NewMatch(RoomPacket room, bool sendPassword = true)
	{
		Wrap(ServerPacketType.NewMatch, () => WriteMatchPayload(room, sendPassword));
	}

	// packet id: 28
	public void DisposeMatch(int matchId)
	{
		Wrap(ServerPacketType.DisposeMatch, () => _writer.Write(matchId));
	}

	// packet id: 34
	public void ToggleBlockNonFriendDm()
	{
		Wrap(ServerPacketType.ToggleBlockNonFriendDms, () => { });
	}

	// packet id: 36
	public void MatchJoinSuccess(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchJoinSuccess, () => WriteMatchPayload(room, true));
	}

	// packet id: 37
	public void MatchJoinFail()
	{
		Wrap(ServerPacketType.MatchJoinFail, () => { });
	}

	// packet id: 42
	public void FellowSpectatorJoined(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorJoined, () => _writer.Write(userId));
	}

	// packet id: 43
	public void FellowSpectatorLeft(int userId)
	{
		Wrap(ServerPacketType.FellowSpectatorLeft, () => _writer.Write(userId));
	}

	// packet id: 46
	public void MatchStart(RoomPacket room)
	{
		Wrap(ServerPacketType.MatchStart, () => WriteMatchPayload(room, true));
	}

	// packet id: 48
	public void MatchScoreUpdate(ScoreFrame frame)
	{
		Wrap(ServerPacketType.MatchScoreUpdate, () => WriteScoreFramePayload(frame));
	}

	// packet id: 50
	public void MatchTransferHost()
	{
		Wrap(ServerPacketType.MatchTransferHost, () => { });
	}

	// packet id: 53
	public void MatchAllPlayersLoaded()
	{
		Wrap(ServerPacketType.MatchAllPlayersLoaded, () => { });
	}

	// packet id: 57
	public void MatchPlayerFailed(int slotId)
	{
		Wrap(ServerPacketType.MatchPlayerFailed, () => _writer.Write(slotId));
	}

	// packet id: 58
	public void MatchComplete()
	{
		Wrap(ServerPacketType.MatchComplete, () => { });
	}

	// packet id: 61
	public void MatchSkip()
	{
		Wrap(ServerPacketType.MatchSkip, () => { });
	}

	// packet id: 64
	public void ChannelJoin(string name)
	{
		Wrap(ServerPacketType.ChannelJoinSuccess, () => _writer.WriteOsuString(name));
	}

	// packet id: 65
	public void ChannelInfo(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelInfo, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 66
	public void ChannelKick(string name)
	{
		Wrap(ServerPacketType.ChannelKick, () => _writer.WriteOsuString(name));
	}

	// packet id: 67
	public void ChannelAutoJoin(string name, string topic, int playerCount)
	{
		Wrap(ServerPacketType.ChannelAutoJoin, () => WriteChannelPayload(name, topic, playerCount));
	}

	// packet id: 71
	public void Privileges(int privileges)
	{
		Wrap(ServerPacketType.Privileges, () => _writer.Write(privileges));
	}

	// packet id: 72
	public void FriendsList(IReadOnlyList<int> friends)
	{
		Wrap(ServerPacketType.FriendsList, () => _writer.WriteInt32List(friends));
	}

	// packet id: 75
	public void ProtocolVersion(int version)
	{
		Wrap(ServerPacketType.ProtocolVersion, () => _writer.Write(version));
	}

	// packet id: 76
	public void MainMenuIcon(string iconUrl, string onclickUrl)
	{
		Wrap(ServerPacketType.MainMenuIcon, () => _writer.WriteOsuString($"{iconUrl}|{onclickUrl}"));
	}

	// packet id: 80 (deprecated)
	public void Monitor()
	{
		Wrap(ServerPacketType.Monitor, () => { });
	}

	// packet id: 81
	public void MatchPlayerSkipped(int userId)
	{
		Wrap(ServerPacketType.MatchPlayerSkipped, () => _writer.Write(userId));
	}

	// packet id: 83
	public void UserPresence(
		int userId,
		string name,
		int utcOffset,
		int countryCode,
		int stablePrivileges,
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
			_writer.Write(checked((byte)((stablePrivileges & 0x1F) | (mode << 5))));
			_writer.Write((float)longitude);
			_writer.Write((float)latitude);
			_writer.Write(globalRank);
		});
	}

	// packet id: 86
	public void RestartServer(int ms)
	{
		Wrap(ServerPacketType.Restart, () => _writer.Write(ms));
	}

	// packet id: 88
	public void MatchInvite(int playerId, string playerName, string message, string targetName)
	{
		Wrap(ServerPacketType.MatchInvite,
			() => WriteMessagePayload(playerName, message, targetName, playerId));
	}

	// packet id: 89
	public void ChannelInfoEnd()
	{
		Wrap(ServerPacketType.ChannelInfoEnd, () => { });
	}

	// packet id: 91
	public void MatchChangePassword(string newPassword)
	{
		Wrap(ServerPacketType.MatchChangePassword, () => _writer.WriteOsuString(newPassword));
	}

	// packet id: 92
	public void SilenceEnd(int delta)
	{
		Wrap(ServerPacketType.SilenceEnd, () => _writer.Write(delta));
	}

	// packet id: 94
	public void UserSilenced(int userId)
	{
		Wrap(ServerPacketType.UserSilenced, () => _writer.Write(userId));
	}

	// packet id: 95 (unused, kept for parity)
	public void UserPresenceSingle(int userId)
	{
		Wrap(ServerPacketType.UserPresenceSingle, () => _writer.Write(userId));
	}

	// packet id: 96 (unused, kept for parity)
	public void UserPresenceBundle(IReadOnlyList<int> userIds)
	{
		Wrap(ServerPacketType.UserPresenceBundle, () => _writer.WriteInt32List(userIds));
	}

	// packet id: 100
	public void UserDmBlocked(string target)
	{
		Wrap(ServerPacketType.UserDmBlocked, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 101
	public void TargetSilenced(string target)
	{
		Wrap(ServerPacketType.TargetIsSilenced, () => WriteMessagePayload("", "", target, 0));
	}

	// packet id: 102
	public void VersionUpdateForced()
	{
		Wrap(ServerPacketType.VersionUpdateForced, () => { });
	}

	// packet id: 103
	public void SwitchServer(int t)
	{
		Wrap(ServerPacketType.SwitchServer, () => _writer.Write(t));
	}

	// packet id: 104
	public void AccountRestricted()
	{
		Wrap(ServerPacketType.AccountRestricted, () => { });
	}

	// packet id: 105 (deprecated)
	public void Rtx(string msg)
	{
		Wrap(ServerPacketType.Rtx, () => _writer.WriteOsuString(msg));
	}

	// packet id: 106
	public void MatchAbort()
	{
		Wrap(ServerPacketType.MatchAbort, () => { });
	}

	// packet id: 107
	public void SwitchTournamentServer(string ip)
	{
		Wrap(ServerPacketType.SwitchTournamentServer, () => _writer.WriteOsuString(ip));
	}

	// packet id: 10
	public void HandleIrcQuit(string name)
	{
		Wrap(ServerPacketType.HandleIrcQuit, () => _writer.WriteOsuString(name));
	}

	// packet id: 45
	public void AllPlayersLoaded()
	{
		Wrap(ServerPacketType.AllPlayersLoaded, () => { });
	}

	// packet id: 62 (unused, kept for parity)
	public void Unauthorized()
	{
		Wrap(ServerPacketType.Unauthorized, () => { });
	}

	// packet id: 69
	public void BeatmapInfoReply(IReadOnlyList<BeatmapInfoResponse> maps)
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

	public static byte[] Build(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}
}