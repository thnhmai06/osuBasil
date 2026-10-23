using Basil.Domain.Mechanics;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Common;

public class FlatEnumsTests
{
	[Fact]
	public void CountryCodes_MatchesPythonTable()
	{
		Assert.Equal(252, Enum.GetValues<Country>().Length);
		Assert.Equal((byte)1, (byte)Country.Oc);
		Assert.Equal((byte)225, (byte)Country.Us);
		Assert.Equal((byte)111, (byte)Country.Jp);
		Assert.Equal((byte)244, (byte)Country.Xx);
		Assert.Equal((byte)252, (byte)Country.Mf);
	}

	[Fact]
	public void PresenceVisibility_MatchesPythonValues()
	{
		Assert.Equal(0, (int)PresenceVisibility.Nil);
		Assert.Equal(1, (int)PresenceVisibility.All);
		Assert.Equal(2, (int)PresenceVisibility.Friends);
	}

	[Fact]
	public void Privileges_MatchesClientBitValues()
	{
		Assert.Equal(1 << 0, (int)Privileges.Player);
		Assert.Equal(1 << 3, (int)Privileges.Owner);
		Assert.Equal(1 << 5, (int)Privileges.Tournament);
	}

	[Fact]
	public void UserActivity_MatchesPythonValues()
	{
		Assert.Equal(0, (int)UserActivity.Idle);
		Assert.Equal(2, (int)UserActivity.Playing);
		Assert.Equal(13, (int)UserActivity.OsuDirect);
	}

	[Fact]
	public void GameTeams_MatchPythonValues()
	{
		Assert.Equal(1, (int)GameTeam.Blue);
		Assert.Equal(2, (int)GameTeam.Red);
	}

	[Fact]
	public void GameWinConditions_MatchPythonValues()
	{
		Assert.Equal(0, (int)GameWinCondition.Score);
		Assert.Equal(1, (int)GameWinCondition.Accuracy);
		Assert.Equal(2, (int)GameWinCondition.Combo);
		Assert.Equal(3, (int)GameWinCondition.ScoreV2);
	}

	[Fact]
	public void GameTeamTypes_MatchPythonValues()
	{
		Assert.Equal(0, (int)GameTeamType.HeadToHead);
		Assert.Equal(1, (int)GameTeamType.TagCoop);
		Assert.Equal(2, (int)GameTeamType.TeamVs);
		Assert.Equal(3, (int)GameTeamType.TagTeamVs);
	}
}