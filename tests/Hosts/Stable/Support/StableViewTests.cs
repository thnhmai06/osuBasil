using Basil.Domain.Users;
using Basil.Host.Stable.Support;

namespace Basil.Host.Stable.Tests.Support;

/// <summary>The roles a client is told an account has.</summary>
public sealed class StableViewTests
{
	[Fact]
	public void Roles_EffectivePrivileges()
	{
		const UserPrivileges held = UserPrivileges.Player | UserPrivileges.Moderator;

		Assert.Equal(held, StableView.Roles(held));
	}

	[Fact]
	public void NoPrivileges_PlayerDefault()
	{
		Assert.Equal(UserPrivileges.Player, StableView.Roles(UserPrivileges.None));
	}
}