using Basil.Application.Sessions.Spectating;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Player-scoped sibling of MatchLiveEvents, feeding the /spec/{id} SSE channel — keyed by
///     player id rather than match id, since input frames are published regardless of match
///     membership (see SpectateFramesHandler).
/// </summary>
public class PlayerInputEventsTests
{
    [Fact]
    public void PublishInput_NoSubscribers_DoesNotThrow()
    {
        var events = new PlayerInputEvents();

        var exception = Record.Exception(() => events.PublishInput(1, "payload"u8.ToArray()));

        Assert.Null(exception);
    }

    [Fact]
    public void PublishInput_MultipleSubscribers_AllReceiveThePayload()
    {
        var events = new PlayerInputEvents();
        var received1 = new List<(int, byte[])>();
        var received2 = new List<(int, byte[])>();
        events.InputPublished += (id, payload) => received1.Add((id, payload));
        events.InputPublished += (id, payload) => received2.Add((id, payload));

        events.PublishInput(7, "frame"u8.ToArray());

        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal(7, received1[0].Item1);
        Assert.Equal("frame"u8.ToArray(), received1[0].Item2);
    }

    [Fact]
    public void PublishInput_AfterUnsubscribing_NoLongerDelivers()
    {
        var events = new PlayerInputEvents();
        var received = new List<byte[]>();
        void Handler(int id, byte[] payload) => received.Add(payload);
        events.InputPublished += Handler;
        events.InputPublished -= Handler;

        events.PublishInput(1, "payload"u8.ToArray());

        Assert.Empty(received);
    }
}
