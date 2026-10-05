using Basil.Domain.Client;

namespace Basil.Domain.Users;

/// <summary>The actions an account may take, grouped by the osu! client privilege each group shows as.</summary>
/// <remarks>
///     Each group takes one byte and has a member named after it that holds every action of the group. Restrictions
///     suspend some granted permissions for a while; the rest are the account's effective permissions.
/// </remarks>
[Flags]
public enum Permissions : ulong
{
	/// <summary>No permission.</summary>
	None = 0,

	/// <summary>Create rooms, in game or for a tournament.</summary>
	PlayerCreateRoom = 1UL << 0,

	/// <summary>Join rooms and take a seat, including being seated by a referee.</summary>
	PlayerJoinRoom = 1UL << 1,

	/// <summary>Post in the general, room and spectator channels the account is a member of.</summary>
	PlayerChat = 1UL << 2,

	/// <summary>Send private messages.</summary>
	PlayerPrivateMessage = 1UL << 3,

	/// <summary>Spectate other players.</summary>
	PlayerSpectate = 1UL << 4,

	/// <summary>Every permission of the player group.</summary>
	Player = PlayerCreateRoom | PlayerJoinRoom | PlayerChat | PlayerPrivateMessage | PlayerSpectate,

	/// <summary>Search and download beatmapsets with osu!direct.</summary>
	SupporterDirect = 1UL << 8,

	/// <summary>Every permission of the supporter group.</summary>
	Supporter = SupporterDirect,

	/// <summary>Log in with osu!tourney and watch any open room, reading its channel.</summary>
	TournamentObserveRooms = 1UL << 16,

	/// <summary>Post in the channel of any room.</summary>
	TournamentPostInAnyRoom = 1UL << 17,

	/// <summary>See the private rooms and private match history of other users.</summary>
	TournamentViewPrivateMatches = 1UL << 18,

	/// <summary>Act with the authority of the creator in every room, and join any room without its password.</summary>
	TournamentManageAnyRoom = 1UL << 19,

	/// <summary>Open more tournament rooms than one creator may otherwise have open.</summary>
	TournamentUnlimitedRooms = 1UL << 20,

	/// <summary>Act for another online user in room and lobby operations, with that user's permissions.</summary>
	TournamentActForUsers = 1UL << 21,

	/// <summary>Import, replace, delete, hide and lock beatmapsets, and see hidden ones.</summary>
	TournamentManageBeatmaps = 1UL << 22,

	/// <summary>See hidden beatmapsets.</summary>
	TournamentViewHiddenBeatmaps = 1UL << 23,

	/// <summary>Every permission of the tournament group.</summary>
	Tournament = TournamentObserveRooms | TournamentPostInAnyRoom | TournamentViewPrivateMatches |
	             TournamentManageAnyRoom | TournamentUnlimitedRooms | TournamentActForUsers |
	             TournamentManageBeatmaps | TournamentViewHiddenBeatmaps,

	/// <summary>Silence other users for a while.</summary>
	ModeratorSilence = 1UL << 24,

	/// <summary>Suspend any permissions of other users, for a while or until lifted, and lift such suspensions.</summary>
	ModeratorRestrict = 1UL << 25,

	/// <summary>Send private messages past blocks and friends-only settings.</summary>
	ModeratorMessageAnyone = 1UL << 26,

	/// <summary>Show a notification to online users.</summary>
	ModeratorAnnounce = 1UL << 27,

	/// <summary>Every permission of the moderator group.</summary>
	Moderator = ModeratorSilence | ModeratorRestrict | ModeratorMessageAnyone | ModeratorAnnounce,

	/// <summary>Change the settings of the running server.</summary>
	DeveloperConfigureServer = 1UL << 32,

	/// <summary>Read the server's diagnostics.</summary>
	DeveloperViewDiagnostics = 1UL << 33,

	/// <summary>Every permission of the developer group.</summary>
	Developer = DeveloperConfigureServer | DeveloperViewDiagnostics,

	/// <summary>Create, edit and delete accounts, set other users' passwords and end their sessions.</summary>
	OwnerManageAccounts = 1UL << 40,

	/// <summary>Grant and remove permissions.</summary>
	OwnerManagePermissions = 1UL << 41,

	/// <summary>Edit the message of the day, the main menu, the FAQ, seasonal backgrounds and banners.</summary>
	OwnerManageContent = 1UL << 42,

	/// <summary>Every permission of the owner group.</summary>
	Owner = OwnerManageAccounts | OwnerManagePermissions | OwnerManageContent,

	/// <summary>Every permission.</summary>
	All = Player | Supporter | Tournament | Moderator | Developer | Owner,

	/// <summary>The permissions a silence suspends: chatting, private messages and joining rooms.</summary>
	SuspendedBySilence = PlayerJoinRoom | PlayerChat | PlayerPrivateMessage
}

/// <summary>Provides the rules for combining and reporting <see cref="Permissions" />.</summary>
public static class PermissionsExtensions
{
	/// <summary>Determines whether every permission of a requirement is held.</summary>
	/// <param name="held">The permissions held.</param>
	/// <param name="required">The permissions that must all be held; none is always satisfied.</param>
	/// <returns><see langword="true" /> if every permission in <paramref name="required" /> is held.</returns>
	public static bool Allows(this Permissions held, Permissions required)
	{
		return (held & required) == required;
	}

	/// <summary>Gets the permissions left in effect once the active restrictions suspend theirs.</summary>
	/// <param name="granted">The permissions granted to the account.</param>
	/// <param name="restrictions">The account's restrictions; only those active at <paramref name="now" /> count.</param>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>The granted permissions that no active restriction suspends.</returns>
	public static Permissions Effective(this Permissions granted, IEnumerable<Restriction> restrictions,
		DateTimeOffset now)
	{
		var suspended = restrictions
			.Where(restriction => restriction.Value.IsActive(now))
			.Aggregate(Permissions.None, (all, restriction) => all | restriction.Value.Permissions);
		return granted & ~suspended;
	}

	/// <summary>Gets the privileges an osu! client is told about for a set of effective permissions.</summary>
	/// <param name="effective">The effective permissions of the user.</param>
	/// <returns>The privileges of every group in which at least one permission is held.</returns>
	/// <remarks>Watching rooms with osu!tourney also turns on <see cref="ClientPrivileges.Supporter" />, which osu!tourney requires.</remarks>
	public static ClientPrivileges ToClientPrivileges(this Permissions effective)
	{
		var privileges = ClientPrivileges.None;
		if ((effective & Permissions.Player) != 0) privileges |= ClientPrivileges.Player;
		if ((effective & (Permissions.Supporter | Permissions.TournamentObserveRooms)) != 0)
			privileges |= ClientPrivileges.Supporter;
		if ((effective & Permissions.Tournament) != 0) privileges |= ClientPrivileges.Tournament;
		if ((effective & Permissions.Moderator) != 0) privileges |= ClientPrivileges.Moderator;
		if ((effective & Permissions.Developer) != 0) privileges |= ClientPrivileges.Developer;
		if ((effective & Permissions.Owner) != 0) privileges |= ClientPrivileges.Owner;
		return privileges;
	}
}
