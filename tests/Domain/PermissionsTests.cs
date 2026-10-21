using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Permission = Basil.Domain.Permissions.Permissions;

namespace Basil.Domain.Tests;

public class PermissionsTests
{
	[Fact]
	public void Effective_ActiveRestriction_ManagerKeepsManagedArea()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = PermissionsGroups.Default | Permission.RoomManageAny | Permission.RoomCreateUnlimited;
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "manager", Permissions = granted } };
		var restriction = new Restriction
		{
			Id = 1,
			Value = new RestrictionData
			{
				User = user,
				Permissions = RestrictionData.Restricted,
				StartsAt = now.AddHours(-1),
				EndsAt = now.AddHours(1)
			}
		};

		var effective = granted.Effective([restriction], now);

		Assert.True(effective.Allows(PermissionsGroups.Multiplayer.Permissions));
		Assert.False(effective.Allows(Permission.ChatPost));
		Assert.False(effective.Allows(Permission.SpectatingWatch));
	}
}