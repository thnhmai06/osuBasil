using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Permission = Basil.Domain.Permissions.Permissions;

namespace Basil.Domain.Tests.Permissions;

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

	[Fact]
	public void All_EqualsTheOrOfEveryMember()
	{
		var orOfMembers = Enum.GetValues<Permission>().Aggregate(Permission.None, (all, member) => all | member);

		Assert.Equal(PermissionsGroups.All, orOfMembers);
	}

	[Theory]
	[InlineData(Permission.RoomObserve, Permission.SpectatingWatch)]
	[InlineData(Permission.RoomManageAny, Permission.RoomViewPrivate)]
	[InlineData(Permission.BeatmapsetManage, Permission.BeatmapsetViewHidden)]
	[InlineData(Permission.UserSilence, Permission.UserNote)]
	[InlineData(Permission.UserRestrict, Permission.UserSilence)]
	[InlineData(Permission.UserManage, Permission.UserViewDeleted)]
	public void LargerPermission_ContainsTheSmallerOne(Permission larger, Permission smaller)
	{
		Assert.True(larger.Allows(smaller));
	}

	[Fact]
	public void Effective_ExpiredRestriction_SuspendsNothing()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = Permission.ChatPost;
		var restriction = MakeRestriction(Permission.ChatPost, now.AddHours(-2), now.AddHours(-1));

		Assert.Equal(granted, granted.Effective([restriction], now));
	}

	[Fact]
	public void Effective_FutureRestriction_SuspendsNothing()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = Permission.ChatPost;
		var restriction = MakeRestriction(Permission.ChatPost, now.AddHours(1), now.AddHours(2));

		Assert.Equal(granted, granted.Effective([restriction], now));
	}

	[Fact]
	public void Effective_OpenEndedRestriction_SuspendsFromItsStart()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = Permission.ChatPost;
		var restriction = MakeRestriction(Permission.ChatPost, now.AddHours(-1), null);

		Assert.False(granted.Effective([restriction], now).Allows(Permission.ChatPost));
	}

	[Fact]
	public void Effective_ActiveRestriction_SuspendsOnlyTheRestrictedBits()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = Permission.ChatPost | Permission.ScoreSubmit;
		var restriction = MakeRestriction(Permission.ChatPost, now.AddHours(-1), now.AddHours(1));

		var effective = granted.Effective([restriction], now);

		Assert.False(effective.Allows(Permission.ChatPost));
		Assert.True(effective.Allows(Permission.ScoreSubmit));
	}

	private static Restriction MakeRestriction(Permission suspended, DateTimeOffset startsAt, DateTimeOffset? endsAt)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "restricted" } };
		return new Restriction
		{
			Id = 1,
			Value = new RestrictionData
			{
				User = user,
				Permissions = suspended,
				StartsAt = startsAt,
				EndsAt = endsAt
			}
		};
	}
}