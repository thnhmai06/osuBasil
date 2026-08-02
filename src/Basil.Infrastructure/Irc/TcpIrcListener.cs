using System.Net;
using System.Net.Sockets;
using Basil.Application.Configurations;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     Embedded IRC gateway: accepts raw TCP connections on <see cref="IrcOptions.Port" /> and hands
///     each one off to its own <see cref="TcpIrcConnection" />. Runs in-process as a
///     <see cref="BackgroundService" />, so no separate executable or container is required.
/// </summary>
/// <remarks>
///     A listener that fails to bind its port logs the error and stops, rather than throwing out of
///     <see cref="BackgroundService" />'s execution path and taking the host down with it. Each
///     accepted connection is assigned a fresh per-process id and runs detached.
/// </remarks>
public sealed class TcpIrcListener(
	IOptions<IrcOptions> options,
	IrcAuthenticationService authService,
	ChatDispatchService chatDispatch,
	ChannelMembershipService channelMembership,
	IChannelRegistry channelRegistry,
	IUserSessionRegistry sessionRegistry,
	ILogger<TcpIrcListener> logger,
	ILogger<TcpIrcConnection> connectionLogger) : BackgroundService
{
	/// <summary>
	///     The next connection id to assign. Unique for the lifetime of this server process, never
	///     persisted or reused.
	/// </summary>
	private static long _nextConnectionId;

	/// <inheritdoc />
	/// <remarks>
	///     When the listener cannot bind (for example the port is already in use), an error is logged
	///     and the method returns so the rest of the host keeps running with chat unavailable.
	/// </remarks>
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
				var connectionId = Interlocked.Increment(ref _nextConnectionId);
				logger.LogDebug("IRC connection accepted: ConnectionId={ConnectionId} RemoteEndPoint={RemoteEndPoint}",
					connectionId, client.Client.RemoteEndPoint);

				var connection = new TcpIrcConnection(
					client, authService, chatDispatch, channelMembership, channelRegistry, sessionRegistry, options,
					connectionLogger, connectionId);

				_ = RunConnectionAsync(connection, client, connectionId, stoppingToken);
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

	/// <summary>
	///     Runs a single connection to completion, swallowing socket and IO errors that mean the
	///     client dropped mid-read/write, and always disposing the underlying <see cref="TcpClient" />.
	/// </summary>
	private async Task RunConnectionAsync(TcpIrcConnection connection, TcpClient client, long connectionId,
		CancellationToken stoppingToken)
	{
		try
		{
			await connection.RunAsync(stoppingToken);
		}
		catch (IOException)
		{
			// The client dropped the connection mid-read/write.
			logger.LogDebug("IRC connection dropped mid-read/write: ConnectionId={ConnectionId}", connectionId);
		}
		catch (SocketException)
		{
			// Same as above, at the socket layer.
			logger.LogDebug("IRC connection dropped (socket error): ConnectionId={ConnectionId}", connectionId);
		}
		finally
		{
			client.Dispose();
		}
	}
}