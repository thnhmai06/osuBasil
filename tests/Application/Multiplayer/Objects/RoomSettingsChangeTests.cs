using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Objects;

/// <summary>Which changes of a room's settings alter the round that the room plays next.</summary>
public sealed class RoomSettingsChangeTests
{
	public static TheoryData<RoomSettingsChange> RoundChanges => new()
	{
		new RoomSettingsChange(Beatmap: RoomFixture.Beatmap),
		new RoomSettingsChange(ClearBeatmap: true),
		new RoomSettingsChange(Mode: GameMode.Taiko),
		new RoomSettingsChange(Mods: GameMods.Hidden),
		new RoomSettingsChange(Freemods: true),
		new RoomSettingsChange(TeamType: GameTeamType.TeamVs),
		new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy)
	};

	public static TheoryData<RoomSettingsChange> OtherChanges => new()
	{
		new RoomSettingsChange(),
		new RoomSettingsChange(Name: "Renamed"),
		new RoomSettingsChange(Password: "secret"),
		new RoomSettingsChange(Size: 8),
		new RoomSettingsChange(IsPrivate: true)
	};

	[Theory]
	[MemberData(nameof(RoundChanges))]
	public void AltersTheRound_ForAChangeOfWhatTheRoundPlays_IsTrue(RoomSettingsChange change)
	{
		Assert.True(change.AltersTheRound);
	}

	[Theory]
	[MemberData(nameof(OtherChanges))]
	public void AltersTheRound_ForAnyOtherChange_IsFalse(RoomSettingsChange change)
	{
		Assert.False(change.AltersTheRound);
	}
}
