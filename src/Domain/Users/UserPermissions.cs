using System.Collections.Immutable;
using Basil.Domain.Moderation;

namespace Basil.Domain.Users;

/// <summary>The actions an account may take.</summary>
/// <remarks>
///     Each member names one action. A permission that includes smaller ones (a manage permission includes the
///     matching view permission) holds their bits too, so a check asks for the bit of the smallest permission that
///     allows the action. Restrictions suspend some granted permissions for a while; the rest are the account's
///     effective permissions. The mask of an area and the permission that manages it live in
///     <see cref="UserPermissionsGroups" />.
/// </remarks>
[Flags]
public enum UserPermissions : ulong
{
	/// <summary>No permission.</summary>
	None = 0,

	#region Rooms

	/// <summary>Create rooms, in game or for a tournament.</summary>
	RoomCreate = 1UL << 0,

	/// <summary>Join rooms and take a seat, including being seated by a referee.</summary>
	RoomJoin = 1UL << 1,

	/// <summary>Log in with osu!tourney and watch any open room, reading its channel; includes spectating players.</summary>
	RoomObserve = (1UL << 16) | SpectatingWatch,

	/// <summary>See the private rooms and private match history of other users.</summary>
	RoomViewPrivate = 1UL << 18,

	/// <summary>
	///     Act with the authority of the creator in every room, and join any room without its password; includes seeing
	///     private matches.
	/// </summary>
	RoomManageAny = (1UL << 19) | RoomViewPrivate,

	/// <summary>Open more rooms than one creator may otherwise have open.</summary>
	RoomCreateUnlimited = 1UL << 20,

	#endregion

	#region Chat

	/// <summary>Post in the general, room and spectator channels the account is a member of.</summary>
	ChatPost = 1UL << 2,

	/// <summary>Send private messages.</summary>
	ChatPostPrivate = 1UL << 3,

	/// <summary>Post in the channel of any room.</summary>
	ChatPostAnyRoom = 1UL << 17,

	/// <summary>Send private messages past blocks and friends-only settings.</summary>
	ChatMessageAnyone = 1UL << 26,

	/// <summary>Show a notification to online users.</summary>
	ChatAnnounce = 1UL << 27,

	#endregion

	#region Spectating

	/// <summary>Spectate other players.</summary>
	SpectatingWatch = 1UL << 4,

	#endregion

	#region Scores

	/// <summary>Submit scores after playing.</summary>
	ScoreSubmit = 1UL << 7,

	#endregion

	#region Beatmapsets

	/// <summary>Upload new beatmapsets while uploads are not locked.</summary>
	BeatmapsetUpload = 1UL << 5,

	/// <summary>Search and download beatmapsets with osu!direct.</summary>
	BeatmapsetDirect = 1UL << 8,

	/// <summary>See hidden beatmaps and beatmapsets.</summary>
	BeatmapsetViewHidden = 1UL << 23,

	/// <summary>Import, replace, delete, hide and lock beatmapsets; includes seeing hidden ones.</summary>
	BeatmapsetManage = (1UL << 22) | BeatmapsetViewHidden,

	#endregion

	#region Screenshots

	/// <summary>Upload screenshots from the game.</summary>
	ScreenshotUpload = 1UL << 6,

	/// <summary>Delete any stored screenshot.</summary>
	ScreenshotDelete = 1UL << 28,

	#endregion

	#region Users

	/// <summary>Add notes about users and read the moderation log.</summary>
	UserNote = 1UL << 30,

	/// <summary>Silence other users for a while; includes adding notes and reading the moderation log.</summary>
	UserSilence = (1UL << 24) | UserNote,

	/// <summary>View a user's login history and IP addresses.</summary>
	UserViewLogins = 1UL << 29,

	/// <summary>
	///     Suspend any permissions of other users, for a while or until lifted, and lift such suspensions; includes
	///     silencing, viewing login history and reading the moderation log.
	/// </summary>
	UserRestrict = (1UL << 25) | UserSilence | UserViewLogins,

	/// <summary>See deleted accounts.</summary>
	UserViewDeleted = 1UL << 43,

	/// <summary>
	///     Create, edit and delete accounts, set other users' passwords and end their sessions; includes seeing deleted
	///     accounts.
	/// </summary>
	UserManage = (1UL << 40) | UserViewDeleted,

	/// <summary>Grant and remove permissions.</summary>
	UserManagePermissions = 1UL << 41,

	#endregion

	#region Server

	/// <summary>Change the settings of the running server.</summary>
	ServerConfigure = 1UL << 32,

	/// <summary>Read the server's diagnostics.</summary>
	ServerViewDiagnostics = 1UL << 33,

	/// <summary>Edit the message of the day, the main menu, the FAQ, seasonal backgrounds and banners.</summary>
	ServerManageContent = 1UL << 42

	#endregion
}

