using Basil.Domain.Users;
using Basil.Host.OsuStable.Support;

namespace Basil.Host.OsuStable.Tests.Support;

/// <summary>The roles a client is told an account has.</summary>
public sealed class OsuStableViewTests
{
	[Fact]
	public void Roles_OfAnObserver_IncludeSupporter()
	{
		var roles = OsuStableView.Roles(UserPermissions.RoomObserve);

		Assert.True(roles.HasFlag(UserPrivileges.Supporter));
	}

	[Fact]
	public void Roles_WithoutAnyPermission_FallBackToPlayer()
	{
		Assert.Equal(UserPrivileges.Player, OsuStableView.Roles(UserPermissions.None));
	}
}