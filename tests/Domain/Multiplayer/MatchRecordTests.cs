using System.Reflection;
using System.Runtime.CompilerServices;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

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

	[Fact]
	public void Creator_IsRequired_EveryMatchHasOne()
	{
		var creator = typeof(MatchData).GetProperty(nameof(MatchData.Creator))!;

		Assert.Contains(creator.CustomAttributes,
			a => a.AttributeType == typeof(RequiredMemberAttribute));
		Assert.Equal(typeof(UserRecord), creator.PropertyType);
		Assert.True(new NullabilityInfoContext().Create(creator).WriteState == NullabilityState.NotNull);
	}

	private static MatchData MakeData()
	{
		return new MatchData
		{
			Name = "match",
			Creator = new UserRecord { Id = 1, Value = new UserData { Name = "creator" } },
			StartedAt = DateTimeOffset.UnixEpoch,
			EndedAt = null
		};
	}
}