using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Multiplayer;

namespace Basil.Host.Stable.Tests.Support;

/// <summary>
///     Numbers the osu! client sends reach Domain only through <see cref="WireMap" />, which refuses what Domain does
///     not define; the two client concepts without a Domain twin (a slot's team and status) are mapped both ways.
/// </summary>
public sealed class WireMapTests
{
	[Theory]
	[InlineData(0, GameMode.Standard)]
	[InlineData(3, GameMode.Mania)]
	public void DefinedNumber_DomainValue(int wire, GameMode expected)
	{
		Assert.Equal(expected, WireMap.Known<GameMode>(wire));
	}

	[Theory]
	[InlineData(4)]
	[InlineData(-1)]
	[InlineData(256)]
	public void UndefinedNumber_Null(int wire)
	{
		Assert.Null(WireMap.Known<GameMode>(wire));
	}

	[Fact]
	public void SignedEnum_NegativeAccepted()
	{
		Assert.Equal(BeatmapsetStatus.NotSubmitted, WireMap.Known<BeatmapsetStatus>(-1));
	}

	[Theory]
	[InlineData(2, UserActivity.Playing)]
	[InlineData(13, UserActivity.OsuDirect)]
	public void KnownActivity_DomainValue(int wire, UserActivity expected)
	{
		Assert.Equal(expected, WireMap.Known<UserActivity>(wire));
	}

	[Fact]
	public void UnknownActivity_Null()
	{
		Assert.Null(WireMap.Known<UserActivity>(14));
	}

	[Fact]
	public void ModsCombination_DomainMods()
	{
		Assert.Equal(GameMods.Hidden | GameMods.DoubleTime, WireMap.Mods(8 | 64));
	}

	[Fact]
	public void UnknownModBit_Dropped()
	{
		Assert.Equal(GameMods.Easy, WireMap.Mods((1L << 31) | 2));
	}

	[Fact]
	public void AllNegativeBits_DefinedKept()
	{
		var all = Enum.GetValues<GameMods>().Aggregate(GameMods.NoMod, (all, mod) => all | mod);

		Assert.Equal(all, WireMap.Mods(-1));
	}

	[Theory]
	[InlineData(null, (byte)0)]
	[InlineData(GameTeam.Blue, (byte)1)]
	[InlineData(GameTeam.Red, (byte)2)]
	public void SlotTeam_MapsToWire(GameTeam? domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
	}

	[Theory]
	[InlineData((byte)0, null)]
	[InlineData((byte)1, GameTeam.Blue)]
	[InlineData((byte)2, GameTeam.Red)]
	[InlineData((byte)3, null)]
	public void SlotTeam_MapsFromWire(byte wire, GameTeam? domain)
	{
		Assert.Equal(domain, WireMap.FromWire((WireSlotTeam)wire));
	}

	[Fact]
	public void UnknownTeamValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((GameTeam)9));
	}

	[Fact]
	public void SlotTeam_RoundTrips()
	{
		foreach (var team in Enum.GetValues<GameTeam>()) Assert.Equal(team, WireMap.FromWire(WireMap.ToWire(team)));
	}

	[Theory]
	[InlineData(RoomSlotStatus.NotReady, (byte)4)]
	[InlineData(RoomSlotStatus.Ready, (byte)8)]
	[InlineData(RoomSlotStatus.NoMap, (byte)16)]
	[InlineData(RoomSlotStatus.Playing, (byte)32)]
	[InlineData(RoomSlotStatus.Complete, (byte)64)]
	public void SlotStatus_MapsBothWays(RoomSlotStatus domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireSlotStatus)wire));
	}
}