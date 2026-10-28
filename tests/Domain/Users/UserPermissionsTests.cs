using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public class UserPermissionsTests
{
	[Fact]
	public void Effective_ActiveRestriction_ManagerKeepsManagedArea()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = UserPermissionsGroups.Default | UserPermissions.RoomManageAny |
		              UserPermissions.RoomManageCreateUnlimited;
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "manager", Permissions = granted } };
		var restriction = new UserRestriction
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

		Assert.True(effective.Allows(UserPermissionsGroups.Multiplayer.Permissions));
		Assert.False(effective.Allows(UserPermissions.ChatPost));
		Assert.False(effective.Allows(UserPermissions.SpectatingWatch));
	}

	[Fact]
	public void All_EqualsTheOrOfEveryMember()
	{
		var orOfMembers = Enum.GetValues<UserPermissions>()
			.Aggregate(UserPermissions.None, (all, member) => all | member);

		Assert.Equal(UserPermissionsGroups.All, orOfMembers);
	}

	[Theory]
	[InlineData(UserPermissions.RoomObserve, UserPermissions.SpectatingWatch)]
	[InlineData(UserPermissions.RoomManageAny, UserPermissions.RoomManageViewPrivate)]
	[InlineData(UserPermissions.BeatmapsetManage, UserPermissions.BeatmapsetManageViewHidden)]
	[InlineData(UserPermissions.UserManageSilence, UserPermissions.UserManageNote)]
	[InlineData(UserPermissions.UserManageRestrict, UserPermissions.UserManageSilence)]
	[InlineData(UserPermissions.UserManage, UserPermissions.UserManageViewDeleted)]
	public void LargerPermission_ContainsTheSmallerOne(UserPermissions larger, UserPermissions smaller)
	{
		Assert.True(larger.Allows(smaller));
	}

	[Fact]
	public void Effective_ExpiredRestriction_SuspendsNothing()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = UserPermissions.ChatPost;
		var restriction = MakeRestriction(UserPermissions.ChatPost, now.AddHours(-2), now.AddHours(-1));

		Assert.Equal(granted, granted.Effective([restriction], now));
	}

	[Fact]
	public void Effective_FutureRestriction_SuspendsNothing()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = UserPermissions.ChatPost;
		var restriction = MakeRestriction(UserPermissions.ChatPost, now.AddHours(1), now.AddHours(2));

		Assert.Equal(granted, granted.Effective([restriction], now));
	}

	[Fact]
	public void Effective_OpenEndedRestriction_SuspendsFromItsStart()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = UserPermissions.ChatPost;
		var restriction = MakeRestriction(UserPermissions.ChatPost, now.AddHours(-1), null);

		Assert.False(granted.Effective([restriction], now).Allows(UserPermissions.ChatPost));
	}

	[Fact]
	public void Effective_ActiveRestriction_SuspendsOnlyTheRestrictedBits()
	{
		var now = DateTimeOffset.UnixEpoch;
		var granted = UserPermissions.ChatPost | UserPermissions.ScoreSubmit;
		var restriction = MakeRestriction(UserPermissions.ChatPost, now.AddHours(-1), now.AddHours(1));

		var effective = granted.Effective([restriction], now);

		Assert.False(effective.Allows(UserPermissions.ChatPost));
		Assert.True(effective.Allows(UserPermissions.ScoreSubmit));
	}

	private static UserRestriction MakeRestriction(UserPermissions suspended, DateTimeOffset startsAt,
		DateTimeOffset? endsAt)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "restricted" } };
		return new UserRestriction
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