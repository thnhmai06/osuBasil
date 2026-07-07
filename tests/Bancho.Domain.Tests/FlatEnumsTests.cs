using Bancho.Domain.Login;
using Bancho.Domain.Multiplayer;
using Bancho.Domain.Scores;
using Bancho.Domain.Users;

namespace Bancho.Domain.Tests;

/// <summary>
///     Spot-checks for the flat constant enums ported from app/constants/privileges.py,
///     app/objects/player.py (Action), and app/objects/match.py (SlotStatus, MatchTeams,
///     MatchWinConditions, MatchTeamTypes) — these carry no logic, just bit/value constants,
///     so a representative sample (rather than exhaustive per-value tests) guards against
///     transcription errors.
/// </summary>
public class FlatEnumsTests
{
    [Fact]
    public void CountryCodes_MatchesPythonTable()
    {
        Assert.Equal(252, CountryCodes.ByAcronym.Count);
        Assert.Equal(1, CountryCodes.ByAcronym["oc"]);
        Assert.Equal(225, CountryCodes.ByAcronym["us"]);
        Assert.Equal(111, CountryCodes.ByAcronym["jp"]);
        Assert.Equal(244, CountryCodes.ByAcronym["xx"]);
        Assert.Equal(252, CountryCodes.ByAcronym["mf"]);
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
        Assert.Equal(1 << 0, (int)Privileges.Unrestricted);
        Assert.Equal(1 << 1, (int)Privileges.Verified);
        Assert.Equal(1 << 13, (int)Privileges.Administrator);
        Assert.Equal(1 << 14, (int)Privileges.Developer);
        Assert.Equal(Privileges.Supporter | Privileges.Premium, Privileges.Donator);
        Assert.Equal(Privileges.Moderator | Privileges.Administrator | Privileges.Developer, Privileges.Staff);
    }

    [Fact]
    public void ClientPrivileges_MatchesPythonBitValues()
    {
        Assert.Equal(1 << 0, (int)ClientPrivileges.Player);
        Assert.Equal(1 << 3, (int)ClientPrivileges.Owner);
        Assert.Equal(1 << 5, (int)ClientPrivileges.Tournament);
    }

    [Fact]
    public void ClanPrivileges_MatchesPythonValues()
    {
        Assert.Equal(1, (int)ClanPrivileges.Member);
        Assert.Equal(2, (int)ClanPrivileges.Officer);
        Assert.Equal(3, (int)ClanPrivileges.Owner);
    }

    [Fact]
    public void Action_MatchesPythonValues()
    {
        Assert.Equal(0, (int)Action.Idle);
        Assert.Equal(2, (int)Action.Playing);
        Assert.Equal(13, (int)Action.OsuDirect);
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
        Assert.Equal(0, (int)MatchTeams.Neutral);
        Assert.Equal(1, (int)MatchTeams.Blue);
        Assert.Equal(2, (int)MatchTeams.Red);
    }

    [Fact]
    public void MatchWinConditions_MatchesPythonValues()
    {
        Assert.Equal(0, (int)MatchWinConditions.Score);
        Assert.Equal(1, (int)MatchWinConditions.Accuracy);
        Assert.Equal(2, (int)MatchWinConditions.Combo);
        Assert.Equal(3, (int)MatchWinConditions.ScoreV2);
    }

    [Fact]
    public void MatchTeamTypes_MatchesPythonValues()
    {
        Assert.Equal(0, (int)MatchTeamTypes.HeadToHead);
        Assert.Equal(1, (int)MatchTeamTypes.TagCoop);
        Assert.Equal(2, (int)MatchTeamTypes.TeamVs);
        Assert.Equal(3, (int)MatchTeamTypes.TagTeamVs);
    }

    [Fact]
    public void SubmissionStatus_MatchesPythonValues()
    {
        Assert.Equal(0, (int)SubmissionStatus.Failed);
        Assert.Equal(1, (int)SubmissionStatus.Submitted);
        Assert.Equal(2, (int)SubmissionStatus.Best);
    }

    [Fact]
    public void LeaderboardType_MatchesPythonValues()
    {
        Assert.Equal(0, (int)LeaderboardType.Local);
        Assert.Equal(1, (int)LeaderboardType.Top);
        Assert.Equal(2, (int)LeaderboardType.Mods);
        Assert.Equal(3, (int)LeaderboardType.Friends);
        Assert.Equal(4, (int)LeaderboardType.Country);
    }
}