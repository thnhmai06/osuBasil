using Basil.Domain.Multiplayer;

namespace Basil.Domain.Tests.Multiplayer;

/// <summary>Verifies the validation of a persisted match.</summary>
public class MatchRecordTests
{
	[Fact]
	public void Id_Zero_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRecord { Id = 0, Value = MakeData() });
	}

	[Fact]
	public void Id_Negative_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRecord { Id = -1, Value = MakeData() });
	}

	[Fact]
	public void Id_One_IsAccepted()
	{
		Assert.Equal(1, new MatchRecord { Id = 1, Value = MakeData() }.Id);
	}

	private static MatchData MakeData()
	{
		return new MatchData
		{
			Name = "match",
			StartedAt = DateTimeOffset.UnixEpoch,
			EndedAt = null
		};
	}
}