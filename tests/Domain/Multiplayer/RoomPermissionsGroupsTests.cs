using Basil.Domain.Multiplayer;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>
///     Pins the permissions each room role grants. The expected masks are written from the rules in
///     AGENTS.md ("Authority") and docs/for-developers/multiplayer.md ("Room roles and permissions"), not
///     from the implementation.
/// </summary>
public class RoomPermissionsGroupsTests
{
	private const RoomPermissions EveryRight =
		RoomPermissions.SlotsMovePlayers |
		RoomPermissions.SlotsLock |
		RoomPermissions.SettingsChange |
		RoomPermissions.SettingsRename |
		RoomPermissions.SettingsPassword |
		RoomPermissions.SettingsPrivate |
		RoomPermissions.MembersKick |
		RoomPermissions.MembersBan |
		RoomPermissions.MembersSeat |
		RoomPermissions.MembersInvite |
		RoomPermissions.RefereesManage |
		RoomPermissions.HostTransfer |
		RoomPermissions.RoundsStart |
		RoomPermissions.RoundsAbort |
		RoomPermissions.RoundsCountdown |
		RoomPermissions.RoomClose |
		RoomPermissions.ChatPost;

	[Fact]
	public void GetPermissions_NoRole_GrantsNothing()
	{
		Assert.Equal(RoomPermissions.None, RoomRoles.None.GetPermissions());
	}

	[Fact]
	public void GetPermissions_Player_GrantsChatAndInviting()
	{
		Assert.Equal(RoomPermissions.ChatPost | RoomPermissions.MembersInvite,
			RoomRoles.Player.GetPermissions());
	}

	[Fact]
	public void GetPermissions_Host_GrantsTheInClientHostOperations()
	{
		const RoomPermissions expected =
			RoomPermissions.SettingsChange |
			RoomPermissions.SettingsRename |
			RoomPermissions.SettingsPassword |
			RoomPermissions.SlotsLock |
			RoomPermissions.MembersKick |
			RoomPermissions.HostTransfer |
			RoomPermissions.RoundsStart |
			RoomPermissions.RoundsAbort |
			RoomPermissions.RoundsCountdown;

		Assert.Equal(expected, RoomRoles.Host.GetPermissions());
	}

	[Fact]
	public void GetPermissions_Referee_GrantsEveryRightExceptManagingReferees()
	{
		Assert.Equal(EveryRight & ~RoomPermissions.RefereesManage, RoomRoles.Referee.GetPermissions());
	}

	[Fact]
	public void GetPermissions_Creator_GrantsEveryRight()
	{
		Assert.Equal(EveryRight, RoomRoles.Creator.GetPermissions());
	}

	[Fact]
	public void GetPermissions_HostAndPlayer_GrantsTheUnion()
	{
		Assert.Equal(RoomRoles.Host.GetPermissions() | RoomRoles.Player.GetPermissions(),
			(RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void GetPermissions_RefereeAndPlayer_GrantsTheUnion()
	{
		Assert.Equal(RoomRoles.Referee.GetPermissions() | RoomRoles.Player.GetPermissions(),
			(RoomRoles.Referee | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void GetPermissions_CreatorAndReferee_GrantsEveryRight()
	{
		Assert.Equal(EveryRight, (RoomRoles.Creator | RoomRoles.Referee).GetPermissions());
	}

	[Fact]
	public void GetPermissions_CreatorAndHostAndPlayer_GrantsEveryRight()
	{
		Assert.Equal(EveryRight, (RoomRoles.Creator | RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void GetPermissions_AllRoles_GrantsEveryRight()
	{
		Assert.Equal(EveryRight,
			(RoomRoles.Creator | RoomRoles.Referee | RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void GetPermissions_Referee_GrantsMoreThanTheHost()
	{
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.SlotsMovePlayers));
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.MembersBan));
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.MembersSeat));
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.RoomClose));
	}

	[Fact]
	public void GetPermissions_Host_DoesNotGrantManagingReferees()
	{
		Assert.False(RoomRoles.Host.GetPermissions().HasFlag(RoomPermissions.RefereesManage));
	}

	[Fact]
	public void GetPermissions_Player_DoesNotGrantAnyManagement()
	{
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SlotsMovePlayers));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SlotsLock));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.MembersKick));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.MembersBan));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.MembersSeat));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.HostTransfer));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoundsStart));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoundsAbort));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoundsCountdown));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoomClose));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SettingsChange));
	}
}