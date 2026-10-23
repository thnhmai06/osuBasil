using System.Collections.Immutable;

namespace Basil.Domain.Permissions;

/// <summary>The masks of permissions by area, and the permission that manages each area.</summary>
public static class PermissionsGroups
{
	/// <summary>Every permission the server defines.</summary>
	public const Permissions All =
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

	/// <summary>The chatting permissions and the permission that manages them.</summary>
	public static readonly PermissionArea Chat =
		new(Permissions.ChatPost | Permissions.ChatPostPrivate | Permissions.ChatPostAnyRoom,
			Permissions.ChatMessageAnyone);

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
	public static readonly ImmutableArray<PermissionArea> Areas =
	[
		Chat, Multiplayer, Spectating, Scores, Beatmapsets, Screenshots
	];
}