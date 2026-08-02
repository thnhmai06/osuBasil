namespace Basil.Protocol.Packets;

/// <summary>Identifies the packet types the server sends to the osu! client, keyed by their protocol ids.</summary>
public enum ServerPackets : byte
{
	/// <summary>Sends the user id after a successful login, or a negative value identifying the login failure reason.</summary>
	UserId = 5,

	/// <summary>Sends a chat message to a player or channel.</summary>
	SendMessage = 7,

	/// <summary>Replies to a client ping.</summary>
	Pong = 8,

	/// <summary>Notifies the client of a username change.</summary>
	HandleIrcChangeUsername = 9, // unused

	/// <summary>Notifies the client that a user has disconnected.</summary>
	HandleIrcQuit = 10,

	/// <summary>Sends a player's statistics and current status.</summary>
	UserStats = 11,

	/// <summary>Notifies the client that a user has logged out.</summary>
	UserLogout = 12,

	/// <summary>Notifies a spectated player that a spectator joined.</summary>
	SpectatorJoined = 13,

	/// <summary>Notifies a spectated player that a spectator left.</summary>
	SpectatorLeft = 14,

	/// <summary>Forwards a bundle of replay frames to spectators.</summary>
	SpectateFrames = 15,

	/// <summary>Notifies the client that a newer version is available.</summary>
	VersionUpdate = 19,

	/// <summary>Notifies a spectated player that a spectator cannot spectate.</summary>
	SpectatorCantSpectate = 22,

	/// <summary>Prompts the client to bring the game window to the foreground.</summary>
	GetAttention = 23,

	/// <summary>Shows a notification popup in the client.</summary>
	Notification = 24,

	/// <summary>Sends a match update to all players in the match.</summary>
	UpdateMatch = 26,

	/// <summary>Announces a newly created match to the lobby.</summary>
	NewMatch = 27,

	/// <summary>Notifies clients that a match has been disposed.</summary>
	DisposeMatch = 28,

	/// <summary>Toggles whether direct messages from non-friends are blocked.</summary>
	ToggleBlockNonFriendDms = 34,

	/// <summary>Confirms a successful match join and sends the current match state.</summary>
	MatchJoinSuccess = 36,

	/// <summary>Notifies a client that joining the match failed.</summary>
	MatchJoinFail = 37,

	/// <summary>Notifies a spectator that another spectator joined.</summary>
	FellowSpectatorJoined = 42,

	/// <summary>Notifies a spectator that another spectator left.</summary>
	FellowSpectatorLeft = 43,

	/// <summary>Notifies a client that all players have finished loading the map.</summary>
	AllPlayersLoaded = 45,

	/// <summary>Starts the match for all players.</summary>
	MatchStart = 46,

	/// <summary>Sends a player's score frame to everyone in the match.</summary>
	MatchScoreUpdate = 48,

	/// <summary>Notifies the new host of a match that they are now host.</summary>
	MatchTransferHost = 50,

	/// <summary>Notifies all match players that every player has loaded.</summary>
	MatchAllPlayersLoaded = 53,

	/// <summary>Notifies match players that a player has failed.</summary>
	MatchPlayerFailed = 57,

	/// <summary>Notifies match players that the map has been completed.</summary>
	MatchComplete = 58,

	/// <summary>Notifies match players that all players have skipped the intro.</summary>
	MatchSkip = 61,

	/// <summary>Notifies the client that their connection is unauthorized.</summary>
	Unauthorized = 62, // unused

	/// <summary>Confirms that the client joined a chat channel.</summary>
	ChannelJoinSuccess = 64,

	/// <summary>Sends information about a chat channel.</summary>
	ChannelInfo = 65,

	/// <summary>Removes the client from a chat channel.</summary>
	ChannelKick = 66,

	/// <summary>Automatically joins the client to a chat channel.</summary>
	ChannelAutoJoin = 67,

	/// <summary>Replies to a beatmap info request with beatmap details.</summary>
	BeatmapInfoReply = 69,

	/// <summary>Sends the client's privileges.</summary>
	Privileges = 71,

	/// <summary>Sends the client's friend list.</summary>
	FriendsList = 72,

	/// <summary>Sends the protocol version to the client.</summary>
	ProtocolVersion = 75,

	/// <summary>Sets the main menu icon and the url it opens.</summary>
	MainMenuIcon = 76,

	/// <summary>Instructs the client to monitor the server.</summary>
	Monitor = 80, // unused

	/// <summary>Notifies the match host that a player skipped the intro.</summary>
	MatchPlayerSkipped = 81,

	/// <summary>Sends a player's presence information.</summary>
	UserPresence = 83,

	/// <summary>Instructs the client to restart after a delay.</summary>
	Restart = 86,

	/// <summary>Sends a match invite message to a player.</summary>
	MatchInvite = 88,

	/// <summary>Marks the end of a batch of channel info packets.</summary>
	ChannelInfoEnd = 89,

	/// <summary>Confirms a match password change to the client.</summary>
	MatchChangePassword = 91,

	/// <summary>Notifies the client of the remaining silence time.</summary>
	SilenceEnd = 92,

	/// <summary>Notifies a user that they have been silenced.</summary>
	UserSilenced = 94,

	/// <summary>Sends presence for a single user.</summary>
	UserPresenceSingle = 95,

	/// <summary>Sends presence for multiple users in one packet.</summary>
	UserPresenceBundle = 96,

	/// <summary>Notifies the client that a direct message was blocked.</summary>
	UserDmBlocked = 100,

	/// <summary>Notifies the client that the message target is silenced.</summary>
	TargetIsSilenced = 101,

	/// <summary>Notifies the client that a newer version is required.</summary>
	VersionUpdateForced = 102,

	/// <summary>Instructs the client to switch servers after a delay.</summary>
	SwitchServer = 103,

	/// <summary>Notifies the client that their account is restricted.</summary>
	AccountRestricted = 104,

	/// <summary>Sends a Rich Text eXchange message to the client.</summary>
	Rtx = 105, // unused

	/// <summary>Notifies match players that the match was aborted.</summary>
	MatchAbort = 106,

	/// <summary>Instructs the client to switch to a tournament server.</summary>
	SwitchTournamentServer = 107
}