namespace Basil.Protocol.Packets;

/// <summary>Identifies the packet types the osu! client sends to the server, keyed by their protocol ids.</summary>
public enum ClientPackets
{
	/// <summary>Updates the client's current action and status.</summary>
	ChangeAction = 0,

	/// <summary>Sends a message to a public channel.</summary>
	SendPublicMessage = 1,

	/// <summary>Logs the client out of the server.</summary>
	Logout = 2,

	/// <summary>Requests an updated user stats packet from the server.</summary>
	RequestStatusUpdate = 3,

	/// <summary>Keeps the connection alive.</summary>
	Ping = 4,

	/// <summary>Starts spectating a player.</summary>
	StartSpectating = 16,

	/// <summary>Stops spectating the current player.</summary>
	StopSpectating = 17,

	/// <summary>Sends a bundle of replay frames to spectators.</summary>
	SpectateFrames = 18,

	/// <summary>Reports a client crash or error to the server.</summary>
	ErrorReport = 20,

	/// <summary>Notifies spectators that the client cannot spectate.</summary>
	CantSpectate = 21,

	/// <summary>Sends a private message to another player.</summary>
	SendPrivateMessage = 25,

	/// <summary>Leaves the multiplayer lobby.</summary>
	PartLobby = 29,

	/// <summary>Joins the multiplayer lobby.</summary>
	JoinLobby = 30,

	/// <summary>Creates a new multiplayer match.</summary>
	CreateMatch = 31,

	/// <summary>Joins an existing multiplayer match.</summary>
	JoinMatch = 32,

	/// <summary>Leaves the current multiplayer match.</summary>
	PartMatch = 33,

	/// <summary>Moves the client to a different slot in the match.</summary>
	MatchChangeSlot = 38,

	/// <summary>Marks the client as ready in the match.</summary>
	MatchReady = 39,

	/// <summary>Locks the match slots.</summary>
	MatchLock = 40,

	/// <summary>Changes the settings of the match.</summary>
	MatchChangeSettings = 41,

	/// <summary>Starts the match, sent by the host.</summary>
	MatchStart = 44,

	/// <summary>Sends the client's current score frame during a match.</summary>
	MatchScoreUpdate = 47,

	/// <summary>Reports that the client has finished playing the map.</summary>
	MatchComplete = 49,

	/// <summary>Changes the mods of a slot, for the host or in free mod mode.</summary>
	MatchChangeMods = 51,

	/// <summary>Reports that the client has finished loading the map.</summary>
	MatchLoadComplete = 52,

	/// <summary>Reports that the client does not have the selected beatmap.</summary>
	MatchNoBeatmap = 54,

	/// <summary>Marks the client as not ready in the match.</summary>
	MatchNotReady = 55,

	/// <summary>Reports that the client has failed during the match.</summary>
	MatchFailed = 56,

	/// <summary>Reports that the client has the selected beatmap.</summary>
	MatchHasBeatmap = 59,

	/// <summary>Requests to skip the map intro.</summary>
	MatchSkipRequest = 60,

	/// <summary>Joins a chat channel.</summary>
	ChannelJoin = 63,

	/// <summary>Requests beatmap information.</summary>
	BeatmapInfoRequest = 68,

	/// <summary>Transfers the match host to another player.</summary>
	MatchTransferHost = 70,

	/// <summary>Adds a player to the friend list.</summary>
	FriendAdd = 73,

	/// <summary>Removes a player from the friend list.</summary>
	FriendRemove = 74,

	/// <summary>Changes the client's team in the match.</summary>
	MatchChangeTeam = 77,

	/// <summary>Leaves a chat channel.</summary>
	ChannelPart = 78,

	/// <summary>Toggles whether the client receives periodic user updates.</summary>
	ReceiveUpdates = 79,

	/// <summary>Sets the client's away message.</summary>
	SetAwayMessage = 82,

	/// <summary>Notifies the server that the client is connected via IRC only.</summary>
	IrcOnly = 84,

	/// <summary>Requests the user stats of a player.</summary>
	UserStatsRequest = 85,

	/// <summary>Invites a player to the match.</summary>
	MatchInvite = 87,

	/// <summary>Changes the match password.</summary>
	MatchChangePassword = 90,

	/// <summary>Requests tournament match information.</summary>
	TournamentMatchInfoRequest = 93,

	/// <summary>Requests the presence of a player.</summary>
	UserPresenceRequest = 97,

	/// <summary>Requests the presence of all players.</summary>
	UserPresenceRequestAll = 98,

	/// <summary>Toggles whether direct messages from non-friends are blocked.</summary>
	ToggleBlockNonFriendDms = 99,

	/// <summary>Joins the match channel of a tournament match.</summary>
	TournamentJoinMatchChannel = 108,

	/// <summary>Leaves the match channel of a tournament match.</summary>
	TournamentLeaveMatchChannel = 109
}