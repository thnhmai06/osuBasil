using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Domain.Tests;

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

	private static Restriction MakeRestriction(DateTimeOffset startsAt, DateTimeOffset? endsAt)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "restricted" } };
		return new Restriction
		{
			Id = 1,
			Value = new RestrictionData
			{
				User = user,
				Permissions = RestrictionData.Restricted,
				StartsAt = startsAt,
				EndsAt = endsAt
			}
		};
	}
}