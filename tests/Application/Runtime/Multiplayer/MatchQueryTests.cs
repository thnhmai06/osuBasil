using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Tests.Multiplayer;

/// <summary>
///     Narrowing a match query to its asker. The rule comes from the owner's decision: an asker without
///     <see cref="Permissions.RoomViewPrivate" /> sees private matches only when they took part in them or manage them.
/// </summary>
public sealed class MatchQueryTests
{
	private static readonly UserRecord Viewer = new() { Id = 7, Value = new UserData { Name = "viewer" } };

	[Fact]
	public void LimitTo_AskerHoldingRoomViewPrivate_LeavesTheQueryUnrestricted()
	{
		Assert.Null(new MatchQuery().LimitTo(Permissions.RoomViewPrivate, Viewer).VisibleTo);
	}

	[Fact]
	public void LimitTo_AskerHoldingRoomManageAny_LeavesTheQueryUnrestricted()
	{
		Assert.Null(new MatchQuery().LimitTo(Permissions.RoomManageAny, Viewer).VisibleTo);
	}

	[Fact]
	public void LimitTo_AskerWithoutRoomViewPrivate_RestrictsPrivateMatchesToTheAsker()
	{
		Assert.Equal(Viewer.Id, new MatchQuery().LimitTo(PermissionsGroups.Default, Viewer).VisibleTo);
	}

	[Fact]
	public void LimitTo_AskerWithoutRoomViewPrivate_KeepsWhatTheAskerSearchedFor()
	{
		var limited = MatchQuery.Parse("private=1 ended=0").LimitTo(Permissions.None, Viewer);

		Assert.True(limited.Private);
		Assert.False(limited.Ended);
	}

	[Fact]
	public void Parse_AnyText_DoesNotSetAViewer()
	{
		Assert.Null(MatchQuery.Parse("private=* ended=* visibleto=7").VisibleTo);
	}
}
