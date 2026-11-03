using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Models.Wire;

namespace Basil.Host.Bancho.Tests.Support;

/// <summary>
///     Domain values reach the osu! client as the numbers of the bancho protocol (bancho.py's enums), and only through
///     <see cref="WireMap" />.
/// </summary>
public sealed class WireMapTests
{
	[Theory]
	[InlineData(GameMode.Standard, (byte)0)]
	[InlineData(GameMode.Taiko, (byte)1)]
	[InlineData(GameMode.Catch, (byte)2)]
	[InlineData(GameMode.Mania, (byte)3)]
	public void GameMode_MapsBothWays(GameMode domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireGameMode)wire));
	}

	[Fact]
	public void GameMode_UnknownWireValue_IsNull()
	{
		Assert.Null(WireMap.FromWire((WireGameMode)4));
	}

	[Fact]
	public void GameMode_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((GameMode)9));
	}

	[Fact]
	public void GameMode_RoundTrips()
	{
		foreach (var mode in Enum.GetValues<GameMode>()) Assert.Equal(mode, WireMap.FromWire(WireMap.ToWire(mode)));
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
	public void SlotTeam_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((GameTeam?)(GameTeam)9));
	}

	[Fact]
	public void SlotTeam_RoundTrips()
	{
		foreach (var team in Enum.GetValues<GameTeam>()) Assert.Equal(team, WireMap.FromWire(WireMap.ToWire(team)));
	}

	[Theory]
	[InlineData(GameTeamType.HeadToHead, (byte)0)]
	[InlineData(GameTeamType.TagCoop, (byte)1)]
	[InlineData(GameTeamType.TeamVs, (byte)2)]
	[InlineData(GameTeamType.TagTeamVs, (byte)3)]
	public void TeamType_MapsBothWays(GameTeamType domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireTeamType)wire));
	}

	[Fact]
	public void TeamType_UnknownWireValue_IsNull()
	{
		Assert.Null(WireMap.FromWire((WireTeamType)4));
	}

	[Fact]
	public void TeamType_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((GameTeamType)9));
	}

	[Fact]
	public void TeamType_RoundTrips()
	{
		foreach (var type in Enum.GetValues<GameTeamType>()) Assert.Equal(type, WireMap.FromWire(WireMap.ToWire(type)));
	}

	[Theory]
	[InlineData(GameWinCondition.Score, (byte)0)]
	[InlineData(GameWinCondition.Accuracy, (byte)1)]
	[InlineData(GameWinCondition.Combo, (byte)2)]
	[InlineData(GameWinCondition.ScoreV2, (byte)3)]
	public void WinCondition_MapsBothWays(GameWinCondition domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireWinCondition)wire));
	}

	[Fact]
	public void WinCondition_UnknownWireValue_IsNull()
	{
		Assert.Null(WireMap.FromWire((WireWinCondition)4));
	}

	[Fact]
	public void WinCondition_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((GameWinCondition)9));
	}

	[Fact]
	public void WinCondition_RoundTrips()
	{
		foreach (var condition in Enum.GetValues<GameWinCondition>())
			Assert.Equal(condition, WireMap.FromWire(WireMap.ToWire(condition)));
	}

	[Theory]
	[InlineData(UserActivity.Idle, (byte)0)]
	[InlineData(UserActivity.Afk, (byte)1)]
	[InlineData(UserActivity.Playing, (byte)2)]
	[InlineData(UserActivity.Editing, (byte)3)]
	[InlineData(UserActivity.Modding, (byte)4)]
	[InlineData(UserActivity.Multiplayer, (byte)5)]
	[InlineData(UserActivity.Watching, (byte)6)]
	[InlineData(UserActivity.Unknown, (byte)7)]
	[InlineData(UserActivity.Testing, (byte)8)]
	[InlineData(UserActivity.Submitting, (byte)9)]
	[InlineData(UserActivity.Paused, (byte)10)]
	[InlineData(UserActivity.Lobby, (byte)11)]
	[InlineData(UserActivity.Multiplaying, (byte)12)]
	[InlineData(UserActivity.OsuDirect, (byte)13)]
	public void UserAction_MapsBothWays(UserActivity domain, byte wire)
	{
		Assert.Equal(wire, (byte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireUserAction)wire));
	}

	[Fact]
	public void UserAction_UnknownWireValue_IsNull()
	{
		Assert.Null(WireMap.FromWire((WireUserAction)14));
	}

	[Fact]
	public void UserAction_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((UserActivity)99));
	}

	[Fact]
	public void UserAction_RoundTrips()
	{
		foreach (var activity in Enum.GetValues<UserActivity>())
			Assert.Equal(activity, WireMap.FromWire(WireMap.ToWire(activity)));
	}

	[Theory]
	[InlineData(BeatmapsetStatus.NotSubmitted, (sbyte)-1)]
	[InlineData(BeatmapsetStatus.Pending, (sbyte)0)]
	[InlineData(BeatmapsetStatus.UpdateAvailable, (sbyte)1)]
	[InlineData(BeatmapsetStatus.Ranked, (sbyte)2)]
	[InlineData(BeatmapsetStatus.Approved, (sbyte)3)]
	[InlineData(BeatmapsetStatus.Qualified, (sbyte)4)]
	[InlineData(BeatmapsetStatus.Loved, (sbyte)5)]
	public void BeatmapStatus_MapsBothWays(BeatmapsetStatus domain, sbyte wire)
	{
		Assert.Equal(wire, (sbyte)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireBeatmapStatus)wire));
	}

	[Fact]
	public void BeatmapStatus_UnknownWireValue_IsNull()
	{
		Assert.Null(WireMap.FromWire((WireBeatmapStatus)6));
	}

	[Fact]
	public void BeatmapStatus_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((BeatmapsetStatus)99));
	}

	[Fact]
	public void BeatmapStatus_RoundTrips()
	{
		foreach (var status in Enum.GetValues<BeatmapsetStatus>())
			Assert.Equal(status, WireMap.FromWire(WireMap.ToWire(status)));
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

	[Theory]
	[InlineData((byte)1)]
	[InlineData((byte)2)]
	[InlineData((byte)128)]
	[InlineData((byte)3)]
	public void SlotStatus_WireValueWithoutOccupant_IsNull(byte wire)
	{
		Assert.Null(WireMap.FromWire((WireSlotStatus)wire));
	}

	[Fact]
	public void SlotStatus_UnknownDomainValue_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => WireMap.ToWire((RoomSlotStatus)99));
	}

	[Fact]
	public void SlotStatus_RoundTrips()
	{
		foreach (var status in Enum.GetValues<RoomSlotStatus>())
			Assert.Equal(status, WireMap.FromWire(WireMap.ToWire(status)));
	}

	[Theory]
	[InlineData(GameMods.NoFail, 1u)]
	[InlineData(GameMods.Easy, 2u)]
	[InlineData(GameMods.TouchScreen, 4u)]
	[InlineData(GameMods.Hidden, 8u)]
	[InlineData(GameMods.HardRock, 16u)]
	[InlineData(GameMods.SuddenDeath, 32u)]
	[InlineData(GameMods.DoubleTime, 64u)]
	[InlineData(GameMods.Relax, 128u)]
	[InlineData(GameMods.HalfTime, 256u)]
	[InlineData(GameMods.Nightcore, 512u)]
	[InlineData(GameMods.Flashlight, 1024u)]
	[InlineData(GameMods.Autoplay, 2048u)]
	[InlineData(GameMods.SpunOut, 4096u)]
	[InlineData(GameMods.Autopilot, 8192u)]
	[InlineData(GameMods.Perfect, 16384u)]
	[InlineData(GameMods.Key4, 32768u)]
	[InlineData(GameMods.Key5, 65536u)]
	[InlineData(GameMods.Key6, 131072u)]
	[InlineData(GameMods.Key7, 262144u)]
	[InlineData(GameMods.Key8, 524288u)]
	[InlineData(GameMods.FadeIn, 1048576u)]
	[InlineData(GameMods.Random, 2097152u)]
	[InlineData(GameMods.Cinema, 4194304u)]
	[InlineData(GameMods.Target, 8388608u)]
	[InlineData(GameMods.Key9, 16777216u)]
	[InlineData(GameMods.KeyCoop, 33554432u)]
	[InlineData(GameMods.Key1, 67108864u)]
	[InlineData(GameMods.Key3, 134217728u)]
	[InlineData(GameMods.Key2, 268435456u)]
	[InlineData(GameMods.ScoreV2, 536870912u)]
	[InlineData(GameMods.Mirror, 1073741824u)]
	public void Mods_EachBit_MapsBothWays(GameMods domain, uint wire)
	{
		Assert.Equal(wire, (uint)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireMods)wire));
	}

	[Fact]
	public void Mods_Combination_MapsBothWays()
	{
		const uint hiddenDoubleTime = 8 | 64;

		Assert.Equal(hiddenDoubleTime, (uint)WireMap.ToWire(GameMods.Hidden | GameMods.DoubleTime));
		Assert.Equal(GameMods.Hidden | GameMods.DoubleTime, WireMap.FromWire((WireMods)hiddenDoubleTime));
	}

	[Fact]
	public void Mods_NoMod_IsZero()
	{
		Assert.Equal(0u, (uint)WireMap.ToWire(GameMods.NoMod));
		Assert.Equal(GameMods.NoMod, WireMap.FromWire(WireMods.NoMod));
	}

	[Fact]
	public void Mods_UnknownWireBit_IsDropped()
	{
		Assert.Equal(GameMods.Easy, WireMap.FromWire((WireMods)((1u << 31) | 2)));
	}

	[Fact]
	public void Mods_RoundTrip()
	{
		var all = Enum.GetValues<GameMods>().Aggregate(GameMods.NoMod, (all, mod) => all | mod);

		Assert.Equal(all, WireMap.FromWire(WireMap.ToWire(all)));
	}

	[Theory]
	[InlineData(UserPrivileges.Player, 1u)]
	[InlineData(UserPrivileges.Moderator, 2u)]
	[InlineData(UserPrivileges.Supporter, 4u)]
	[InlineData(UserPrivileges.Owner, 8u)]
	[InlineData(UserPrivileges.Developer, 16u)]
	[InlineData(UserPrivileges.Tournament, 32u)]
	[InlineData(UserPrivileges.Player | UserPrivileges.Supporter, 5u)]
	[InlineData(UserPrivileges.None, 0u)]
	public void Privileges_MapBothWays(UserPrivileges domain, uint wire)
	{
		Assert.Equal(wire, (uint)WireMap.ToWire(domain));
		Assert.Equal(domain, WireMap.FromWire((WireClientPrivileges)wire));
	}

	[Fact]
	public void Privileges_UnknownWireBit_IsDropped()
	{
		Assert.Equal(UserPrivileges.Player, WireMap.FromWire((WireClientPrivileges)(1u | (1u << 10))));
	}

	[Fact]
	public void Privileges_RoundTrip()
	{
		var all = Enum.GetValues<UserPrivileges>().Aggregate(UserPrivileges.None, (all, role) => all | role);

		Assert.Equal(all, WireMap.FromWire(WireMap.ToWire(all)));
	}
}
