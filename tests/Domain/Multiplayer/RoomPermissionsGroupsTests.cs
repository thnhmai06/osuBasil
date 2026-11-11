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
		RoomPermissions.MembersInvite |
		RoomPermissions.RefereesManage |
		RoomPermissions.HostTransfer |
		RoomPermissions.RoundsStart |
		RoomPermissions.RoundsAbort |
		RoomPermissions.RoomClose |
		RoomPermissions.ChatPost;

	[Fact]
	public void NoRole_NothingGranted()
	{
		Assert.Equal(RoomPermissions.None, RoomRoles.None.GetPermissions());
	}

	[Fact]
	public void Player_ChatAndInviting()
	{
		Assert.Equal(RoomPermissions.ChatPost | RoomPermissions.MembersInvite,
			RoomRoles.Player.GetPermissions());
	}

	[Fact]
	public void Host_InClientOperations()
	{
		const RoomPermissions expected =
			RoomPermissions.SettingsChange |
			RoomPermissions.SettingsRename |
			RoomPermissions.SettingsPassword |
			RoomPermissions.SlotsLock |
			RoomPermissions.MembersKick |
			RoomPermissions.HostTransfer |
			RoomPermissions.RoundsStart |
			RoomPermissions.RoundsAbort;

		Assert.Equal(expected, RoomRoles.Host.GetPermissions());
	}

	[Fact]
	public void Referee_AllButManaging()
	{
		Assert.Equal(EveryRight & ~RoomPermissions.RefereesManage, RoomRoles.Referee.GetPermissions());
	}

	[Fact]
	public void Creator_EveryRight()
	{
		Assert.Equal(EveryRight, RoomRoles.Creator.GetPermissions());
	}

	[Fact]
	public void HostAndPlayer_Union()
	{
		Assert.Equal(RoomRoles.Host.GetPermissions() | RoomRoles.Player.GetPermissions(),
			(RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void RefereeAndPlayer_Union()
	{
		Assert.Equal(RoomRoles.Referee.GetPermissions() | RoomRoles.Player.GetPermissions(),
			(RoomRoles.Referee | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void CreatorAndReferee_EveryRight()
	{
		Assert.Equal(EveryRight, (RoomRoles.Creator | RoomRoles.Referee).GetPermissions());
	}

	[Fact]
	public void CreatorHostPlayer_EveryRight()
	{
		Assert.Equal(EveryRight, (RoomRoles.Creator | RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void AllRoles_EveryRight()
	{
		Assert.Equal(EveryRight,
			(RoomRoles.Creator | RoomRoles.Referee | RoomRoles.Host | RoomRoles.Player).GetPermissions());
	}

	[Fact]
	public void Referee_MoreThanHost()
	{
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.SlotsMovePlayers));
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.MembersBan));
		Assert.True(RoomRoles.Referee.GetPermissions().HasFlag(RoomPermissions.RoomClose));
	}

	[Fact]
	public void Host_NoManageReferees()
	{
		Assert.False(RoomRoles.Host.GetPermissions().HasFlag(RoomPermissions.RefereesManage));
	}

	[Fact]
	public void Player_NoManagement()
	{
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SlotsMovePlayers));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SlotsLock));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.MembersKick));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.MembersBan));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.HostTransfer));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoundsStart));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoundsAbort));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.RoomClose));
		Assert.False(RoomRoles.Player.GetPermissions().HasFlag(RoomPermissions.SettingsChange));
	}
}