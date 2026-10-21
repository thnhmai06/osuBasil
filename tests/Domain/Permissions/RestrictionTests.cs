using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Permission = Basil.Domain.Permissions.Permissions;

namespace Basil.Domain.Tests.Permissions;

public class RestrictionTests
{
	[Fact]
	public void IsActive_StartInclusiveAndEndExclusive()
	{
		var startsAt = DateTimeOffset.UnixEpoch;
		var restriction = MakeRestriction(startsAt, startsAt.AddHours(1));

		Assert.True(restriction.Value.IsActive(startsAt));
		Assert.False(restriction.Value.IsActive(startsAt.AddHours(1)));
	}

	[Fact]
	public void Permissions_None_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new RestrictionData
		{
			User = MakeUser(),
			Permissions = Permission.None,
			StartsAt = DateTimeOffset.UnixEpoch
		});
	}

	[Fact]
	public void Permissions_UndefinedBit_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new RestrictionData
		{
			User = MakeUser(),
			Permissions = (Permission)(1UL << 60),
			StartsAt = DateTimeOffset.UnixEpoch
		});
	}

	[Fact]
	public void StartsAt_AfterEndsAt_Throws()
	{
		var now = DateTimeOffset.UnixEpoch;

		Assert.Throws<ArgumentOutOfRangeException>(() => new RestrictionData
		{
			User = MakeUser(),
			Permissions = RestrictionData.Restricted,
			StartsAt = now.AddHours(1),
			EndsAt = now
		});
	}

	[Fact]
	public void EndsAt_BeforeStartsAt_Throws()
	{
		var now = DateTimeOffset.UnixEpoch;

		Assert.Throws<ArgumentOutOfRangeException>(() => new RestrictionData
		{
			User = MakeUser(),
			Permissions = RestrictionData.Restricted,
			EndsAt = now,
			StartsAt = now.AddHours(1)
		});
	}

	private static Restriction MakeRestriction(DateTimeOffset startsAt, DateTimeOffset? endsAt)
	{
		return new Restriction
		{
			Id = 1,
			Value = new RestrictionData
			{
				User = MakeUser(),
				Permissions = RestrictionData.Restricted,
				StartsAt = startsAt,
				EndsAt = endsAt
			}
		};
	}

	private static UserRecord MakeUser()
	{
		return new UserRecord { Id = 1, Value = new UserData { Name = "restricted" } };
	}
}