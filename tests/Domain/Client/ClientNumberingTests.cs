using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Client;

/// <summary>
///     Domain enums carry the numbering of the osu! (stable) client, because the protocol uses them directly: these are
///     the numbers of the client's documented enums, every member pinned.
/// </summary>
public sealed class ClientNumberingTests
{
	[Theory]
	[InlineData(GameMode.Standard, 0)]
	[InlineData(GameMode.Taiko, 1)]
	[InlineData(GameMode.Catch, 2)]
	[InlineData(GameMode.Mania, 3)]
	public void GameMode_UsesTheClientNumbers(GameMode value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(GameTeamType.HeadToHead, 0)]
	[InlineData(GameTeamType.TagCoop, 1)]
	[InlineData(GameTeamType.TeamVs, 2)]
	[InlineData(GameTeamType.TagTeamVs, 3)]
	public void TeamType_UsesTheClientNumbers(GameTeamType value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(GameWinCondition.Score, 0)]
	[InlineData(GameWinCondition.Accuracy, 1)]
	[InlineData(GameWinCondition.Combo, 2)]
	[InlineData(GameWinCondition.ScoreV2, 3)]
	public void WinCondition_UsesTheClientNumbers(GameWinCondition value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(UserActivity.Idle, 0)]
	[InlineData(UserActivity.Afk, 1)]
	[InlineData(UserActivity.Playing, 2)]
	[InlineData(UserActivity.Editing, 3)]
	[InlineData(UserActivity.Modding, 4)]
	[InlineData(UserActivity.Multiplayer, 5)]
	[InlineData(UserActivity.Watching, 6)]
	[InlineData(UserActivity.Unknown, 7)]
	[InlineData(UserActivity.Testing, 8)]
	[InlineData(UserActivity.Submitting, 9)]
	[InlineData(UserActivity.Paused, 10)]
	[InlineData(UserActivity.Lobby, 11)]
	[InlineData(UserActivity.Multiplaying, 12)]
	[InlineData(UserActivity.OsuDirect, 13)]
	public void Activity_UsesTheClientNumbers(UserActivity value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(BeatmapsetStatus.NotSubmitted, -1)]
	[InlineData(BeatmapsetStatus.Pending, 0)]
	[InlineData(BeatmapsetStatus.UpdateAvailable, 1)]
	[InlineData(BeatmapsetStatus.Ranked, 2)]
	[InlineData(BeatmapsetStatus.Approved, 3)]
	[InlineData(BeatmapsetStatus.Qualified, 4)]
	[InlineData(BeatmapsetStatus.Loved, 5)]
	public void BeatmapStatus_UsesTheClientNumbers(BeatmapsetStatus value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(GameMods.NoFail, 1)]
	[InlineData(GameMods.Easy, 2)]
	[InlineData(GameMods.TouchScreen, 4)]
	[InlineData(GameMods.Hidden, 8)]
	[InlineData(GameMods.HardRock, 16)]
	[InlineData(GameMods.SuddenDeath, 32)]
	[InlineData(GameMods.DoubleTime, 64)]
	[InlineData(GameMods.Relax, 128)]
	[InlineData(GameMods.HalfTime, 256)]
	[InlineData(GameMods.Nightcore, 512)]
	[InlineData(GameMods.Flashlight, 1024)]
	[InlineData(GameMods.Autoplay, 2048)]
	[InlineData(GameMods.SpunOut, 4096)]
	[InlineData(GameMods.Autopilot, 8192)]
	[InlineData(GameMods.Perfect, 16384)]
	[InlineData(GameMods.Key4, 32768)]
	[InlineData(GameMods.Key5, 65536)]
	[InlineData(GameMods.Key6, 131072)]
	[InlineData(GameMods.Key7, 262144)]
	[InlineData(GameMods.Key8, 524288)]
	[InlineData(GameMods.FadeIn, 1048576)]
	[InlineData(GameMods.Random, 2097152)]
	[InlineData(GameMods.Cinema, 4194304)]
	[InlineData(GameMods.Target, 8388608)]
	[InlineData(GameMods.Key9, 16777216)]
	[InlineData(GameMods.KeyCoop, 33554432)]
	[InlineData(GameMods.Key1, 67108864)]
	[InlineData(GameMods.Key3, 134217728)]
	[InlineData(GameMods.Key2, 268435456)]
	[InlineData(GameMods.ScoreV2, 536870912)]
	[InlineData(GameMods.Mirror, 1073741824)]
	public void Mods_UseTheClientBits(GameMods value, int number)
	{
		Assert.Equal(number, (int)value);
	}

	[Theory]
	[InlineData(UserPrivileges.Player, 1)]
	[InlineData(UserPrivileges.Moderator, 2)]
	[InlineData(UserPrivileges.Supporter, 4)]
	[InlineData(UserPrivileges.Owner, 8)]
	[InlineData(UserPrivileges.Developer, 16)]
	[InlineData(UserPrivileges.Tournament, 32)]
	[InlineData(UserPrivileges.None, 0)]
	public void Privileges_UseTheClientBits(UserPrivileges value, int number)
	{
		Assert.Equal(number, (int)value);
	}
}
