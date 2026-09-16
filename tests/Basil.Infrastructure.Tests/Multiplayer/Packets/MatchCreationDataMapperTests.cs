using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Multiplayer.Packets;
using Basil.Protocol.Multiplayer;

namespace Basil.Infrastructure.Tests.Multiplayer.Packets;

public class MatchCreationDataMapperTests
{
	private static MatchState MakeWireData(int mapId, int hostId = 1)
	{
		return new MatchState(
			0, false, 0, 0, "test", "",
			"", mapId, new string('a', 32),
			[], [], [], hostId, (int)GameMode.Standard, (int)MatchWinCondition.Score,
			(int)MatchTeamType.HeadToHead, false, [], 0);
	}

	/// <summary>
	///     Regression test (Issue #4): the mapped MapId is null domain-side, not the wire's 0/-1
	///     sentinels. `0` is what an HTTP creation request leaves as an unused placeholder (see
	///     MatchListEndpoints.HandleCreate); `-1` is a real client's explicit "no beatmap chosen".
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ToCreationData_WireMapIdIsZeroOrNegativeOne_MappedMapIdIsNull(int wireMapId)
	{
		var data = MakeWireData(wireMapId).ToCreationData();

		Assert.Null(data.MapId);
	}

	[Fact]
	public void ToCreationData_WireMapIdIsPositive_MappedMapIdMatches()
	{
		var data = MakeWireData(654).ToCreationData();

		Assert.Equal(654, data.MapId);
	}

	[Fact]
	public void IsValid_HostIdMatchesAndNameFitsWithinLimit_ReturnsTrue()
	{
		Assert.True(MatchCreationDataMapper.IsValid(MakeWireData(0, hostId: 7), 7));
	}

	[Fact]
	public void IsValid_HostIdDoesNotMatch_ReturnsFalse()
	{
		Assert.False(MatchCreationDataMapper.IsValid(MakeWireData(0, hostId: 7), 8));
	}

	[Fact]
	public void IsValid_NameLongerThanLimit_ReturnsFalse()
	{
		var data = MakeWireData(0, hostId: 7) with { Name = new string('a', 51) };

		Assert.False(MatchCreationDataMapper.IsValid(data, 7));
	}
}
