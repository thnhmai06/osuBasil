using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public class UserPrivilegesTests
{
	[Fact]
	public void Privileges_DerivedFromHeldPermissions()
	{
		Assert.Equal(UserPrivileges.None, UserPermissions.None.Privileges);
		Assert.Equal(UserPrivileges.Player | UserPrivileges.Supporter, UserPermissionsGroups.Default.Privileges);
		Assert.Equal(UserPrivileges.Player | UserPrivileges.Supporter | UserPrivileges.Tournament, UserPermissions.RoomObserve.Privileges);
		Assert.Equal(UserPrivileges.Moderator, UserPermissions.UserSilence.Privileges);
	}
}