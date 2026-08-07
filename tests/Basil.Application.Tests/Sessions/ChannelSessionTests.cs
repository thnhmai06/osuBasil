using Basil.Application.Sessions.Channels;
using Basil.Domain.Users;

namespace Basil.Application.Tests.Sessions;

/// <summary>Verifies `ChannelSession`'s read/write privilege gating (`CanRead`/`CanWrite`) and live membership tracking.</summary>
public class ChannelSessionTests
{
	[Fact]
	public void CanRead_ZeroReadPriv_AlwaysTrue()
	{
		var channel = new ChannelSession(1, "#osu", 0, (UserPrivileges)2, true);

		Assert.True(channel.CanRead(0));
	}

	[Fact]
	public void CanRead_OverlappingBit_IsTrue()
	{
		var channel = new ChannelSession(1, "#staff", UserPrivileges.Staff, UserPrivileges.Staff, true);

		Assert.True(channel.CanRead(UserPrivileges.Moderator));
	}

	[Fact]
	public void CanRead_NoOverlappingBit_IsFalse()
	{
		var channel = new ChannelSession(1, "#staff", UserPrivileges.Staff, UserPrivileges.Staff, true);

		Assert.False(channel.CanRead(UserPrivileges.Unrestricted | UserPrivileges.Verified));
	}

	[Fact]
	public void CanWrite_ZeroWritePriv_AlwaysTrue()
	{
		var channel = new ChannelSession(1, "#osu", (UserPrivileges)1, 0, true);

		Assert.True(channel.CanWrite(0));
	}

	[Fact]
	public void JoinThenPart_UpdatesPlayerCount()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);

		channel.Join(1);
		channel.Join(2);
		Assert.Equal(2, channel.PlayerCount);
		Assert.True(channel.Contains(1));

		channel.Part(1);
		Assert.Equal(1, channel.PlayerCount);
		Assert.False(channel.Contains(1));
	}

	[Fact]
	public void DisplayName_DefaultsToName()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);

		Assert.Equal("#osu", channel.DisplayName);
	}

	[Fact]
	public void DisplayName_CanDifferFromRegistryName()
	{
		var channel = new ChannelSession(0, "#spec_5", 0, 0, false, "#spectator", true);

		Assert.Equal("#spec_5", channel.Name);
		Assert.Equal("#spectator", channel.DisplayName);
		Assert.True(channel.Instance);
	}

	[Fact]
	public void MemberIds_ReflectsJoinsAndParts()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		channel.Join(1);
		channel.Join(2);

		Assert.Equal([1, 2], channel.MemberIds.OrderBy(id => id));

		channel.Part(1);

		Assert.Equal([2], channel.MemberIds);
	}

	[Fact]
	public void Join_FirstSessionForUserId_ReturnsTrue()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);

		Assert.True(channel.Join(1));
	}

	[Fact]
	public void Join_SecondSessionForSameUserId_ReturnsFalse_ButPlayerCountStaysOne()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);

		Assert.True(channel.Join(1));
		Assert.False(channel.Join(1));
		Assert.Equal(1, channel.PlayerCount);
	}

	[Fact]
	public void Part_WhileAnotherSessionOfSameUserIdRemains_ReturnsFalse_ButStaysInRoster()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		channel.Join(1);
		channel.Join(1); // 2 sessions of the same UserId

		Assert.False(channel.Part(1));
		Assert.True(channel.Contains(1));
	}

	[Fact]
	public void Part_LastSessionForUserId_ReturnsTrue_AndLeavesRoster()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);
		channel.Join(1);
		channel.Join(1);

		channel.Part(1);
		Assert.True(channel.Part(1));
		Assert.False(channel.Contains(1));
	}

	[Fact]
	public void Part_UnknownUserId_ReturnsFalse()
	{
		var channel = new ChannelSession(1, "#osu", 0, 0, true);

		Assert.False(channel.Part(999));
	}
}