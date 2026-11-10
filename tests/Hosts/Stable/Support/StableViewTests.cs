using Basil.Domain.Users;
using Basil.Host.Stable.Support;

namespace Basil.Host.Stable.Tests.Support;

/// <summary>The roles a client is told an account has.</summary>
public sealed class StableViewTests
{
	[Fact]
	public void Roles_AreTheEffectivePrivileges()
	{
		const UserPrivileges held = UserPrivileges.Player | UserPrivileges.Moderator;

		Assert.Equal(held, StableView.Roles(held));
	}

	[Fact]
	public void Roles_WithoutAnyPrivilege_FallBackToPlayer()
	{
		Assert.Equal(UserPrivileges.Player, StableView.Roles(UserPrivileges.None));
	}
}