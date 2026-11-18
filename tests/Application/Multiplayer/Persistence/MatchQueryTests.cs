using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Tests.Persistence;

public sealed class MatchQueryTests
{
	private static readonly UserRecord Viewer = new() { Id = 7, Value = new UserData { Name = "viewer" } };

	[Fact]
	public void AskerViewPrivate_QueryUnrestricted()
	{
		Assert.Null(new MatchQuery().LimitTo(UserPrivileges.Tournament, Viewer).VisibleTo);
	}

	[Fact]
	public void AskerManageAny_QueryUnrestricted()
	{
		Assert.Null(new MatchQuery().LimitTo(UserPrivileges.Tournament, Viewer).VisibleTo);
	}

	[Fact]
	public void AskerNoView_PrivateRestricted()
	{
		Assert.Equal(Viewer.Id, new MatchQuery().LimitTo(UserPrivilegesExtensions.Default, Viewer).VisibleTo);
	}

	[Fact]
	public void AskerNoView_SearchKept()
	{
		var limited = MatchQuery.Parse("private=1 ended=0").LimitTo(UserPrivileges.None, Viewer);

		Assert.True(limited.Private);
		Assert.False(limited.Ended);
	}

	[Fact]
	public void AnyText_NoViewer()
	{
		Assert.Null(MatchQuery.Parse("private=* ended=* visibleto=7").VisibleTo);
	}
}