using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Tests.Sessions;

/// <summary>Ported from app/objects/channel.py's Channel — can_read/can_write + live membership tracking.</summary>
public class ChannelSessionTests
{
    [Fact]
    public void CanRead_ZeroReadPriv_AlwaysTrue()
    {
        var channel = new ChannelSession(1, "#osu", "topic", readPriv: 0, writePriv: 2, autoJoin: true);

        Assert.True(channel.CanRead((Privileges)0));
    }

    [Fact]
    public void CanRead_OverlappingBit_IsTrue()
    {
        var channel = new ChannelSession(1, "#staff", "topic", readPriv: (int)Privileges.Staff, writePriv: (int)Privileges.Staff, autoJoin: true);

        Assert.True(channel.CanRead(Privileges.Moderator));
    }

    [Fact]
    public void CanRead_NoOverlappingBit_IsFalse()
    {
        var channel = new ChannelSession(1, "#staff", "topic", readPriv: (int)Privileges.Staff, writePriv: (int)Privileges.Staff, autoJoin: true);

        Assert.False(channel.CanRead(Privileges.Unrestricted | Privileges.Verified));
    }

    [Fact]
    public void CanWrite_ZeroWritePriv_AlwaysTrue()
    {
        var channel = new ChannelSession(1, "#osu", "topic", readPriv: 1, writePriv: 0, autoJoin: true);

        Assert.True(channel.CanWrite((Privileges)0));
    }

    [Fact]
    public void JoinThenPart_UpdatesPlayerCount()
    {
        var channel = new ChannelSession(1, "#osu", "topic", readPriv: 0, writePriv: 0, autoJoin: true);

        channel.Join(1);
        channel.Join(2);
        Assert.Equal(2, channel.PlayerCount);
        Assert.True(channel.Contains(1));

        channel.Part(1);
        Assert.Equal(1, channel.PlayerCount);
        Assert.False(channel.Contains(1));
    }

    [Fact]
    public void DisplayName_DefaultsToName()
    {
        var channel = new ChannelSession(1, "#osu", "topic", 0, 0, true);

        Assert.Equal("#osu", channel.DisplayName);
    }

    [Fact]
    public void DisplayName_CanDifferFromRegistryName()
    {
        var channel = new ChannelSession(0, "#spec_5", "topic", 0, 0, false, displayName: "#spectator", instance: true);

        Assert.Equal("#spec_5", channel.Name);
        Assert.Equal("#spectator", channel.DisplayName);
        Assert.True(channel.Instance);
    }

    [Fact]
    public void MemberIds_ReflectsJoinsAndParts()
    {
        var channel = new ChannelSession(1, "#osu", "topic", 0, 0, true);
        channel.Join(1);
        channel.Join(2);

        Assert.Equal(new[] { 1, 2 }, channel.MemberIds.OrderBy(id => id));

        channel.Part(1);

        Assert.Equal([2], channel.MemberIds);
    }
}