/// <summary>Provides the rules for combining and reporting <see cref="UserPermissions" />.</summary>
public static class PermissionsExtensions
{
	/// <param name="held">The permissions held.</param>
	extension(UserPermissions held)
	{
		/// <summary>Gets the roles an osu! client is told the user has, from their effective permissions.</summary>
		/// <returns>The role of every role whose permissions the user holds at least one of.</returns>
		/// <remarks>
		///     Watching rooms with osu!tourney also turns on <see cref="UserPrivileges.Supporter" />, which osu!tourney
		///     requires.
		/// </remarks>
		public UserPrivileges Privileges
		{
			get
			{
				var privileges = UserPrivileges.None;
				if ((held & UserPrivileges.Player.Permissions) != 0) privileges |= UserPrivileges.Player;
				if ((held & UserPrivileges.Supporter.Permissions) != 0 || held.Allows(UserPermissions.RoomObserve))
					privileges |= UserPrivileges.Supporter;
				if ((held & UserPrivileges.Tournament.Permissions) != 0) privileges |= UserPrivileges.Tournament;
				if ((held & UserPrivileges.Moderator.Permissions) != 0) privileges |= UserPrivileges.Moderator;
				if ((held & UserPrivileges.Developer.Permissions) != 0) privileges |= UserPrivileges.Developer;
				if ((held & UserPrivileges.Owner.Permissions) != 0) privileges |= UserPrivileges.Owner;
				return privileges;
			}
		}

		/// <summary>Determines whether every permission of a requirement is held.</summary>
		/// <param name="required">The permissions that must all be held; none is always satisfied.</param>
		/// <returns><see langword="true" /> if every permission in <paramref name="required" /> is held.</returns>
		public bool Allows(UserPermissions required)
		{
			return (held & required) == required;
		}

		/// <summary>Gets the permissions left in effect once the active restrictions suspend theirs.</summary>
		/// <param name="restrictions">The account's restrictions; only those active at <paramref name="now" /> count.</param>
		/// <param name="now">The moment to evaluate at.</param>
		/// <returns>
		///     The granted permissions that no active restriction suspends; a user who manages an area keeps every
		///     permission of that area.
		/// </returns>
		public UserPermissions Effective(IEnumerable<UserRestriction> restrictions, DateTimeOffset now)
		{
			var suspended = restrictions
				.Where(restriction => restriction.Value.IsActive(now))
				.Aggregate(UserPermissions.None, (all, restriction) => all | restriction.Value.Permissions);
			var kept = UserPermissionsGroups.Areas
				.Where(area => area.Manager != UserPermissions.None && held.Allows(area.Manager))
				.Aggregate(UserPermissions.None, (current, area) => current | area.Permissions);
			return held & ~(suspended & ~kept);
		}
	}
}

/// <summary>The masks of permissions by area, and the permission that manages each area.</summary>
public static class UserPermissionsGroups
{
	/// <summary>Every permission the server defines.</summary>
	public const UserPermissions All =
		UserPermissions.RoomCreate | UserPermissions.RoomJoin | UserPermissions.RoomObserve |
		UserPermissions.RoomViewPrivate |
		UserPermissions.RoomManageAny | UserPermissions.RoomCreateUnlimited | UserPermissions.ChatPost |
		UserPermissions.ChatPostPrivate | UserPermissions.ChatPostAnyRoom | UserPermissions.ChatMessageAnyone |
		UserPermissions.ChatAnnounce | UserPermissions.SpectatingWatch | UserPermissions.ScoreSubmit |
		UserPermissions.BeatmapsetUpload | UserPermissions.BeatmapsetDirect | UserPermissions.BeatmapsetViewHidden |
		UserPermissions.BeatmapsetManage | UserPermissions.ScreenshotUpload | UserPermissions.ScreenshotDelete |
		UserPermissions.UserNote | UserPermissions.UserSilence | UserPermissions.UserViewLogins |
		UserPermissions.UserRestrict |
		UserPermissions.UserViewDeleted | UserPermissions.UserManage | UserPermissions.UserManagePermissions |
		UserPermissions.ServerConfigure | UserPermissions.ServerViewDiagnostics | UserPermissions.ServerManageContent;

	/// <summary>The permissions a new account holds.</summary>
	public static readonly UserPermissions Default =
		UserPrivileges.Player.Permissions | UserPrivileges.Supporter.Permissions;

	/// <summary>The chatting permissions and the permission that manages them.</summary>
	public static readonly ModerationArea Chat =
		new(UserPermissions.ChatPost | UserPermissions.ChatPostPrivate | UserPermissions.ChatPostAnyRoom,
			UserPermissions.ChatMessageAnyone);

	/// <summary>The multiplayer permissions and the permission that manages them.</summary>
	public static readonly ModerationArea Multiplayer =
		new(UserPermissions.RoomCreate | UserPermissions.RoomJoin | UserPermissions.RoomCreateUnlimited,
			UserPermissions.RoomManageAny);

	/// <summary>The spectating permissions and the permission that manages them.</summary>
	public static readonly ModerationArea
		Spectating = new(UserPermissions.SpectatingWatch, UserPermissions.RoomObserve);

	/// <summary>The scoring permissions and the permission that manages them.</summary>
	public static readonly ModerationArea Scores = new(UserPermissions.ScoreSubmit, UserPermissions.None);

	/// <summary>The beatmapset permissions and the permission that manages them.</summary>
	public static readonly ModerationArea Beatmapsets = new(UserPermissions.BeatmapsetUpload,
		UserPermissions.BeatmapsetManage);

	/// <summary>The screenshot permissions and the permission that manages them.</summary>
	public static readonly ModerationArea Screenshots =
		new(UserPermissions.ScreenshotUpload, UserPermissions.ScreenshotDelete);

	/// <summary>Every area, in a fixed order.</summary>
	public static readonly ImmutableArray<ModerationArea> Areas =
	[
		Chat, Multiplayer, Spectating, Scores, Beatmapsets, Screenshots
	];
}