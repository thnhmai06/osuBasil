using Basil.Domain.Users;
using Basil.Host.Bancho.Support;

namespace Basil.Host.Bancho.Tests.Support;

/// <summary>The roles a client is told an account has.</summary>
public sealed class BanchoViewTests
{
	[Fact]
	public void Roles_OfAnObserver_IncludeSupporter()
	{
		var roles = BanchoView.Roles(UserPermissions.RoomObserve);

		Assert.True(roles.HasFlag(UserPrivileges.Supporter));
	}

	[Fact]
	public void Roles_WithoutAnyPermission_FallBackToPlayer()
	{
		Assert.Equal(UserPrivileges.Player, BanchoView.Roles(UserPermissions.None));
	}
}
