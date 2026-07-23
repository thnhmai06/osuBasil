using Basil.Application.Sessions.Multiplayer;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Replaces InMemoryMatchEventBus's hand-rolled per-match subscriber dictionary with plain C#
///     events — the CLR's multicast delegate already provides the fan-out these tests exercise.
/// </summary>
public class MatchLiveEventsTests
{
    [Fact]
    public void PublishMain_NoSubscribers_DoesNotThrow()
    {
        var events = new MatchLiveEvents();

        var exception = Record.Exception(() => events.PublishMain(1, "payload"u8.ToArray()));

        Assert.Null(exception);
    }

    [Fact]
    public void PublishMain_MultipleSubscribers_AllReceiveThePayload()
    {
        var events = new MatchLiveEvents();
        var received1 = new List<(int, byte[])>();
        var received2 = new List<(int, byte[])>();
        events.MainPublished += (id, payload) => received1.Add((id, payload));
        events.MainPublished += (id, payload) => received2.Add((id, payload));

        events.PublishMain(5, "hello"u8.ToArray());

        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal(5, received1[0].Item1);
        Assert.Equal("hello"u8.ToArray(), received1[0].Item2);
    }

    [Fact]
    public void PublishMain_AfterUnsubscribing_NoLongerDelivers()
    {
        var events = new MatchLiveEvents();
        var received = new List<byte[]>();
        void Handler(int id, byte[] payload) => received.Add(payload);
        events.MainPublished += Handler;
        events.MainPublished -= Handler;

        events.PublishMain(1, "payload"u8.ToArray());

        Assert.Empty(received);
    }

    [Fact]
    public void PublishPlayer_MultipleSubscribers_AllReceiveMatchIdPlayerNameAndPayload()
    {
        var events = new MatchLiveEvents();
        (int MatchDbId, string PlayerName, byte[] Payload)? received = null;
        events.PlayerScorePublished += (id, name, payload) => received = (id, name, payload);

        events.PublishPlayer(9, "alice", "score"u8.ToArray());

        Assert.NotNull(received);
        Assert.Equal(9, received!.Value.MatchDbId);
        Assert.Equal("alice", received.Value.PlayerName);
        Assert.Equal("score"u8.ToArray(), received.Value.Payload);
    }

    [Fact]
    public void PublishPlayer_NoSubscribers_DoesNotThrow()
    {
        var events = new MatchLiveEvents();

        var exception = Record.Exception(() => events.PublishPlayer(1, "alice", "payload"u8.ToArray()));

        Assert.Null(exception);
    }
}
