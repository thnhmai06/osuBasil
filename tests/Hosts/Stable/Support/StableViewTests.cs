using Basil.Domain.Users;
using Basil.Host.Stable.Support;

namespace Basil.Host.Stable.Tests.Support;

/// <summary>The roles a client is told an account has.</summary>
public sealed class StableViewTests
{
	[Fact]
	public void Roles_OfAnObserver_IncludeSupporter()
	{
		var roles = StableView.Roles(UserPermissions.RoomObserve);

		Assert.True(roles.HasFlag(UserPrivileges.Supporter));
	}

	[Fact]
	public void Roles_WithoutAnyPermission_FallBackToPlayer()
	{
		Assert.Equal(UserPrivileges.Player, StableView.Roles(UserPermissions.None));
	}
}