using Basil.Domain.Multiplayer;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>
///     Pins the privileges each room role groups. The expected masks are written from the rules in
///     AGENTS.md ("Authority") and docs/for-developers/multiplayer.md ("Room roles and permissions"), not
///     from the implementation.
/// </summary>
public class RoomPrivilegesTests
{
	private const RoomPrivileges EveryPrivilege =
		RoomPrivileges.SlotsMovePlayers |
		RoomPrivileges.SlotsLock |
		RoomPrivileges.SettingsChange |
		RoomPrivileges.SettingsRename |
		RoomPrivileges.SettingsPassword |
		RoomPrivileges.SettingsPrivate |
		RoomPrivileges.MembersKick |
		RoomPrivileges.MembersBan |
		RoomPrivileges.MembersInvite |
		RoomPrivileges.RefereesManage |
		RoomPrivileges.HostTransfer |
		RoomPrivileges.RoundsStart |
		RoomPrivileges.RoundsAbort |
		RoomPrivileges.RoomClose |
		RoomPrivileges.ChatPost;

	[Fact]
	public void Player_ChatAndInviting()
	{
		Assert.Equal(RoomPrivileges.ChatPost | RoomPrivileges.MembersInvite,
			RoomPrivileges.Player);
	}

	[Fact]
	public void Host_InClientOperations()
	{
		const RoomPrivileges expected =
			RoomPrivileges.SettingsChange |
			RoomPrivileges.SettingsRename |
			RoomPrivileges.SettingsPassword |
			RoomPrivileges.SlotsLock |
			RoomPrivileges.MembersKick |
			RoomPrivileges.HostTransfer |
			RoomPrivileges.RoundsStart |
			RoomPrivileges.RoundsAbort;

		Assert.Equal(expected, RoomPrivileges.Host);
	}

	[Fact]
	public void Referee_AllButManaging()
	{
		Assert.Equal(EveryPrivilege & ~RoomPrivileges.RefereesManage, RoomPrivileges.Referee);
	}

	[Fact]
	public void Creator_EveryPrivilege()
	{
		Assert.Equal(EveryPrivilege, RoomPrivileges.Creator);
	}

	[Fact]
	public void Referee_NotCreator()
	{
		Assert.False(RoomPrivileges.Referee.HasFlag(RoomPrivileges.Creator));
	}

	[Fact]
	public void CreatorAndReferee_EveryPrivilege()
	{
		Assert.Equal(EveryPrivilege, (RoomPrivileges.Creator | RoomPrivileges.Referee));
	}

	[Fact]
	public void CreatorHostPlayer_EveryPrivilege()
	{
		Assert.Equal(EveryPrivilege, (RoomPrivileges.Creator | RoomPrivileges.Host | RoomPrivileges.Player));
	}

	[Fact]
	public void AllRoles_EveryPrivilege()
	{
		Assert.Equal(EveryPrivilege,
			(RoomPrivileges.Creator | RoomPrivileges.Referee | RoomPrivileges.Host | RoomPrivileges.Player));
	}

	[Fact]
	public void Referee_MoreThanHost()
	{
		Assert.True(RoomPrivileges.Referee.HasFlag(RoomPrivileges.SlotsMovePlayers));
		Assert.True(RoomPrivileges.Referee.HasFlag(RoomPrivileges.MembersBan));
		Assert.True(RoomPrivileges.Referee.HasFlag(RoomPrivileges.RoomClose));
	}

	[Fact]
	public void Host_NoManageReferees()
	{
		Assert.False(RoomPrivileges.Host.HasFlag(RoomPrivileges.RefereesManage));
	}

	[Fact]
	public void Player_NoManagement()
	{
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.SlotsMovePlayers));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.SlotsLock));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.MembersKick));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.MembersBan));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.HostTransfer));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.RoundsStart));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.RoundsAbort));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.RoomClose));
		Assert.False(RoomPrivileges.Player.HasFlag(RoomPrivileges.SettingsChange));
	}
}