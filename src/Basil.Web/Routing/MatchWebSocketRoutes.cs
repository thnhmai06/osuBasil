using System.Net.WebSockets;
using System.Threading.Channels;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Web.Routing;

/// <summary>
///     Raw ASP.NET Core WebSockets (no SignalR) for the api. host's three live TRT channels. These
///     are server-to-client push only — no client message is ever expected — so each connection is
///     just an outbound pump reading its own bounded channel, fed by <see cref="IMatchEventBus" />.
///     Publishing is a non-blocking best-effort write into that channel (see IMatchEventBus's doc
///     comment), so a slow or dead subscriber's socket write can never stall the publisher, which is
///     often still holding <c>MatchSession.Lock</c>.
/// </summary>
internal static class MatchWebSocketRoutes
{
    public static Task HandleMainAsync(int matchId, HttpContext context, CancellationToken cancellationToken)
    {
        var eventBus = context.RequestServices.GetRequiredService<IMatchEventBus>();
        return RunAsync(context, writer => eventBus.SubscribeMain(matchId, writer), cancellationToken);
    }

    public static Task HandlePlayerAsync(int matchId, string playerName, HttpContext context,
        CancellationToken cancellationToken)
    {
        var eventBus = context.RequestServices.GetRequiredService<IMatchEventBus>();
        return RunAsync(context, writer => eventBus.SubscribePlayer(matchId, playerName, writer), cancellationToken);
    }

    public static Task HandleInputAsync(int matchId, HttpContext context, CancellationToken cancellationToken)
    {
        var eventBus = context.RequestServices.GetRequiredService<IMatchEventBus>();
        return RunAsync(context, writer => eventBus.SubscribeInput(matchId, writer), cancellationToken);
    }

    private static async Task RunAsync(HttpContext context, Func<ChannelWriter<byte[]>, IDisposable> subscribe,
        CancellationToken cancellationToken)
    {
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var channel = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
        using var subscription = subscribe(channel.Writer);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (socket.State != WebSocketState.Open) break;
                await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the client disconnects (cancellationToken is HttpContext.RequestAborted).
        }
        catch (WebSocketException)
        {
            // The client went away mid-send.
        }

        if (socket.State == WebSocketState.Open)
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // ponytail: best-effort close — the client may already be gone.
            }
    }
}