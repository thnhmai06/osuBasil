using Basil.Domain.Permissions;
using Permission = Basil.Domain.Permissions.Permissions;

namespace Basil.Domain.Tests.Permissions;

public class PrivilegesTests
{
	[Fact]
	public void Privileges_DerivedFromHeldPermissions()
	{
		Assert.Equal(Privileges.None, Permission.None.Privileges);
		Assert.Equal(Privileges.Player | Privileges.Supporter, PermissionsGroups.Default.Privileges);
		Assert.Equal(Privileges.Player | Privileges.Supporter | Privileges.Tournament, Permission.RoomObserve.Privileges);
		Assert.Equal(Privileges.Moderator, Permission.UserSilence.Privileges);
	}
}