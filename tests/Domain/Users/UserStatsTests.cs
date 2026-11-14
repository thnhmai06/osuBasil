using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

/// <summary>Verifies the validation of the statistics a user's account carries.</summary>
public class UserStatsTests
{
	[Fact]
	public void UndefinedMode_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats { UserId = 1, Mode = (GameMode)200 });
	}

	[Fact]
	public void NegativeTotalScore_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			TotalScore = -1
		});
	}

	[Fact]
	public void NegativeRankedScore_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			RankedScore = -1
		});
	}

	[Fact]
	public void NegativePlayCount_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			PlayCount = -1
		});
	}
}