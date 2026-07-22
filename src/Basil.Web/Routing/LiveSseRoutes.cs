using System.Net.ServerSentEvents;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;

namespace Basil.Web.Routing;

/// <summary>
///     ASP.NET Core's native SSE support (<c>TypedResults.ServerSentEvents</c>) for the api. host's
///     three live TRT channels. These are server-to-client push only — no client message is ever
///     expected — so each connection just subscribes a C# event (<see cref="IMatchLiveEvents" />/
///     <see cref="IPlayerInputEvents" />) into its own bounded, drop-oldest <see cref="Channel{T}" />
///     and hands that straight to the framework; <see cref="Channel{T}" />'s reader is already an
///     <see cref="IAsyncEnumerable{T}" />, so no hand-written iterator is needed. Publishing is a
///     non-blocking event raise plus a non-blocking <c>ChannelWriter.TryWrite</c>, so a slow or dead
///     subscriber can never stall the publisher, which is often still holding <c>MatchSession.Lock</c>.
///     <c>EventType</c> tags each stream ("main"/"playerScore"/"input") so a client can
///     <c>EventSource.addEventListener</c> per feed; <c>EventId</c>/resumption is deliberately not
///     used since these feeds have no backlog to resume from after a reconnect.
/// </summary>
internal static class LiveSseRoutes
{
    public static IResult HandleMain(int matchId, IMatchLiveEvents events, CancellationToken cancellationToken)
    {
        return TypedResults.ServerSentEvents(Subscribe(cancellationToken, "main", publish =>
        {
            void Handler(int id, byte[] payload)
            {
                if (id == matchId) publish(payload);
            }

            events.MainPublished += Handler;
            return () => events.MainPublished -= Handler;
        }));
    }

    public static IResult HandlePlayer(int matchId, string playerName, IMatchLiveEvents events,
        CancellationToken cancellationToken)
    {
        return TypedResults.ServerSentEvents(Subscribe(cancellationToken, "playerScore", publish =>
        {
            void Handler(int id, string name, byte[] payload)
            {
                if (id == matchId && name == playerName) publish(payload);
            }

            events.PlayerScorePublished += Handler;
            return () => events.PlayerScorePublished -= Handler;
        }));
    }

    public static IResult HandleInput(int playerId, IPlayerInputEvents events, CancellationToken cancellationToken)
    {
        return TypedResults.ServerSentEvents(Subscribe(cancellationToken, "input", publish =>
        {
            void Handler(int id, byte[] payload)
            {
                if (id == playerId) publish(payload);
            }

            events.InputPublished += Handler;
            return () => events.InputPublished -= Handler;
        }));
    }

    private static IAsyncEnumerable<SseItem<string>> Subscribe(
        CancellationToken cancellationToken, string eventType, Func<Action<byte[]>, Action> subscribe)
    {
        var channel = Channel.CreateBounded<SseItem<string>>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
        var unsubscribe = subscribe(payload =>
            channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
        cancellationToken.Register(unsubscribe);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }
}
