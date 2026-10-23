using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Pins the osu! protocol packet ids of <see cref="ServerPacketType" /> and
///     <see cref="ClientPacketType" />, using the ids from the osu! protocol / bancho.py
///     <c>constants/packets.py</c> tables.
/// </summary>
public class PacketTypeIdsTests
{
	// Columns: enum member name, protocol id. Where bancho.py's ALL_CAPS name differs from the C#
	// member, the member name carries the same meaning (e.g. USER_LOGOUT -> UserLogout, RESTART -> Restart).
	[Theory]
	[InlineData("UserId", 5)]
	[InlineData("SendMessage", 7)]
	[InlineData("Pong", 8)]
	[InlineData("UserStats", 11)]
	[InlineData("UserLogout", 12)] // bancho.py: USER_LOGOUT
	[InlineData("SpectatorJoined", 13)]
	[InlineData("SpectatorLeft", 14)]
	[InlineData("SpectateFrames", 15)]
	[InlineData("VersionUpdate", 19)]
	[InlineData("SpectatorCantSpectate", 22)]
	[InlineData("GetAttention", 23)]
	[InlineData("Notification", 24)]
	[InlineData("UpdateMatch", 26)]
	[InlineData("NewMatch", 27)]
	[InlineData("DisposeMatch", 28)]
	[InlineData("ToggleBlockNonFriendDms", 34)]
	[InlineData("MatchJoinSuccess", 36)]
	[InlineData("MatchJoinFail", 37)]
	[InlineData("FellowSpectatorJoined", 42)]
	[InlineData("FellowSpectatorLeft", 43)]
	[InlineData("MatchStart", 46)]
	[InlineData("MatchScoreUpdate", 48)]
	[InlineData("MatchTransferHost", 50)]
	[InlineData("MatchAllPlayersLoaded", 53)]
	[InlineData("MatchPlayerFailed", 57)]
	[InlineData("MatchComplete", 58)]
	[InlineData("MatchSkip", 61)]
	[InlineData("ChannelJoinSuccess", 64)]
	[InlineData("ChannelInfo", 65)]
	[InlineData("ChannelKick", 66)]
	[InlineData("ChannelAutoJoin", 67)]
	[InlineData("Privileges", 71)]
	[InlineData("FriendsList", 72)]
	[InlineData("ProtocolVersion", 75)]
	[InlineData("MainMenuIcon", 76)]
	[InlineData("Monitor", 80)]
	[InlineData("MatchPlayerSkipped", 81)]
	[InlineData("UserPresence", 83)]
	[InlineData("Restart", 86)] // bancho.py: RESTART
	[InlineData("MatchInvite", 88)]
	[InlineData("ChannelInfoEnd", 89)]
	[InlineData("MatchChangePassword", 91)]
	[InlineData("SilenceEnd", 92)]
	[InlineData("UserSilenced", 94)]
	[InlineData("UserPresenceSingle", 95)]
	[InlineData("UserPresenceBundle", 96)]
	[InlineData("UserDmBlocked", 100)]
	[InlineData("TargetIsSilenced", 101)]
	[InlineData("VersionUpdateForced", 102)]
	public void ServerPacketType_MemberCarriesOsuProtocolId(string memberName, int protocolId)
	{
		Assert.Equal(protocolId, (int)Enum.Parse<ServerPacketType>(memberName));
	}

	[Theory]
	[InlineData("ChangeAction", 0)]
	[InlineData("SendPublicMessage", 1)]
	[InlineData("Logout", 2)]
	[InlineData("RequestStatusUpdate", 3)]
	[InlineData("Ping", 4)]
	[InlineData("StartSpectating", 16)]
	[InlineData("StopSpectating", 17)]
	[InlineData("SpectateFrames", 18)]
	[InlineData("ErrorReport", 20)]
	[InlineData("CantSpectate", 21)]
	[InlineData("SendPrivateMessage", 25)]
	[InlineData("PartLobby", 29)]
	[InlineData("JoinLobby", 30)]
	[InlineData("CreateMatch", 31)]
	[InlineData("JoinMatch", 32)]
	[InlineData("PartMatch", 33)]
	[InlineData("MatchChangeSlot", 38)]
	[InlineData("MatchReady", 39)]
	[InlineData("MatchLock", 40)]
	[InlineData("MatchChangeSettings", 41)]
	[InlineData("MatchStart", 44)]
	[InlineData("MatchScoreUpdate", 47)]
	[InlineData("MatchComplete", 49)]
	[InlineData("MatchChangeMods", 51)]
	[InlineData("MatchLoadComplete", 52)]
	[InlineData("MatchNoBeatmap", 54)]
	[InlineData("MatchNotReady", 55)]
	[InlineData("MatchFailed", 56)]
	[InlineData("MatchHasBeatmap", 59)]
	[InlineData("MatchSkipRequest", 60)]
	[InlineData("ChannelJoin", 63)]
	[InlineData("BeatmapInfoRequest", 68)]
	[InlineData("MatchTransferHost", 70)]
	[InlineData("FriendAdd", 73)]
	[InlineData("FriendRemove", 74)]
	[InlineData("MatchChangeTeam", 77)]
	[InlineData("ChannelPart", 78)]
	[InlineData("ReceiveUpdates", 79)]
	[InlineData("SetAwayMessage", 82)]
	[InlineData("IrcOnly", 84)]
	[InlineData("UserStatsRequest", 85)]
	[InlineData("MatchInvite", 87)]
	[InlineData("MatchChangePassword", 90)]
	[InlineData("TournamentMatchInfoRequest", 93)]
	[InlineData("UserPresenceRequest", 97)]
	[InlineData("UserPresenceRequestAll", 98)]
	[InlineData("ToggleBlockNonFriendDms", 99)]
	[InlineData("TournamentJoinMatchChannel", 108)]
	[InlineData("TournamentLeaveMatchChannel", 109)]
	public void ClientPacketType_MemberCarriesOsuProtocolId(string memberName, int protocolId)
	{
		Assert.Equal(protocolId, (int)Enum.Parse<ClientPacketType>(memberName));
	}
}