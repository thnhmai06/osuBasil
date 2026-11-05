using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

/// <summary>Verifies the validation of the statistics a user's account carries.</summary>
public class UserStatsTests
{
	[Fact]
	public void Mode_Undefined_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats { UserId = 1, Mode = (GameMode)200 });
	}

	[Fact]
	public void TotalScore_Negative_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			TotalScore = -1
		});
	}

	[Fact]
	public void RankedScore_Negative_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			RankedScore = -1
		});
	}

	[Fact]
	public void PlayCount_Negative_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserStats
		{
			UserId = 1,
			Mode = GameMode.Standard,
			PlayCount = -1
		});
	}
}