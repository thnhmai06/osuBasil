using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public class UserPrivilegesTests
{
	[Fact]
	public void All_OrOfMembers()
	{
		var orOfMembers = Enum.GetValues<UserPrivileges>()
			.Aggregate(UserPrivileges.None, (all, member) => all | member);

		Assert.Equal(UserPrivilegesExtensions.All, orOfMembers);
	}

	[Fact]
	public void Default_PlayerAndSupporter()
	{
		Assert.Equal(UserPrivileges.Player | UserPrivileges.Supporter, UserPrivilegesExtensions.Default);
	}

	[Theory]
	[InlineData(UserPrivileges.None, UserPrivileges.None, true)]
	[InlineData(UserPrivileges.Player, UserPrivileges.None, true)]
	[InlineData(UserPrivileges.Player, UserPrivileges.Player, true)]
	[InlineData(UserPrivileges.Player, UserPrivileges.Player | UserPrivileges.Moderator, false)]
	[InlineData(UserPrivileges.Player | UserPrivileges.Moderator, UserPrivileges.Moderator, true)]
	[InlineData(UserPrivileges.None, UserPrivileges.Owner, false)]
	public void Allows_RequiresEveryBit(UserPrivileges held, UserPrivileges required, bool expected)
	{
		Assert.Equal(expected, held.Allows(required));
	}

	[Fact]
	public void Tournament_AddsSupporter()
	{
		Assert.Equal(UserPrivileges.Tournament | UserPrivileges.Supporter, UserPrivileges.Tournament.Completed());
	}

	[Theory]
	[InlineData(UserPrivileges.None)]
	[InlineData(UserPrivileges.Player)]
	[InlineData(UserPrivileges.Owner | UserPrivileges.Moderator)]
	public void WithoutTournament_ChangesNothing(UserPrivileges privileges)
	{
		Assert.Equal(privileges, privileges.Completed());
	}

	[Fact]
	public void SilencedUntil_OnlyBeforeEnd()
	{
		var now = DateTimeOffset.UnixEpoch;
		var data = new UserData { Name = "talker", SilencedUntil = now.AddMinutes(5) };

		Assert.True(data.IsSilenced(now));
		Assert.False(data.IsSilenced(now.AddMinutes(5)));
		Assert.False(new UserData { Name = "free" }.IsSilenced(now));
	}
}
