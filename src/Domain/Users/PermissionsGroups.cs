namespace Basil.Domain.Users;

/// <summary>The masks of permissions by area, and the permission that manages each area.</summary>
public static class PermissionsGroups
{
	/// <summary>The permissions of one area and the permission that manages it.</summary>
	/// <param name="Permissions">The permissions the area covers.</param>
	/// <param name="Manager">
	///     The permission a user must hold in full to keep the area's permissions in effect while they are restricted;
	///     <see cref="Basil.Domain.Users.Permissions.None" /> means nobody is exempt.
	/// </param>
	/// <remarks>A restriction never suspends the permissions of an area from a user who holds the area's manager.</remarks>
	public readonly record struct PermissionArea(Permissions Permissions, Permissions Manager);

	/// <summary>Every permission the server defines.</summary>
	public static readonly Permissions All =
		Permissions.RoomCreate | Permissions.RoomJoin | Permissions.RoomObserve | Permissions.RoomViewPrivate |
		Permissions.RoomManageAny | Permissions.RoomCreateUnlimited | Permissions.ChatPost |
		Permissions.ChatPostPrivate | Permissions.ChatPostAnyRoom | Permissions.ChatMessageAnyone |
		Permissions.ChatAnnounce | Permissions.SpectatingWatch | Permissions.ScoreSubmit |
		Permissions.BeatmapsetUpload | Permissions.BeatmapsetDirect | Permissions.BeatmapsetViewHidden |
		Permissions.BeatmapsetManage | Permissions.ScreenshotUpload | Permissions.ScreenshotDelete |
		Permissions.UserNote | Permissions.UserSilence | Permissions.UserViewLogins | Permissions.UserRestrict |
		Permissions.UserViewDeleted | Permissions.UserManage | Permissions.UserManagePermissions |
		Permissions.ServerConfigure | Permissions.ServerViewDiagnostics | Permissions.ServerManageContent;

	/// <summary>The permissions a new account holds.</summary>
	public static readonly Permissions Default = Privileges.Player.Permissions | Privileges.Supporter.Permissions;

	/// <summary>The permissions a delegated room operation may use; kept only while delegation is being removed.</summary>
	public static readonly Permissions LegacyRoomDelegation = Permissions.RoomCreate | Permissions.RoomJoin |
	                                                         Permissions.RoomManageAny | Permissions.RoomCreateUnlimited;

	/// <summary>The chatting permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Chat =
		new(Permissions.ChatPost | Permissions.ChatPostPrivate, Permissions.ChatMessageAnyone);

	/// <summary>The multiplayer permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Multiplayer =
		new(Permissions.RoomCreate | Permissions.RoomJoin | Permissions.RoomCreateUnlimited, Permissions.RoomManageAny);

	/// <summary>The spectating permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Spectating = new(Permissions.SpectatingWatch, Permissions.RoomObserve);

	/// <summary>The scoring permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Scores = new(Permissions.ScoreSubmit, Permissions.None);

	/// <summary>The beatmapset permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Beatmapsets = new(Permissions.BeatmapsetUpload, Permissions.BeatmapsetManage);

	/// <summary>The screenshot permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Screenshots =
		new(Permissions.ScreenshotUpload, Permissions.ScreenshotDelete);

	/// <summary>Every area, in a fixed order.</summary>
	public static readonly IReadOnlyList<PermissionArea> Areas =
		[Chat, Multiplayer, Spectating, Scores, Beatmapsets, Screenshots];
}
