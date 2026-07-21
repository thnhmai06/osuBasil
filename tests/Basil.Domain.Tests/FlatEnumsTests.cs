using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Domain.Tests;

/// <summary>
///     Spot-checks for the flat constant enums ported from app/constants/privileges.py,
///     app/objects/player.py (UserActivity), and app/objects/match.py (SlotStatus, MatchTeam,
///     MatchWinCondition, MatchTeamType) — these carry no logic, just bit/value constants,
///     so a representative sample (rather than exhaustive per-value tests) guards against
///     transcription errors.
/// </summary>
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
    public void PresenceFilter_MatchesPythonValues()
    {
        Assert.Equal(0, (int)PresenceFilter.Nil);
        Assert.Equal(1, (int)PresenceFilter.All);
        Assert.Equal(2, (int)PresenceFilter.Friends);
    }

    [Fact]
    public void Privileges_MatchesPythonBitValues()
    {
        Assert.Equal(1 << 0, (int)UserPrivileges.Unrestricted);
        Assert.Equal(1 << 1, (int)UserPrivileges.Verified);
        Assert.Equal(1 << 13, (int)UserPrivileges.Administrator);
        Assert.Equal(1 << 14, (int)UserPrivileges.Developer);
        Assert.Equal(UserPrivileges.Supporter | UserPrivileges.Premium, UserPrivileges.Donator);
        Assert.Equal(UserPrivileges.Moderator | UserPrivileges.Administrator | UserPrivileges.Developer, UserPrivileges.Staff);
    }

    [Fact]
    public void ClientPrivileges_MatchesPythonBitValues()
    {
        Assert.Equal(1 << 0, (int)ClientPrivileges.Player);
        Assert.Equal(1 << 3, (int)ClientPrivileges.Owner);
        Assert.Equal(1 << 5, (int)ClientPrivileges.Tournament);
    }

    [Fact]
    public void Action_MatchesPythonValues()
    {
        Assert.Equal(0, (int)UserActivity.Idle);
        Assert.Equal(2, (int)UserActivity.Playing);
        Assert.Equal(13, (int)UserActivity.OsuDirect);
    }

    [Fact]
    public void SlotStatus_MatchesPythonBitValues_AndHasPlayerMask()
    {
        Assert.Equal(1, (int)SlotStatus.Open);
        Assert.Equal(128, (int)SlotStatus.Quit);

        // matches the 0b01111100 magic number used directly in app/packets.py's write_match/read_match
        var hasPlayerMask = SlotStatus.NotReady | SlotStatus.Ready | SlotStatus.NoMap
                            | SlotStatus.Playing | SlotStatus.Complete;
        Assert.Equal(0b0111_1100, (int)hasPlayerMask);
    }

    [Fact]
    public void MatchTeams_MatchesPythonValues()
    {
        Assert.Equal(0, (int)MatchTeam.Neutral);
        Assert.Equal(1, (int)MatchTeam.Blue);
        Assert.Equal(2, (int)MatchTeam.Red);
    }

    [Fact]
    public void MatchWinConditions_MatchesPythonValues()
    {
        Assert.Equal(0, (int)MatchWinCondition.Score);
        Assert.Equal(1, (int)MatchWinCondition.Accuracy);
        Assert.Equal(2, (int)MatchWinCondition.Combo);
        Assert.Equal(3, (int)MatchWinCondition.ScoreV2);
    }

    [Fact]
    public void MatchTeamTypes_MatchesPythonValues()
    {
        Assert.Equal(0, (int)MatchTeamType.HeadToHead);
        Assert.Equal(1, (int)MatchTeamType.TagCoop);
        Assert.Equal(2, (int)MatchTeamType.TeamVs);
        Assert.Equal(3, (int)MatchTeamType.TagTeamVs);
    }

}