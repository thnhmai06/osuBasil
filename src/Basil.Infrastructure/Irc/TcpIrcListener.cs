using System.Net;
using System.Net.Sockets;
using Basil.Application.Configuration;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     Embedded IRC gateway — accepts raw TCP connections on <see cref="IrcOptions.Port" /> and hands
///     each one off to its own <see cref="TcpIrcConnection" />. Runs in-process alongside Basil.Web's
///     Kestrel host (no separate executable, no Docker), matching the "embedded" decision in
///     docs/architecture.md's chat section.
/// </summary>
public sealed class TcpIrcListener(
    IOptions<IrcOptions> options,
    IrcAuthenticationService authService,
    ChatDispatchService chatDispatch,
    ChannelMembershipService channelMembership,
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ILogger<TcpIrcListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, options.Value.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            // A chat gateway failing to bind shouldn't take the whole server down (e.g. port already
            // in use) — log and skip, rather than letting BackgroundService's unhandled-exception
            // behaviour crash host startup entirely.
            logger.LogError(ex, "IRC gateway failed to bind port {Port} — IRC will be unavailable.",
                options.Value.Port);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                var connection = new TcpIrcConnection(
                    client, authService, chatDispatch, channelMembership, channelRegistry, sessionRegistry, options);

                _ = RunConnectionAsync(connection, client, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunConnectionAsync(TcpIrcConnection connection, TcpClient client,
        CancellationToken stoppingToken)
    {
        try
        {
            await connection.RunAsync(stoppingToken);
        }
        catch (IOException)
        {
            // The client dropped the connection mid-read/write.
        }
        catch (SocketException)
        {
            // Same as above, at the socket layer.
        }
        finally
        {
            client.Dispose();
        }
    }
}
