using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Tests.Sessions;

/// <summary>Ported from app/objects/player.py's Player — session state for online players.</summary>
public class PlayerSessionTests
{
    private static PlayerSession MakeSession(UserPrivileges priv)
    {
        return new PlayerSession(1000, "cmyui", "some-token", priv, DateTimeOffset.UnixEpoch.AddSeconds(1000));
    }

    [Fact]
    public void BanchoPriv_Unrestricted_MapsToPlayer()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);

        Assert.Equal(ClientPrivileges.Player, session.BanchoPriv);
    }

    [Fact]
    public void BanchoPriv_MapsEachServerPrivilegeToItsClientEquivalent()
    {
        var session = MakeSession(
            UserPrivileges.Unrestricted | UserPrivileges.Donator | UserPrivileges.Moderator
            | UserPrivileges.Administrator | UserPrivileges.Developer);

        var expected = ClientPrivileges.Player | ClientPrivileges.Supporter | ClientPrivileges.Moderator
                       | ClientPrivileges.Developer | ClientPrivileges.Owner;
        Assert.Equal(expected, session.BanchoPriv);
    }

    [Fact]
    public void BanchoPriv_NoPrivileges_IsZero()
    {
        var session = MakeSession(0);

        Assert.Equal((ClientPrivileges)0, session.BanchoPriv);
    }

    [Fact]
    public void Restricted_WithoutUnrestrictedPrivilege_IsTrue()
    {
        var session = MakeSession(UserPrivileges.Verified);

        Assert.True(session.Restricted);
    }

    [Fact]
    public void Restricted_WithUnrestrictedPrivilege_IsFalse()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);

        Assert.False(session.Restricted);
    }

    [Fact]
    public void SafeName_NormalizesViaSafeNameMake()
    {
        var session = new PlayerSession(1, "Cool Guy", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

        Assert.Equal("cool_guy", session.SafeName);
    }

    [Fact]
    public void Silenced_SilenceEndInFuture_IsTrue()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);
        session.SilenceEnd = DateTimeOffset.UtcNow.AddSeconds(60);

        Assert.True(session.Silenced);
        Assert.True(session.RemainingSilence > TimeSpan.Zero);
    }

    [Fact]
    public void Silenced_SilenceEndInPast_IsFalse()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);
        session.SilenceEnd = DateTimeOffset.UtcNow.AddSeconds(-60);

        Assert.False(session.Silenced);
        Assert.Equal(TimeSpan.Zero, session.RemainingSilence);
    }

    [Fact]
    public void EnqueueThenDequeue_ReturnsConcatenatedBytesAndClearsQueue()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);

        session.Enqueue([1, 2, 3]);
        session.Enqueue([4, 5]);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, session.Dequeue());
        Assert.Empty(session.Dequeue());
    }

    [Fact]
    public async Task Enqueue_IsThreadSafe_NoDataLostUnderConcurrentWrites()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);
        const int writersCount = 50;

        await Task.WhenAll(Enumerable.Range(0, writersCount).Select(i =>
            Task.Run(() => session.Enqueue([(byte)i]))));

        var result = session.Dequeue();
        Assert.Equal(writersCount, result.Length);
    }

    [Fact]
    public void JoinChannel_TracksMembership()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);

        session.JoinChannel("#osu");

        Assert.True(session.InChannel("#osu"));
        Assert.Contains("#osu", session.Channels);
    }

    [Fact]
    public void LeaveChannel_RemovesMembership()
    {
        var session = MakeSession(UserPrivileges.Unrestricted);
        session.JoinChannel("#osu");

        session.LeaveChannel("#osu");

        Assert.False(session.InChannel("#osu"));
        Assert.DoesNotContain("#osu", session.Channels);
    }
}